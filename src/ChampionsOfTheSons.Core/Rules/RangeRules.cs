namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Every range the player's actions use (pickup radius, spell radius and range, aura radius, area damage) is scaled by one stat,
/// <see cref="RangeMult"/>. Wide Reach raises it (COS-85, Max 2026-11-20: Wide Reach affects everything range-related).
/// </summary>
public static class RangeRules
{
    public const string RangeMult = "range_mult";

    /// <summary>The player's range factor (never below 0.1).</summary>
    public static double Factor(StatSheet s) => Math.Max(0.1, s.Get(RangeMult));

    /// <summary>A base range or radius in metres after the player's range factor.</summary>
    public static double Scaled(StatSheet s, double metres) => Math.Max(0, metres) * Factor(s);
}
