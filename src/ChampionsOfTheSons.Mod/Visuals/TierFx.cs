using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// COS-110. Since Max's 2026-11-21 review a miniboss's or boss's power-up aura takes its element's colour and grows with its tier and
/// abilities (<see cref="EnemyAura"/>); one without an element gets only the ring at its feet and a soft glow. The original proposal (2026-11-20): elites, minibosses and bosses carry effects in the rarity colour language.
/// Elite: a faint blue aura at the feet with a few drifting motes. Miniboss: a yellow aura, a soft light and embers off the shoulders.
/// Boss: a red-orange aura and ground ring, a strong pulsing light, dark embers over the whole frame and a heat shimmer, a burst when
/// it engages the player (with the boss bar) and a large burst when it dies. Built from the tier every client already has (enemy sync),
/// so nothing new goes over the network. Effects attach within <see cref="Range"/> of the local player, at most <see cref="MaxAttached"/>;
/// "reduce heavy effects" keeps only the auras and the boss light.
/// </summary>
public sealed class TierFxFeature : IModFeature
{
    public const string Aura = "Fx_TierAura", Embers = "Fx_TierEmbers", Shimmer = "Fx_TierShimmer", Power = "Fx_TierPower";
    public const float Range = 60f, ScanInterval = 0.5f;
    public const int MaxAttached = 16;

    internal static readonly Color EliteColor = new(0.35f, 0.62f, 1f), MinibossColor = new(1f, 0.82f, 0.3f), BossColor = new(1f, 0.36f, 0.12f);

    internal sealed class Attached
    {
        public EnemyRuntime Runtime = null!;
        public EnemyTier Tier;
        public EnemyElement Element;
        public float LightBase;
        public readonly List<GameObject> Parts = new();
        public Light? Light;
        public Vector3 LastPos;
    }

    private static readonly Dictionary<EnemyRuntime, Attached> ByEnemy = new();
    private static float _time, _nextScan;

    internal static IReadOnlyDictionary<EnemyRuntime, Attached> All => ByEnemy;
    internal static int Bursts { get; private set; }

    public void Init()
    {
        EnemyHud.EnemyHudFeature.BossEngaged += r => Burst(r, notice: true);
        SelfTest.PhaseReset += Clear;
    }

