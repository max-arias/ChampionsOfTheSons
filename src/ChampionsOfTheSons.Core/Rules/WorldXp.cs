using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// XP and loot from the world outside enemies (COS-75, COTF's rules): animals pay kill XP (on the streak path) and the
/// valuable ones may drop items; a felled tree gives every player flat XP; a cut bush or sapling gives its cutter a little.
/// Not scaled by difficulty (COTF).
/// </summary>
public static class WorldXp
{
    /// <summary>Flat XP for felling a tree, to every player (no streak).</summary>
    public const double TreeXp = 30;

    /// <summary>Flat XP for cutting a bush or sapling: 3 to 5, to the cutter.</summary>
    public const int BushXpMin = 3, BushXpMax = 5;

    /// <summary>An animal worth more than this may drop one item (default rule).</summary>
    public const double DropWorthFloor = 82;

    public const double DefaultDropChance = 0.5;

    /// <summary>A kill of this animal: XP between its min and max.</summary>
    public static double AnimalXp(AnimalDef a, Rng rng) =>
        a.XpMax <= a.XpMin ? a.XpMin : Math.Round(a.XpMin + rng.NextDouble() * (a.XpMax - a.XpMin));

    /// <summary>The worths the kill's drops are rolled at (empty: no drop).</summary>
    public static IReadOnlyList<double> DropWorths(AnimalDef a, double xp, Rng rng)
    {
        double chance = a.DropChance ?? (xp > DropWorthFloor ? DefaultDropChance : 0);
        if (chance <= 0 || rng.NextDouble() >= chance) return Array.Empty<double>();
        return a.DropWorths.Count > 0 ? a.DropWorths : new[] { xp };
    }

    public static double BushXp(Rng rng) => rng.RangeInclusive(BushXpMin, BushXpMax);
}
