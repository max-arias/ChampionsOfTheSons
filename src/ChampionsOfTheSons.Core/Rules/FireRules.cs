namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// The player's fire stats on every burn the player sets (COS-148): Black Flame, Parry ignite, Firebolt's burn and the
/// Ashglow Greatbow. The item and perk text is the spec: "burn N% longer" and "burn damage ticks N% faster" (the same
/// damage per tick, more ticks, so more damage per second).
/// </summary>
public static class FireRules
{
    public const string Duration = "fire_duration";
    public const string TickRate = "fire_tick_rate";
    public const string GreatBowIgnites = "i_great_bow_ignites";

    /// <summary>A burn of <paramref name="perSecond"/> for <paramref name="seconds"/> after the player's own fire stats.</summary>
    public static (double PerSecond, double Seconds) Burn(StatSheet stats, double perSecond, double seconds) =>
        Burn(stats.Get(TickRate), stats.Get(Duration), perSecond, seconds);

    /// <summary>A burn after given fire stats: in co-op every player's fire tick rate and duration add up, as in COTF ("stacks
    /// with other players", COS-216).</summary>
    public static (double PerSecond, double Seconds) Burn(double tickRate, double duration, double perSecond, double seconds) =>
        (perSecond * (1 + Math.Max(0, tickRate)), seconds * (1 + Math.Max(0, duration)));

    /// <summary>The greatbow's burn from an arrow hit: Firebolt's share of the hit per second, for Firebolt's time.</summary>
    public static (double PerSecond, double Seconds) GreatBowBurn(double tickRate, double duration, double hitDamage) =>
        Burn(tickRate, duration, hitDamage * SpellTuning.FireboltBurnFraction, SpellTuning.FireboltBurnSeconds);
}
