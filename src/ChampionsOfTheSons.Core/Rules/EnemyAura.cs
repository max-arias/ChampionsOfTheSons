namespace ChampionsOfTheSons.Core.Rules;

/// <summary>The element a boss's power-up aura shows (COS-110, Max 2026-11-21).</summary>
public enum EnemyElement : byte
{
    None,
    Fire,
    Cold,
    Arcane,
    Poison,
    Holy,
}

/// <summary>
/// How a miniboss's or boss's aura looks (COS-110, Max 2026-11-21): its element comes from its abilities (the first one in its
/// list that has an element); one without an element gets only a light ring at its feet. The aura grows with the tier and with
/// the number of abilities.
/// </summary>
public static class EnemyAura
{
    public static EnemyElement ElementOf(EnemyAbility ability) => ability switch
    {
        EnemyAbility.Meteor or EnemyAbility.Flare or EnemyAbility.FireCataclysm => EnemyElement.Fire,
        EnemyAbility.Blizzard => EnemyElement.Cold,
        EnemyAbility.ArcaneCataclysm or EnemyAbility.BlackHole or EnemyAbility.Warp or EnemyAbility.Laser => EnemyElement.Arcane,
        EnemyAbility.Poisonous => EnemyElement.Poison,
        EnemyAbility.Radiance => EnemyElement.Holy,
        _ => EnemyElement.None,
    };

    public static EnemyElement ElementOf(IEnumerable<EnemyAbility> abilities)
    {
        foreach (var a in abilities)
            if (ElementOf(a) is var e && e != EnemyElement.None) return e;
        return EnemyElement.None;
    }

    /// <summary>The most abilities that still grow the aura (a story boss has ten).</summary>
    public const int MaxAbilities = 10;

    /// <summary>Aura strength, 0 for elites and normal enemies: a miniboss 0.4 to 0.7, a boss 0.7 to 1.0, rising with the ability count.</summary>
    public static double Intensity(EnemyTier tier, int abilityCount)
    {
        double share = Math.Clamp(abilityCount, 0, MaxAbilities) / (double)MaxAbilities;
        return tier switch
        {
            EnemyTier.Boss => 0.7 + 0.3 * share,
            EnemyTier.Miniboss => 0.4 + 0.3 * share,
            _ => 0,
        };
    }
}