    public void OnGameStart() => Clear();

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld || !FxKit.Available) return;
        try
        {
            _time += dt;
            if (_time >= _nextScan)
            {
                _nextScan = _time + ScanInterval;
                Scan();
            }
            Pulse();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: tier effects failed: {e}");
            _nextScan = _time + 5;
        }
    }

    public void RegisterSelfTests() => TierFxSelfTest.Register();

    internal static Color ColorOf(EnemyTier tier) => tier switch
    {
        EnemyTier.Boss => BossColor,
        EnemyTier.Miniboss => MinibossColor,
        _ => EliteColor,
    };

    private static void Scan()
    {
        Vector3 player = LocalPlayer.Transform != null ? LocalPlayer.Transform.position : Vector3.zero;
        var seen = new HashSet<EnemyRuntime>();
        foreach (var r in EnemyRegistry.All)
        {
            if (r.Profile.Tier == EnemyTier.Normal || !Alive(r)) continue;
            var pos = r.Actor!.transform.position;
            if (Vector3.Distance(pos, player) > Range) continue;
            seen.Add(r);
            if (ByEnemy.TryGetValue(r, out var a)) { a.LastPos = pos; continue; }
            if (ByEnemy.Count < MaxAttached) Attach(r, pos);
        }
        foreach (var (r, a) in ByEnemy.ToList())
        {
            if (seen.Contains(r)) continue;
            bool died = !Alive(r) && a.Tier >= EnemyTier.Miniboss && Vector3.Distance(a.LastPos, player) <= Range;
            if (died) Burst(r, notice: false, at: a.LastPos);
            Detach(r);
        }
    }

    internal static Color ColorOf(EnemyElement element) => element switch
    {
        EnemyElement.Fire => BossColor,
        EnemyElement.Cold => new Color(0.6f, 0.85f, 1f),
        EnemyElement.Arcane => new Color(0.68f, 0.38f, 1f),
        EnemyElement.Poison => new Color(0.38f, 0.92f, 0.3f),
        EnemyElement.Holy => new Color(1f, 0.86f, 0.45f),
        _ => Color.white,
    };

    /// <summary>The colour an enemy's effects use: its element's, else its tier's.</summary>
    internal static Color ColorOf(EnemyRuntime r)
    {
        var element = r.Profile.Tier >= EnemyTier.Miniboss ? EnemyAura.ElementOf(r.Profile.Abilities) : EnemyElement.None;
        return element == EnemyElement.None ? ColorOf(r.Profile.Tier) : ColorOf(element);
    }

    private static void Attach(EnemyRuntime r, Vector3 pos)
    {
        var tier = r.Profile.Tier;
        // Max (2026-11-21): a boss's power-up aura shows its element (fire, cold, arcane, poison, holy) and grows with the tier and its
        // abilities; a miniboss or boss without an element gets only the ring at its feet and a soft glow.
        var element = tier >= EnemyTier.Miniboss ? EnemyAura.ElementOf(r.Profile.Abilities) : EnemyElement.None;
        float k = (float)EnemyAura.Intensity(tier, r.Profile.Abilities.Count);
        bool power = element != EnemyElement.None;
        var color = power ? ColorOf(element) : ColorOf(tier);
        var t = r.Actor!.transform;
        // The actor's scale (Gargantuan, boss size) already scales its transform: sizes here are for a normal-sized body.
        var a = new Attached { Runtime = r, Tier = tier, Element = element, LastPos = pos };
        float auraRadius = tier switch { EnemyTier.Boss => 1.6f, EnemyTier.Miniboss => 1.2f, _ => 0.9f };
        var aura = FxKit.Spawn(Aura, pos, auraRadius, color, t);
        if (aura != null)
        {
            SetRate(aura, "motes", power ? 26 * k : FxKit.Reduced ? 0 : 6);
            a.Parts.Add(aura);
        }
        if (power && tier == EnemyTier.Boss && aura != null)
        {
            // A second, wider ring on the ground for the boss.
            var ring = FxKit.Spawn(FxKit.Ring, pos, 2.6f, color * 0.8f, t);
            if (ring != null) { SetRate(ring, "motes", 0); a.Parts.Add(ring); }
        }
        if (power && !FxKit.Reduced)
        {
            var emberColor = element == EnemyElement.Fire && tier == EnemyTier.Boss ? new Color(0.55f, 0.16f, 0.06f) : color;
            var embers = tier == EnemyTier.Boss
                ? FxKit.Spawn(Embers, pos + Vector3.up * 1.1f, new Vector3(0.7f, 1.0f, 0.7f), emberColor, t)
                : FxKit.Spawn(Embers, pos + Vector3.up * 1.5f, new Vector3(0.45f, 0.2f, 0.45f), emberColor, t);
            if (embers != null)
            {
                SetRate(embers, "embers", 30 * k);
                a.Parts.Add(embers);
            }
        }
        // The power-up aura (Max, COS-110): flames swirling up the body, sparks circling the waist, motes shooting up.
        if (power)
        {
            float body = Mathf.Max(0.8f, (float)r.Profile.Scale);
            var fx = FxKit.Spawn(Power, pos, Vector3.one * body, color, t);
            if (fx != null)
            {
                float cut = FxKit.Reduced ? 0.35f : 1f;
                SetRate(fx, "flames", 70 * k * cut);
                SetRate(fx, "orbit", 24 * k * cut);
                SetRate(fx, "rise", tier == EnemyTier.Boss && !FxKit.Reduced ? 18 * k : 0);
                a.Parts.Add(fx);
            }
        }
        if (element == EnemyElement.Fire && tier == EnemyTier.Boss && !FxKit.Reduced)
        {
            var shimmer = FxKit.Spawn(Shimmer, pos, new Vector3(0.9f, 2.4f, 0.9f), new Color(1f, 0.55f, 0.3f), t);
            if (shimmer != null) a.Parts.Add(shimmer);
        }
        if (tier >= EnemyTier.Miniboss && a.Parts.Count > 0)
        {
            a.LightBase = power ? 30 * k : tier == EnemyTier.Boss ? 10 : 6; // without an element: a soft body glow
            a.Light = FxKit.AddLight(a.Parts[0], color, tier == EnemyTier.Boss ? 7 : 4, a.LightBase, essential: tier == EnemyTier.Boss, offset: Vector3.up * 1.2f / auraRadius);
        }
        ByEnemy[r] = a;
    }

    private static void Detach(EnemyRuntime r)
    {
        if (!ByEnemy.Remove(r, out var a)) return;
        foreach (var p in a.Parts)
            if (p != null) UnityEngine.Object.Destroy(p);
    }

    /// <summary>The boss light breathes; a miniboss's stays steady.</summary>
    private static void Pulse()
    {
        foreach (var a in ByEnemy.Values)
            if (a.Tier == EnemyTier.Boss && a.Light != null)
                a.Light.intensity = a.LightBase * (0.7f + 0.3f * Mathf.Sin(Time.time * 2.4f));
    }

    /// <summary>A burst in the tier colour: on engage (boss) or at death (miniboss and boss, the boss's larger).</summary>
    internal static void Burst(EnemyRuntime r, bool notice, Vector3? at = null)
    {
        if (!FxKit.Available || (notice && r.Profile.Tier != EnemyTier.Boss)) return;
        Vector3 pos;
        try { pos = at ?? r.Actor!.transform.position; } catch { return; }
        float size = r.Profile.Tier == EnemyTier.Boss ? (notice ? 2.2f : 3.5f) : 1.8f;
        var go = FxKit.Spawn(FxKit.Burst, pos + Vector3.up * 0.2f, size, ColorOf(r));
        if (go == null) return;
        Bursts++;
        UnityEngine.Object.Destroy(go, 2.5f);
    }

    private static void SetRate(GameObject root, string child, float rate)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps.gameObject.name != child) continue;
            var em = ps.emission;
            em.rateOverTime = rate;
            if (rate <= 0) ps.Clear();
        }
    }

    private static bool Alive(EnemyRuntime r)
    {
        try
        {
            return r.Actor != null && r.IsActive;
        }
        catch
        {
            return false;
        }
    }

    internal static void Clear()
    {
        foreach (var r in ByEnemy.Keys.ToList()) Detach(r);
        _nextScan = 0;
    }

    /// <summary>Scan now (self-test).</summary>
    internal static void ScanNow() => Scan();
}
