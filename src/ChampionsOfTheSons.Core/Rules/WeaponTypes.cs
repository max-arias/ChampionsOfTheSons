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
/// Weapon type rules (COS-93). The equipped weapon item's type is active only while the player holds a game weapon it
/// can be built on: its model replaces the game weapon's in hand and its rules replace or scale the game weapon's.
/// </summary>
public static class WeaponTypes
{
    public const string WeaponSlot = "equip.weapon";

    /// <summary>The type of the weapon item in the weapon slot, or null (no item, or an item without a type).</summary>
    public static WeaponTypeDef? Equipped(Inventory inventory, ContentDb content)
    {
        var item = inventory.Get(WeaponSlot);
        if (item == null || !content.Items.TryGetValue(item.BaseId, out var def) || def.WeaponModel == null) return null;
        return content.WeaponTypes.TryGetValue(def.WeaponModel, out var type) ? type : null;
    }

    public static bool Fits(WeaponTypeDef type, HeldKind held) => type.Base switch
    {
        WeaponBase.Melee => held == HeldKind.Melee,
        WeaponBase.Spear => held == HeldKind.Spear,
        WeaponBase.Bow => held == HeldKind.Bow,
        _ => false,
    };

    /// <summary>The type in effect: the equipped item's type when the held game weapon fits it, else null.</summary>
    public static WeaponTypeDef? Active(Inventory inventory, ContentDb content, HeldKind held)
    {
        var type = Equipped(inventory, content);
        return type != null && Fits(type, held) ? type : null;
    }

    /// <summary>Base damage of a melee hit before the player's stats: the type's own damage, else the game weapon's.</summary>
    public static double MeleeBaseDamage(WeaponTypeDef? type, double gameDamage) =>
        type?.Damage is double d && type.Base != WeaponBase.Bow ? d : gameDamage;

    /// <summary>Base damage of a bow shot: (game damage + type flat) × type multiplier.</summary>
    public static double RangedBaseDamage(WeaponTypeDef? type, double gameDamage) =>
        type == null || type.Base != WeaponBase.Bow ? gameDamage : (gameDamage + type.RangedFlatDamage) * type.RangedDamageMult;

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
