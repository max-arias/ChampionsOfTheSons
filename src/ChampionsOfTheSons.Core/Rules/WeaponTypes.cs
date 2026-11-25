using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>What the player holds, as far as weapon types care (the bridge classifies the game's held item).</summary>
public enum HeldKind
{
    None,
    Melee,
    Spear,
    Bow,
    /// <summary>Anything else: tools, torches, guns, the guide book.</summary>
    Other,
}

/// <summary>
/// Weapon type rules (COS-93). The weapon slot item's type is a game weapon of its own (COS-186), built on one of the
/// game's weapons: its model replaces that weapon's in hand and its rules replace or scale that weapon's.
/// </summary>
public static class WeaponTypes
{
    /// <summary>The item type of weapons.</summary>
    public const string WeaponType = "weapon";

    /// <summary>The old weapon equipment slot (before COS-212). Saves that still hold an item there move it into the bag on load.</summary>
    public const string LegacyWeaponSlot = "equip.weapon";

    /// <summary>Whether an item is a weapon (goes on the quickbar, COS-212).</summary>
    public static bool IsWeapon(ItemInstance? item, ContentDb content) =>
        item != null && content.Items.TryGetValue(item.BaseId, out var def) && def.Type == WeaponType;

    /// <summary>The weapon type of an item, or null (no item, or an item without a type).</summary>
    public static WeaponTypeDef? TypeOf(ItemInstance? item, ContentDb content)
    {
        if (item == null || !content.Items.TryGetValue(item.BaseId, out var def) || def.WeaponModel == null) return null;
        return content.WeaponTypes.TryGetValue(def.WeaponModel, out var type) ? type : null;
    }

    /// <summary>The type of the player's selected weapon (the last quickbar key pressed), or null.</summary>
    public static WeaponTypeDef? Selected(PlayerCharacter pc) => TypeOf(pc.SelectedWeapon, pc.Content);

    public static bool Fits(WeaponTypeDef type, HeldKind held) => type.Base switch
    {
        WeaponBase.Melee => held == HeldKind.Melee,
        WeaponBase.Spear => held == HeldKind.Spear,
        WeaponBase.Bow => held == HeldKind.Bow,
        _ => false,
    };

    /// <summary>Base damage of a melee hit before the player's stats: the type's own damage, else the game weapon's.</summary>
    public static double MeleeBaseDamage(WeaponTypeDef? type, double gameDamage) =>
        type?.Damage is double d && type.Base != WeaponBase.Bow ? d : gameDamage;

    /// <summary>Base damage of a bow shot: (game damage + type flat) × type multiplier.</summary>
    public static double RangedBaseDamage(WeaponTypeDef? type, double gameDamage) =>
        type == null || type.Base != WeaponBase.Bow ? gameDamage : (gameDamage + type.RangedFlatDamage) * type.RangedDamageMult;

    /// <summary>The type damage <see cref="WeaponDamage"/> is measured against (a type with this damage gets the curve itself).</summary>
    public const double ReferenceTypeDamage = 20;

    /// <summary>Hits an on-level Legendary weapon of reference damage needs, alone, on a plain on-level enemy (COS-191, Max).</summary>
    public const double LegendaryHitsToKill = 10;

    /// <summary>Hits in which a Legendary weapon's own pierce strips a plain on-level enemy's mean armor (COS-191).</summary>
    public const double LegendaryHitsToStrip = 2;

    /// <summary>Levels a plain "on-level" enemy has above the player: the default kind extra of <see cref="EnemyScaler.RollLevel"/>.</summary>
    public const int OnLevelExtra = 1;

    private static double RarityShare(Rarity rarity) =>
        LootRoller.RarityMultiplier[Rarities.Clamp((int)rarity)] / LootRoller.RarityMultiplier[Rarities.Max];

    /// <summary>
    /// COS-191: base damage of a hit with a mod weapon, before the player's stats: it grows with the item's level like enemy
    /// health, so an on-level Legendary alone kills a plain on-level enemy (100 vanilla health) in about
    /// <see cref="LegendaryHitsToKill"/> hits once its armor is stripped, at every level. Lower rarities take the loot rarity
    /// ratio; the type's damage scales it (a bow uses the reference damage times its ranged multiplier).
    /// </summary>
    public static double WeaponDamage(WeaponTypeDef? type, int itemLevel, Rarity rarity)
    {
        double typeScale = type == null ? 1
            : type.Base == WeaponBase.Bow ? type.RangedDamageMult
            : (type.Damage ?? ReferenceTypeDamage) / ReferenceTypeDamage;
        return typeScale * RarityShare(rarity) * EnemyScaler.HealthCurve.At(Math.Max(1, itemLevel) + OnLevelExtra) / LegendaryHitsToKill;
    }

    /// <summary>COS-191: armor pierce of each hit with a mod weapon: an on-level Legendary strips a plain enemy's mean armor in
    /// <see cref="LegendaryHitsToStrip"/> hits; lower rarities take the loot rarity ratio.</summary>
    public static double WeaponPierce(int itemLevel, Rarity rarity) =>
        RarityShare(rarity) * EnemyScaler.MeanArmor(Math.Max(1, itemLevel) + OnLevelExtra) / LegendaryHitsToStrip;

    public static double SwingSpeedFactor(WeaponTypeDef? type) => type == null || type.Base == WeaponBase.Bow ? 1 : type.SwingSpeed;
    public static double StaminaCostFactor(WeaponTypeDef? type) => type == null || type.Base == WeaponBase.Bow ? 1 : type.StaminaCost;
    public static double ReachFactor(WeaponTypeDef? type) => type == null || type.Base == WeaponBase.Bow ? 1 : type.Reach;

    /// <summary>Whether a hit on a tree does damage: types like swords and hammers cannot fell trees.</summary>
    public static bool CanCutTrees(WeaponTypeDef? type) => type == null || type.CutsTrees;

    /// <summary>Whether a held spear may be thrown: only types built on the spear can forbid it.</summary>
    public static bool CanThrow(WeaponTypeDef? type) => type == null || type.Base != WeaponBase.Spear || type.CanThrow;

    /// <summary>Multiplier of the time between bow shots (a slower draw is a longer wait).</summary>
    public static double ShotDelayFactor(WeaponTypeDef? type) => type == null || type.Base != WeaponBase.Bow ? 1 : 1 / type.DrawSpeed;

    /// <summary>
    /// The hammer rule: with the hammer stun stat, every hit of a type with a stun slow slows the enemy
    /// (speed multiplier, seconds), or null. COTF slowed to 25% for 0.4 s.
    /// </summary>
    public static (double SpeedMult, double Seconds)? HitSlow(WeaponTypeDef? type, StatSheet stats)
    {
        if (type == null || type.StunSlowSeconds <= 0 || type.StunSlow <= 0) return null;
        if (stats.Get(CombatStatIds.HammerStun) <= 0) return null;
        return (type.StunSlow, type.StunSlowSeconds);
    }
}
