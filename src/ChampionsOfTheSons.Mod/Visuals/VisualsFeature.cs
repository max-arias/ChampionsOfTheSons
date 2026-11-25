using ChampionsOfTheSons.Mod.Bundles;
using ChampionsOfTheSons.Mod.Spells;
using System.Linq;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// Bundled visuals (COS-63, COS-102): the "reduce heavy effects" setting, effects for the player's toggles and long buffs
/// (Black Flame and Multishot at the hand, Berserk and Golden Skin at the feet), and the self-tests that show every drop
/// model and effect on screen and measure the frame cost of many effects.
/// </summary>
public sealed class VisualsFeature : IModFeature
{
    private static ConfigEntry<bool>? _reduce;

    /// <summary>Hides hand flames and glyphs, halves particles and drops non-essential lights. Track B's settings overlay shows it.</summary>
    public static bool ReduceHeavyEffects => _reduce?.Value ?? false;

    /// <summary>The setting itself (settings overlay).</summary>
    public static ConfigEntry<bool>? ReduceEntry => _reduce;

    public static void SetReduceHeavyEffects(bool on)
    {
        if (_reduce != null) _reduce.Value = on;
    }

    public void Init()
    {
        _reduce = Config.Category.CreateEntry("reduce_heavy_effects", false, "Reduce heavy effects",
            "Hide hand flames and glyphs, halve spell particles and use fewer effect lights.");
    }

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld)
        {
            PlayerStateFx.Clear();
            ScreenFx.Clear();
            return;
        }
        PlayerStateFx.Update();
        ScreenFx.Update(Time.unscaledDeltaTime);
        FrameMeter.Sample(Time.unscaledDeltaTime);
    }

    // ---------------------------------------------------------------- self-tests

    private readonly List<GameObject> _shown = new();

    public void RegisterSelfTests()
    {
        // The test save opens a "Game Loaded" modal over a blurred pause menu: close both before the screenshots.
        SelfTest.Add(0.3f, "visuals: unpause", () =>
        {
            UI.UiCapture.DismissDialogs();
            TestKit.Unpause();
        });
        SelfTest.Add(1f, "visuals: drop models", ShowDrops);
        SelfTest.Add(3f, "visuals: shot drops", () => TestKit.Screenshot("visuals-01-drops"));
        SelfTest.Add(3.5f, "visuals: effects", ShowEffects);
        SelfTest.Add(5.5f, "visuals: shot effects", () => TestKit.Screenshot("visuals-02-effects"));
        SelfTest.Add(6f, "visuals: reduced", Reduced);
        SelfTest.Add(6.5f, "visuals: baseline frames", () =>
        {
            Cleanup();
            FrameMeter.Start();
        });
        SelfTest.Add(9f, "visuals: many effects", () =>
        {
            _baseline = FrameMeter.Stop();
            SpawnMany(60);
        });
        // Measure from a second after the spawn: the frame that instantiates 60 prefabs is a one-off hitch.
        SelfTest.Add(10f, "visuals: measure many", FrameMeter.Start);
        SelfTest.Add(10.5f, "visuals: shot many", () => TestKit.Screenshot("visuals-03-many"));
        SelfTest.Add(12.5f, "visuals: frame cost", FrameCost);

        // Worn-steel weapons (COS-111) under the game's own light: held-size weapons and the drops, by day and at dusk.
        SelfTest.Add(13f, "visuals: weapons by day", ShowWeapons);
        SelfTest.Add(14.5f, "visuals: shot weapons by day", () => TestKit.Screenshot("visuals-04-weapons-day"));
        SelfTest.Add(15f, "visuals: dusk", () => SetHour(17, 35));
        SelfTest.Add(16f, "visuals: weapons at dusk", ShowWeapons);
        SelfTest.Add(18f, "visuals: shot weapons at dusk", () => TestKit.Screenshot("visuals-05-weapons-dusk"));
        SelfTest.Add(18.5f, "visuals: drops at dusk", () => ShowDrops(light: false));
        SelfTest.Add(20f, "visuals: shot drops at dusk", () => TestKit.Screenshot("visuals-06-drops-dusk"));
        SelfTest.Add(20.5f, "visuals: restore time", () =>
        {
            Cleanup();
            if (_savedHour >= 0) SetHour(_savedHour, _savedMinute);
        });

        // Close-ups, four effects a shot (COS-211): the grid above is too far away to judge one effect's look.
        int pages = (FxKit.All.Length + 3) / 4;
        SelfTest.Add(20.8f, "visuals: open ground for close-ups", MoveToOpenGround);
        for (int p = 0; p < pages; p++)
        {
            int page = p;
            SelfTest.Add(22f + p * 1.8f, "visuals: close-up " + (p + 1), () => CloseUp(page, false));
            // One-shot effects are over within half a second: spawn them again just before the shot.
            SelfTest.Add(23.05f + p * 1.8f, "visuals: close-up bursts " + (p + 1), () => CloseUp(page, true));
            SelfTest.Add(23.3f + p * 1.8f, "visuals: shot close-up " + (p + 1), () => TestKit.Screenshot("visuals-1" + page + "-closeup"));
        }
        // The two effects the mod animates itself (COS-211): Ball Lightning's bolts and Black Flame's field of flames.
        float live = 22f + pages * 1.8f;
        FlameRing? field = null;
        SelfTest.Add(live, "visuals: ball lightning", () =>
        {
            Cleanup();
            if (ViewSpot(0.5f, 0.7f) is { } at) SpellFx.Sphere(at + Vector3.up * 2f, 0.8f, SpellFx.Lightning, 2.2f, look: SpellFx.Look.BallLightning);
        });
        SelfTest.Add(live + 1.2f, "visuals: shot ball lightning", () => TestKit.Screenshot("visuals-20-ball-lightning"));
        SelfTest.Add(live + 2.6f, "visuals: flame field", () =>
        {
            if (ViewSpot(0.5f, 0.7f) is not { } at) return;
            field = new FlameRing();
            field.Update(at, (float)Core.Rules.SpellTuning.BlackFlameRadius);
        });
        SelfTest.Add(live + 3.6f, "visuals: shot flame field", () => TestKit.Screenshot("visuals-21-flame-field"));
        SelfTest.Add(live + 3.8f, "visuals: close-ups done", () =>
        {
            field?.Destroy();
            Cleanup();
        });
        // COS-213: a portal pair stands on the ground, blue then orange.
        var gates = new List<GameObject>();
        SelfTest.Add(live + 4f, "visuals: portal pair", () =>
        {
            if (ViewSpot(0.35f, 0.6f) is { } a) gates.Add(SpellFx.Portal(a, (Cam?.position ?? a) - a, Spells.SpellPortals.FirstGate, 3f));
            if (ViewSpot(0.65f, 0.6f) is { } b) gates.Add(SpellFx.Portal(b, (Cam?.position ?? b) - b, Spells.SpellPortals.SecondGate, 3f));
        });
        SelfTest.Add(live + 5.1f, "visuals: portal parts", () =>
        {
            foreach (var g in gates.Where(g => g != null))
                foreach (var ps in g.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    var m = r != null ? r.sharedMaterial : null;
                    SelfTest.Log($"visuals: portal part {ps.name}: {ps.particleCount} particles, renderer {(r != null && r.enabled)}, material {(m != null ? m.name : "none")}"
                        + $" tint {(m != null && m.HasProperty("_TintColor") ? m.GetColor("_TintColor").ToString() : "-")}, start colour {ps.main.startColor.color}");
                }
        });
        SelfTest.Add(live + 5.2f, "visuals: shot portals", () => TestKit.Screenshot("visuals-22-portals"));
        SelfTest.Add(live + 5.4f, "visuals: portals done", () =>
        {
            foreach (var g in gates) SpellFx.Remove(g);
            Cleanup();
        });

        // COS-213: a heal holds in live play (the game pulled health back to its own target health).
        float healed = 0;
        SelfTest.Add(live + 5.6f, "visuals: heal", () =>
        {
            var h = TheForest.Utils.LocalPlayer.Vitals?.Health;
            if (h == null) return;
            h.SetCurrentValue(h.GetMax() * 0.3f);
            TheForest.Utils.LocalPlayer.Vitals!.SetTargetHealth(h.GetMax() * 0.3f);
            Bridge.PlayerEffects.Heal(h.GetMax() * 0.2f);
            healed = h._currentValue;
        });
        SelfTest.Add(live + 7.6f, "visuals: heal holds", () =>
        {
            var h = TheForest.Utils.LocalPlayer.Vitals?.Health;
            if (h == null) return;
            // Undone = back near the 30% it started from; other phases' enemies may still nick a little off meanwhile.
            SelfTest.Check("visuals: a heal is not undone by the game (COS-213)", h._currentValue >= healed - 0.05f * h.GetMax(), $"{healed:0.#} -> {h._currentValue:0.#} of {h.GetMax():0}");
            h.SetCurrentValue(h.GetMax());
            TheForest.Utils.LocalPlayer.Vitals!.SetTargetHealth(h.GetMax());
        });
    }

    /// <summary>Where a ray through a point of the screen (0..1) meets the world, at most 7 m away; null without a camera.</summary>
    private static Vector3? ViewSpot(float x, float y)
    {
        var cam = Cam;
        if (cam == null || cam.GetComponent<Camera>() is not { } lens) return null;
        var ray = lens.ViewportPointToRay(new Vector3(x, y, 0));
        foreach (var hit in Physics.RaycastAll(ray, 40f).OrderBy(h => h.distance))
        {
            if (hit.transform == null || hit.collider.isTrigger || (LocalPlayer.Transform != null && hit.transform.IsChildOf(LocalPlayer.Transform))) continue;
            return hit.distance <= 7f ? hit.point : FlameRing.Ground(ray.GetPoint(7f));
        }
        return FlameRing.Ground(ray.GetPoint(7f));
    }

    private static readonly (float Forward, float Side)[] CloseUpSpots = { (6, -3.9f), (6, -1.3f), (6, 1.3f), (6, 3.9f) };

    /// <summary>
    /// Moves the player to a nearby place where the close-up spots are level and in plain view along the camera's own
    /// heading (the camera does not follow a turned player, so the place is searched for, not the heading).
    /// </summary>
    private void MoveToOpenGround()
    {
        var cam = Cam;
        var t = LocalPlayer.Transform;
        if (cam == null || t == null) return;
        var fwd = Flat(cam.forward);
        var right = Vector3.Cross(Vector3.up, fwd);
        foreach (float radius in new[] { 0f, 12f, 20f, 30f, 45f, 60f })
            for (int a = 0; a < (radius == 0 ? 1 : 12); a++)
            {
                if (TestKit.GroundAt(t.position + Quaternion.Euler(0, a * 30, 0) * Vector3.forward * radius) is not { } g) continue;
                var eye = g + Vector3.up * 1.7f;
                bool ok = true;
                foreach (var spot in CloseUpSpots)
                {
                    var at = TestKit.GroundAt(g + fwd * spot.Forward + right * spot.Side);
                    if (at == null || Mathf.Abs(at.Value.y - g.y) > 1.2f) { ok = false; break; }
                    foreach (float h in new[] { 0.1f, 2.5f })
                    {
                        var to = at.Value + Vector3.up * h;
                        foreach (var hit in Physics.RaycastAll(eye, (to - eye).normalized, (to - eye).magnitude - 0.2f))
                            if (hit.transform != null && !hit.collider.isTrigger && !hit.transform.IsChildOf(t)) ok = false;
                    }
                    if (!ok) break;
                }
                if (!ok) continue;
                if (radius > 0) SpellCaster.TeleportPlayer(g + Vector3.up * 0.05f);
                Physics.SyncTransforms();
                SelfTest.Log($"visuals: close-ups at {g} ({radius} m away)");
                return;
            }
        SelfTest.Log("visuals: no open ground found for the close-ups");
    }

    /// <summary>Four effects in a row a few metres ahead, standing on the ground under each spot.</summary>
    private void CloseUp(int page, bool oneShots)
    {
        if (!oneShots) Cleanup();
        var cam = Cam;
        if (!CotsBundle.Loaded || cam == null) return;
        var body = LocalPlayer.Transform != null ? LocalPlayer.Transform : cam;
        var fwd = Flat(cam.forward);
        var right = Vector3.Cross(Vector3.up, fwd);
        Color[] tints = { SpellFx.Holy, SpellFx.Fire, SpellFx.Arcane, SpellFx.Ice, SpellFx.Heal, SpellFx.Lightning, SpellFx.Blood };
        for (int i = page * 4; i < Math.Min(FxKit.All.Length, page * 4 + 4); i++)
        {
            string name = FxKit.All[i];
            if (oneShots != (name is FxKit.Burst or FxKit.HitFire or FxKit.HitIce or FxKit.HitArcane or FxKit.HitStone)) continue;
            var spot = CloseUpSpots[i - page * 4];
            var pos = body.position + fwd * spot.Forward + right * spot.Side;
            pos.y = TestKit.GroundAt(pos)?.y ?? body.position.y;
            // The camera's pitch is whatever the save left it at: put each effect where a ray through its place on
            // screen meets the world, so all four are in frame whatever the camera looks at.
            if (cam.GetComponent<Camera>() is { } lens)
            {
                var ray = lens.ViewportPointToRay(new Vector3(0.14f + 0.24f * (i - page * 4), 0.66f, 0));
                foreach (var hit in Physics.RaycastAll(ray, 40f).OrderBy(h => h.distance))
                {
                    if (hit.transform == null || hit.collider.isTrigger || hit.transform.IsChildOf(body)) continue;
                    pos = hit.distance <= 7f ? hit.point : ray.GetPoint(7f);
                    break;
                }
            }
            bool ground = name is FxKit.Ring or FxKit.Dome or FxKit.Column or FxKit.Snow or FxKit.Glyph or FxKit.Gas or FxKit.Portal or FxKit.AuraBuff;
            pos.y += ground ? 0.3f : 1.1f;   // clear of the grass
            var scale = name is FxKit.Beam or FxKit.Chain ? new Vector3(0.25f, 3, 0.25f) : name is FxKit.Column or FxKit.Snow ? new Vector3(1, 3, 1)
                : name is FxKit.Orb or FxKit.BallLightning ? Vector3.one * 0.5f : Vector3.one;
            var go = FxKit.Spawn(name, pos, scale, tints[i % tints.Length], essential: true);
            if (go != null) _shown.Add(go);
        }
    }

    private static readonly string[] WeaponModels =
        { "Weapon_Sword", "Weapon_GreatSword", "Weapon_Axe", "Weapon_Hammer", "Weapon_Bow", "Weapon_Polearm" };

    /// <summary>The held-weapon models at real size, standing in a row in front of the camera, turned three-quarters.</summary>
    private void ShowWeapons()
    {
        Cleanup();
        var cam = Cam;
        if (!CotsBundle.Loaded || cam == null) return;
        var fwd = Flat(cam.forward);
        var right = Vector3.Cross(Vector3.up, fwd);
        int missing = 0;
        for (int i = 0; i < WeaponModels.Length; i++)
        {
            var holder = new GameObject("cots-visuals-test");
            holder.transform.position = cam.position + fwd * 2.6f + right * ((i - 4) * 0.42f) + Vector3.down * 1.0f;
            holder.transform.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(-10, -35, 0);
            if (CotsBundle.Spawn(WeaponModels[i], holder.transform) == null) missing++;
            _shown.Add(holder);
        }
        SelfTest.Check("visuals: every held-weapon model loads", missing == 0, $"{missing} of {WeaponModels.Length} missing");
    }

    private int _savedHour = -1, _savedMinute;

    /// <summary>Sets the time of day (TimeOfDayHolder.SetDirectTime); remembers the first time it replaced.</summary>
    private void SetHour(int hour, int minute)
    {
        try
        {
            var now = Sons.Environment.TimeOfDayHolder.GetTimeOfDay();
            if (_savedHour < 0)
            {
                _savedHour = now.Hours;
                _savedMinute = now.Minutes;
            }
            Sons.Environment.TimeOfDayHolder.SetDirectTime(new Sons.Environment.TimeOfDay(now.Days, hour, minute, 0, 0), true, 0f, null, true);
            var after = Sons.Environment.TimeOfDayHolder.GetTimeOfDay();
            SelfTest.Log($"visuals: time {now.Hours:D2}:{now.Minutes:D2} -> {after.Hours:D2}:{after.Minutes:D2}");
        }
        catch (Exception e)
        {
            SelfTest.Log("visuals: could not set the time: " + e.Message);
        }
    }

    private (float ms, int frames) _baseline;

    private static Transform? Cam => LocalPlayer.MainCam != null ? LocalPlayer.MainCam.transform : Camera.main?.transform;

    private void ShowDrops() => ShowDrops(light: true);

    private void ShowDrops(bool light)
    {
        Cleanup();
        var cam = Cam;
        if (!CotsBundle.Loaded || cam == null)
        {
            SelfTest.Log("visuals: skipped, " + (cam == null ? "no camera" : CotsBundle.Problem));
            return;
        }
        var fwd = Flat(cam.forward);
        var right = Vector3.Cross(Vector3.up, fwd);
        int n = Loot.LootVisuals.DropPrefabs.Length, missing = 0, pink = 0;
        for (int i = 0; i < n; i++)
        {
            string name = Loot.LootVisuals.DropPrefabs[i];
            int row = i / 7, col = i % 7;
            var pos = cam.position + fwd * (3.2f + row * 1.4f) + right * ((col - 3) * 0.75f) + Vector3.down * (1.1f - row * 0.2f);
            var holder = new GameObject("cots-visuals-test");
            holder.transform.position = pos;
            var go = CotsBundle.Spawn(name, holder.transform);
            if (go == null) missing++;
            else
            {
                go.transform.localScale = Vector3.one * 0.55f;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    if (r.sharedMaterial == null || r.sharedMaterial.shader == null || !r.sharedMaterial.shader.isSupported || r.sharedMaterial.shader.name != "HDRP/Lit") pink++;
            }
            _shown.Add(holder);
        }
        if (light)
        {
            var l = new GameObject("cots-visuals-light");
            l.transform.position = cam.position + fwd * 2f + Vector3.up;
            var pl = l.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.range = 8;
            pl.intensity = 60;
            _shown.Add(l);
        }
        SelfTest.Check("visuals: every drop model loads", missing == 0, $"{missing} of {n} missing");
        SelfTest.Check("visuals: drop materials are HDRP/Lit and supported", pink == 0, $"{pink} renderers wrong");
    }

    private void ShowEffects()
    {
        Cleanup();
        var cam = Cam;
        if (!CotsBundle.Loaded || cam == null) return;
        var fwd = Flat(cam.forward);
        var right = Vector3.Cross(Vector3.up, fwd);
        var ground = cam.position + Vector3.down * 1.6f;
        Color[] tints = { SpellFx.Holy, SpellFx.Fire, SpellFx.Arcane, SpellFx.Ice, SpellFx.Heal, SpellFx.Lightning, SpellFx.Blood };
        int missing = 0, broken = 0;
        for (int i = 0; i < FxKit.All.Length; i++)
        {
            string name = FxKit.All[i];
            int row = i / 5, col = i % 5;
            var pos = ground + fwd * (6f + row * 4.5f) + right * ((col - 2) * 3f);
            var scale = name is FxKit.Beam or FxKit.Chain ? new Vector3(0.25f, 3, 0.25f) : name is FxKit.Column or FxKit.Snow ? new Vector3(1, 3, 1) : Vector3.one;
            var go = FxKit.Spawn(name, pos, scale, tints[i % tints.Length]);
            if (go == null)
            {
                missing++;
                continue;
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial == null || !r.sharedMaterial.shader.isSupported) broken++;
            _shown.Add(go);
        }
        SelfTest.Check("visuals: every effect loads", missing == 0, $"{missing} of {FxKit.All.Length} missing");
        SelfTest.Check("visuals: effect shaders supported", broken == 0, $"{broken} renderers with unsupported shaders");
    }

    private void Reduced()
    {
        if (!CotsBundle.Loaded) return;
        bool was = ReduceHeavyEffects;
        SetReduceHeavyEffects(true);
        var flame = FxKit.Spawn(FxKit.Flame, Vector3.zero, 1, Color.white);
        var ring = FxKit.Spawn(FxKit.Ring, Vector3.zero, 1, Color.white);
        SetReduceHeavyEffects(was);
        SelfTest.Check("visuals: reduced hides decorative effects", flame == null && ring != null, $"flame {(flame == null ? "hidden" : "shown")}, ring {(ring != null ? "shown" : "missing")}");
        if (flame != null) UnityEngine.Object.Destroy(flame);
        if (ring != null) UnityEngine.Object.Destroy(ring);
    }

    private void SpawnMany(int count)
    {
        var cam = Cam;
        if (!CotsBundle.Loaded || cam == null) return;
        var fwd = Flat(cam.forward);
        var right = Vector3.Cross(Vector3.up, fwd);
        var ground = cam.position + Vector3.down * 1.6f;
        for (int i = 0; i < count; i++)
        {
            string name = FxKit.All[i % FxKit.All.Length];
            var pos = ground + fwd * (5f + (i / 10) * 3f) + right * ((i % 10 - 4.5f) * 2.2f);
            var scale = name is FxKit.Beam or FxKit.Chain ? new Vector3(0.25f, 3, 0.25f) : name is FxKit.Column or FxKit.Snow ? new Vector3(1, 3, 1) : Vector3.one;
            var go = FxKit.Spawn(name, pos, scale, SpellFx.Fire);
            if (go == null) continue;
            if (i % 5 == 0) FxKit.AddLight(go, SpellFx.Fire, 6, 40);
            _shown.Add(go);
        }
    }

    private void FrameCost()
    {
        var many = FrameMeter.Stop();
        int particles = 0, lights = 0;
        foreach (var go in _shown)
            if (go != null)
            {
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) particles += ps.particleCount;
                lights += go.GetComponentsInChildren<Light>().Length;
            }
        // Frame time is logged only: other features running in the same pass and the shared machine make it noisy
        // (COS-113, COS-108 saw +-20%). The budget is checked on counts, which catch a runaway effect deterministically.
        SelfTest.Log($"visuals: frame cost: baseline {_baseline.ms:F2} ms over {_baseline.frames} frames, with {_shown.Count} effects ({particles} particles, {lights} lights) {many.ms:F2} ms over {many.frames} frames");
        SelfTest.Check("visuals: 60 effects stay under 15000 particles", particles < 15000, particles.ToString());
        SelfTest.Check("visuals: 60 effects carry at most one light per five effects", lights <= _shown.Count / 5 + 1, lights.ToString());
        Cleanup();
    }

    private void Cleanup()
    {
        foreach (var go in _shown)
            if (go != null) UnityEngine.Object.Destroy(go);
        _shown.Clear();
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0;
        return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
    }

    /// <summary>Average frame time between <see cref="Start"/> and <see cref="Stop"/> (the self-test's frame-cost check).</summary>
    private static class FrameMeter
    {
        private static bool _on;
        private static float _sum;
        private static int _frames;

        public static void Start()
        {
            _on = true;
            _sum = 0;
            _frames = 0;
        }

        public static void Sample(float dt)
        {
            if (!_on) return;
            _sum += dt;
            _frames++;
        }

        public static (float ms, int frames) Stop()
        {
            _on = false;
            return (_frames == 0 ? 0 : _sum / _frames * 1000f, _frames);
        }
    }
}
