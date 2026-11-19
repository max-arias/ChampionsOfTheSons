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
    public EnemyRuntime(EnemyKind kind, EnemyProfile profile, double vanillaMaxHealth)
    {
        Kind = kind;
        Profile = profile;
        VanillaMaxHealth = vanillaMaxHealth;
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
        ModContent.Db.Enemies.TryGetValue(type.ToString(), out var k) ? k : null;

    public static EnemyRuntime? Get(StatsManager? stats) =>
        stats != null && ByStats.TryGetValue(stats.Pointer, out var r) ? r : null;

    public static EnemyRuntime? Get(VailActor? actor)
    {
        if (actor == null) return null;
        var vsm = actor.GetStatsManager();
        return vsm == null ? null : Get(vsm.GetStatsManager());
    }

    public static EnemyRuntime GetOrRoll(StatsManager stats, EnemyKind kind, double vanillaMaxHealth)
    {
        if (ByStats.TryGetValue(stats.Pointer, out var existing)) return existing;
        var profile = EnemyScaler.Roll(kind, vanillaMaxHealth, WorldState.Rules, Rng);
        var runtime = new EnemyRuntime(kind, profile, vanillaMaxHealth);
        ByStats[stats.Pointer] = runtime;
        return runtime;
    }

    public static void Remove(StatsManager? stats)
    {
        if (stats != null) ByStats.Remove(stats.Pointer);
    }

    public static void Clear() => ByStats.Clear();

    /// <summary>Drop actors the world simulation removed (despawned without dying).</summary>
    public static void Init() => SonsSdk.SdkEvents.OnWorldSimActorRemoved.Subscribe(actor => Remove(actor?.StatManager));

    public static HealthStat? Health(StatsManager stats) =>
        stats.GetStat(Il2CppType.Of<HealthStat>())?.TryCast<HealthStat>();
}
