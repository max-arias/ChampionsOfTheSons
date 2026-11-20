using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using Bolt;
using Il2CppInterop.Runtime;
using Sons.Ai.Vail;
using Sons.StatSystem;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>Our state for one live enemy (host side).</summary>
public sealed class EnemyRuntime
{
    public EnemyRuntime(EnemyKind kind, EnemyProfile profile, double vanillaMaxHealth, VailActor? actor, EnemyCombatState combat)
    {
        Kind = kind;
        Profile = profile;
        VanillaMaxHealth = vanillaMaxHealth;
        Actor = actor;
        Combat = combat;
    }

    /// <summary>The in-world actor. The one SetMaxHealthStat sees is not always the live one, so it is re-resolved by stats (see <see cref="EnemyRegistry.ResolveActors"/>) and on every hit.</summary>
    public VailActor? Actor { get; set; }
    public bool ActorLive { get; set; }
    /// <summary>The stats object shared by every actor of this enemy (health lives here).</summary>
    public StatsManager? Stats { get; set; }
    /// <summary>Abilities, debuffs and DoTs in motion (see <see cref="EnemyCombatState"/>).</summary>
    public EnemyCombatState Combat { get; }
    public bool ScaleApplied { get; set; }
    /// <summary>Undead used its revive.</summary>
    public bool Revived { get; set; }
    public float AppliedSpeed { get; set; } = -1;
    public float WantedSpeed { get; set; } = -1;
    /// <summary>Where it was when last seen alive (a dead actor's position is not reliable).</summary>
    public UnityEngine.Vector3? LastPosition { get; set; }

    /// <summary>Self-tests only: the headless game keeps spawned enemies dormant, so a test can declare one active (true) or inactive (false). Null = ask the game.</summary>
    public bool? TestActive
    {
        get => _testActive;
        set
        {
            _testActive = value;
            EnemyRegistry.SetTestForced(this, value == true);
        }
    }
    private bool? _testActive;

    /// <summary>Self-tests only: overrides whether the boss bar sees this enemy as aware of the player (COS-67). Null = ask the game.</summary>
    public bool? TestAware { get; set; }

    /// <summary>Alive, enabled in the scene and not despawned: the only state in which abilities may act (the original's bug #43).</summary>
    public bool IsActive
    {
        get
        {
            try
            {
                if (Actor == null || Actor.IsDead() || Actor.GetHealth() <= 0) return false;
                return TestActive ?? Actor.gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }
    }

    public EnemyKind Kind { get; }
    public EnemyProfile Profile { get; }
    public double VanillaMaxHealth { get; }
    public double ArmorReduction { get; set; }
    public bool LastHitByLocalPlayer { get; set; }
    public bool LastHitWasCrit { get; set; }
}

/// <summary>
/// Live enemies by their StatsManager (the one object both VailActor and WorldSimActor hand out for the same enemy).
/// The host rolls profiles; clients will mirror them over the network (COS-12).
/// </summary>
public static class EnemyRegistry
{
    private static readonly Dictionary<IntPtr, EnemyRuntime> ByStats = new();
    private static readonly Rng Rng = Rng.FromTime();

    public static int Count => ByStats.Count;

    public static bool IsAuthority => !BoltNetwork.isRunning || BoltNetwork.isServer;

    public static EnemyKind? KindFor(VailActorTypeId type) =>
        ModContent.Db != null && ModContent.Db.Enemies.TryGetValue(type.ToString(), out var k) ? k : null;

    public static EnemyRuntime? Get(StatsManager? stats) =>
        stats != null && ByStats.TryGetValue(stats.Pointer, out var r) ? r : null;

    public static EnemyRuntime? Get(VailActor? actor)
    {
        if (actor == null) return null;
        var vsm = actor.GetStatsManager();
        return vsm == null ? null : Get(vsm.GetStatsManager());
    }

    /// <summary>Every tracked enemy, without a copy (do not change the registry while looping).</summary>
    public static Dictionary<IntPtr, EnemyRuntime>.ValueCollection All => ByStats.Values;

    /// <summary>Self-tests only: rewrites a freshly rolled profile (e.g. to force a Boss), applied before anything sees it.</summary>
    public static Func<EnemyProfile, EnemyProfile>? TestProfileOverride { get; set; }

    /// <summary>A snapshot of every tracked enemy (safe to change the registry while looping).</summary>
    public static List<EnemyRuntime> Snapshot() => ByStats.Values.ToList();

    public static EnemyRuntime GetOrRoll(StatsManager stats, EnemyKind kind, double vanillaMaxHealth, VailActor? actor = null)
    {
        if (ByStats.TryGetValue(stats.Pointer, out var existing)) return existing;
        UnityEngine.Vector3? at = null;
        try { if (actor != null) at = actor.transform.position; }
        catch { /* a dying actor: roll against the whole party */ }
        var profile = EnemyScaler.Roll(kind, vanillaMaxHealth, WorldState.Rules, Net.CoopSession.PartyLevelAt(at), Rng);
        if (TestProfileOverride != null) profile = TestProfileOverride(profile);
        if (profile.Name == null && ModContent.Db != null)
            profile = profile with { Name = EnemyInfo.PickName(profile.Tier, ModContent.Db.EliteGivenIds, ModContent.Db.EliteEpithetIds, Rng) };
        var runtime = new EnemyRuntime(kind, profile, vanillaMaxHealth, actor, new EnemyCombatState(profile));
        runtime.Stats = stats;
        ByStats[stats.Pointer] = runtime;
        Dirty = true;
        return runtime;
    }

