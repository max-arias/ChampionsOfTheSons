using ChampionsOfTheSons.Mod.Bundles;
using ChampionsOfTheSons.Mod.Spells;
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
            return;
        }
        PlayerStateFx.Update();
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
    }

    private static readonly string[] WeaponModels =
        { "Weapon_Sword", "Weapon_GreatSword", "Weapon_Axe", "Weapon_Hammer", "Weapon_Spear", "Weapon_Bow", "Weapon_Polearm", "Weapon_GreatAxe", "Weapon_GreatHammer" };

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
        Color[] tints = { SpellFx.Holy, SpellFx.Fire, SpellFx.Arcane, SpellFx.Ice, SpellFx.Heal, SpellFx.Storm, SpellFx.Blood };
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
