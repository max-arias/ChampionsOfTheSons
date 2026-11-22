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

    /// <summary>A burn of <paramref name="perSecond"/> for <paramref name="seconds"/> after the player's fire stats.</summary>
    public static (double PerSecond, double Seconds) Burn(StatSheet stats, double perSecond, double seconds) =>
        (perSecond * (1 + Math.Max(0, stats.Get(TickRate))), seconds * (1 + Math.Max(0, stats.Get(Duration))));

    /// <summary>The greatbow's burn from an arrow hit: Firebolt's share of the hit per second, for Firebolt's time.</summary>
    public static (double PerSecond, double Seconds) GreatBowBurn(StatSheet stats, double hitDamage) =>
        Burn(stats, hitDamage * SpellTuning.FireboltBurnFraction, SpellTuning.FireboltBurnSeconds);
}