    /// <summary>Client side of co-op: registers the host's profile for an enemy so nameplates and hit code see it like a local roll.</summary>
    public static EnemyRuntime Mirror(StatsManager stats, EnemyKind kind, EnemyProfile profile, VailActor? actor)
    {
        if (ByStats.TryGetValue(stats.Pointer, out var existing)) return existing;
        var runtime = new EnemyRuntime(kind, profile, profile.MaxHealth, actor, new EnemyCombatState(profile))
        {
            Stats = stats,
            ScaleApplied = true,
        };
        ByStats[stats.Pointer] = runtime;
        Dirty = true;
        return runtime;
    }

    public static void Remove(StatsManager? stats)
    {
        if (stats != null) ByStats.Remove(stats.Pointer);
    }

    public static void Clear()
    {
        ByStats.Clear();
        _live.Clear();
        TestForced.Clear();
    }

    private static readonly int PerfResolve = Perf.Perf.Register("  EnemyRegistry.ResolveActors");

    // Double-buffered so a loop over Live never sees the list change under it (a refresh fills the other buffer, then swaps).
    private static List<EnemyRuntime> _live = new(), _liveBack = new();
    private static readonly HashSet<EnemyRuntime> TestForced = new();

    /// <summary>
    /// Tracked enemies whose actor was active in the scene at the last <see cref="ResolveActors"/> (at most a fraction of a second
    /// ago): the few dozen near the player, not the thousands the world simulation tracks (COS-132). Callers still check
    /// <see cref="EnemyRuntime.IsActive"/>. Do not keep the list across frames.
    /// </summary>
    public static IReadOnlyList<EnemyRuntime> Live => _live;

    /// <summary>An enemy was added since the last <see cref="ResolveActors"/>: refresh <see cref="Live"/> now rather than at the next interval.</summary>
    public static bool Dirty { get; private set; }

    /// <summary>Self-tests: a runtime whose TestActive was set to true stays in <see cref="Live"/> (the headless game keeps it dormant).</summary>
    internal static void SetTestForced(EnemyRuntime r, bool forced)
    {
        // A test that switches an enemy on acts on it right away (casts, hits): put it in Live now, not at the next refresh.
        if (forced && TestForced.Add(r)) ResolveActors();
        else if (!forced) TestForced.Remove(r);
    }

    /// <summary>
    /// Points every enemy at its live actor and rebuilds <see cref="Live"/>. Walks only the game's active actors
    /// (VailActorManager.GetActiveActors), not every VailActor object in memory (COS-132: that scan was the biggest frame cost).
    /// </summary>
    public static void ResolveActors()
    {
        using var perf = Perf.Perf.Scope(PerfResolve);
        Dirty = false;
        var next = _liveBack;
        next.Clear();
        var active = VailActorManager.GetActiveActors();
        if (active != null)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var a = active[i];
                if (a == null) continue;
                var r = Get(a);
                if (r == null || !a.gameObject.activeInHierarchy) continue;
                r.Actor = a;
                r.ActorLive = true;
                next.Add(r);
            }
        }
        if (TestForced.Count > 0) ResolveForced(next);
        _liveBack = _live;
        _live = next;
    }

    /// <summary>Self-tests only: dormant test enemies are not in the game's active list, so find their actor the slow way.</summary>
    private static void ResolveForced(List<EnemyRuntime> next)
    {
        TestForced.RemoveWhere(r => r.Stats == null || !ByStats.ContainsKey(r.Stats.Pointer));
        if (TestForced.Any(r => r.Actor == null || !r.ActorLive))
        {
            foreach (var a in UnityEngine.Resources.FindObjectsOfTypeAll<VailActor>())
            {
                var r = Get(a);
                if (r == null || !TestForced.Contains(r)) continue;
                bool live = a.gameObject.activeInHierarchy;
                if (live || !r.ActorLive)
                {
                    r.Actor = a;
                    if (live) r.ActorLive = true;
                }
            }
        }
        foreach (var r in TestForced)
            if (!next.Contains(r)) next.Add(r);
    }

    /// <summary>Drop actors the world simulation removed (despawned without dying).</summary>
    public static void Init() => SonsSdk.SdkEvents.OnWorldSimActorRemoved.Subscribe(actor => Remove(actor?.StatManager));

    public static HealthStat? HealthOf(VailActor? actor)
    {
        var stats = actor?.GetStatsManager()?.GetStatsManager();
        return stats == null ? null : Health(stats);
    }

    public static HealthStat? Health(StatsManager stats) =>
        stats.GetStat(Il2CppType.Of<HealthStat>())?.TryCast<HealthStat>();
}
