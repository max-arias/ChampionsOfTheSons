namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Spell damage on the world (COS-213): animals take the spell's hit as it is; trees have a health pool that spell damage
/// wears down; player-built structures take a share of the hit in the game's own damage units. Own numbers.
/// </summary>
public static class WorldDamageRules
{
    /// <summary>Spell damage a standing tree takes before it falls.</summary>
    public const double TreeHealth = 250;

    /// <summary>Share of a spell hit a structure takes (the game's structure health is in vanilla weapon units).</summary>
    public const double StructureShare = 0.5;

    /// <summary>Seconds without a spell hit after which a damaged tree is whole again.</summary>
    public const double TreeForgetSeconds = 120;

    /// <summary>One spell hit on a tree with <paramref name="left"/> health: what is left, and whether it falls.</summary>
    public static (double Left, bool Felled) TreeHit(double left, double amount)
    {
        if (amount <= 0) return (left, false);
        left -= amount;
        return left <= 0 ? (0, true) : (left, false);
    }

    public static double Structure(double amount) => Math.Max(0, amount) * StructureShare;
}
