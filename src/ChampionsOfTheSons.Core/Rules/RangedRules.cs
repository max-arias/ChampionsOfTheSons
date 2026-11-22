namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Which ranged weapon fired a shot. The bridge tells them apart by the game item's name (COS-86 spike).</summary>
public enum RangedKind
{
    /// <summary>A projectile whose weapon is unknown (no shot seen, or a weapon we don't classify).</summary>
    Other,
    Bow,
    Crossbow,
    /// <summary>Firearms: pistol, revolver, shotgun, rifle.</summary>
    Bullet,
    Slingshot,
    ThrownSpear,
}

/// <summary>What the bridge knows about a projectile hit: the weapon that fired it, how far the target was, and whether it struck a head collider.</summary>
public readonly record struct RangedShot(RangedKind Kind, double Distance, bool HeadNode, bool Seeking = false);

/// <summary>A ranged hit after the ranged rules: the hit itself, how many extra copies land (True Aim), and whether it counts as a headshot.</summary>
public readonly record struct RangedHit(OutgoingHit Hit, int ExtraHits, bool Headshot);

/// <summary>Stat IDs the ranged and melee perk rules read (COS-86).</summary>
public static class RangedStatIds
{
    public const string NoConsumeChance = "perk_projectile_no_consume_chance";
    public const string BowDamage = "perk_bow_damage_mult";
    public const string CrossbowDamage = "perk_crossbow_damage_mult";
    public const string BulletDamage = "perk_bullet_damage_mult";
    /// <summary>Chance a bullet hit counts as a headshot (default 10%).</summary>
    public const string BulletHeadshotChance = "perk_bullet_crit_chance";
    /// <summary>Chance a thrown-spear hit counts as a headshot (default 5%).</summary>
    public const string SpearHeadshotChance = "perk_thrown_spear_crit_chance";
    public const string SecondSpearChance = "perk_thrown_spearhell_chance";
    public const string SpearArmorStrip = "perk_thrown_spear_extra_armor_reduction";
    public const string DamagePerDistance = "projectile_damage_per_distance";
    public const string DamageBySize = "perk_projectile_damage_increased_by_size";
    public const string CritBySpeed = "perk_projectile_damage_increased_by_speed";
    public const string TrueAim = "perk_true_aim";
    public const string TrueAimUpgrade = "perk_true_aim_upgrade";
    public const string AreaChance = "area_damage_chance";
    public const string AreaDamage = "area_damage";
    public const string AreaRadius = "area_damage_radius";
    public const string StaminaOnHit = "stamina_on_hit";
    public const string FireDance = "perk_dance_of_firegod";
    public const string FireDanceLock = "perk_dance_of_firegod_atk_cap";
}

/// <summary>Ranged combat rules from COTF (per-weapon multipliers, rolled headshots, distance, size and speed perks, spears).
/// Numbers come from content stats; the constants here are the item and perk texts' own (docs/mechanics.md).</summary>
public static class RangedRules
{
    /// <summary>True Aim: arrow headshots beyond 60 ft (18.3 m) deal ×1.5 and hit once more.</summary>
    public const double TrueAimDistance = 18.288, TrueAimMult = 1.5;
    public const int TrueAimExtraHits = 1;
    /// <summary>Palelight Cuirass: arrow headshots beyond 120 ft (36.6 m) deal ×5 and hit two extra times.</summary>
    public const double TrueAimUpgradeDistance = 36.576, TrueAimUpgradeMult = 5;
    public const int TrueAimUpgradeExtraHits = 2;
    /// <summary>Ember Heart: melee damage × (1 + attack speed × 1.1) while Black Flame burns. Measured Breath fixes the swing speed at ×2.</summary>
    public const double FireDanceFactor = 1.1, FireDanceLockedSpeed = 2;

    /// <summary>The weapon kind of a game item, from its item name ("Crossbow", "CompoundBow", "Pistol", "CraftedSpear", ...).</summary>
    public static RangedKind KindOf(string? itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return RangedKind.Other;
        bool Has(string s) => itemName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        if (Has("crossbow")) return RangedKind.Crossbow;
        if (Has("slingshot")) return RangedKind.Slingshot;
        if (Has("bow")) return RangedKind.Bow;
        if (Has("spear")) return RangedKind.ThrownSpear;
        if (Has("pistol") || Has("revolver") || Has("shotgun") || Has("rifle") || Has("gun") || Has("flintlock") || Has("musket"))
            return RangedKind.Bullet;
        return RangedKind.Other;
    }

    /// <summary>The perk and item multiplier for one weapon kind.</summary>
    public static double WeaponMultiplier(StatSheet s, RangedKind kind) => kind switch
    {
        RangedKind.Bow => s.Get(RangedStatIds.BowDamage),
        RangedKind.Crossbow => s.Get(RangedStatIds.CrossbowDamage),
        RangedKind.Bullet => s.Get(RangedStatIds.BulletDamage),
        RangedKind.ThrownSpear => s.Get(CombatStatIds.ThrownSpearDamage),
        _ => 1,
    };

    /// <summary>The chance a hit counts as a headshot without striking a head: bullets and thrown spears only.</summary>
    public static double RolledHeadshotChance(StatSheet s, RangedKind kind) => kind switch
    {
        RangedKind.Bullet => Math.Clamp(s.Get(RangedStatIds.BulletHeadshotChance), 0, 1),
        RangedKind.ThrownSpear => Math.Clamp(s.Get(RangedStatIds.SpearHeadshotChance), 0, 1),
        _ => 0,
    };

    /// <summary>A shot or throw keeps its ammo with this chance.</summary>
    public static bool KeepsAmmo(StatSheet s, Rng rng) => rng.Chance(Math.Clamp(s.Get(RangedStatIds.NoConsumeChance), 0, 1));

    /// <summary>Double Spears: chance that a thrown spear that hit sends a second spear at the same target.</summary>
    public static double SecondSpearChance(StatSheet s, RangedKind kind) =>
        kind == RangedKind.ThrownSpear ? Math.Clamp(s.Get(RangedStatIds.SecondSpearChance), 0, 1) : 0;

    /// <summary>True Aim and its upgrade: (damage factor, extra hits) for an arrow headshot at a distance (not Seeking Arrow).</summary>
    public static (double Mult, int ExtraHits) TrueAim(StatSheet s, RangedShot shot, bool headshot)
    {
        if (!headshot || shot.Seeking || shot.Kind is not (RangedKind.Bow or RangedKind.Crossbow)) return (1, 0);
        if (s.IsOn(RangedStatIds.TrueAimUpgrade) && shot.Distance > TrueAimUpgradeDistance) return (TrueAimUpgradeMult, TrueAimUpgradeExtraHits);
        if (s.IsOn(RangedStatIds.TrueAim) && shot.Distance > TrueAimDistance) return (TrueAimMult, TrueAimExtraHits);
        return (1, 0);
    }

    /// <summary>
    /// A ranged weapon hit: <c>(base + rangedFlat) × crit × Ranged × weapon × size × (1 + distance × perMetre) × 0.75^i × headshot × True Aim</c>.
    /// The crit multiplier gains <c>projectile speed − 1</c> with the speed perk; the size perk multiplies by projectile size.
    /// A bullet or thrown spear also counts as a headshot with its rolled chance. Spears with the armor-strip perk pierce
    /// <c>3 × ranged pierce + melee pierce</c>.
    /// </summary>
    public static RangedHit Hit(StatSheet s, double baseDamage, RangedShot shot, Rng rng, int multishotIndex = 0)
    {
        bool crit = rng.Chance(s.Get(StatIds.CritChance));
        double critMult = 1;
        if (crit)
        {
            critMult = 1 + s.Get(StatIds.CritDamage);
            if (s.IsOn(RangedStatIds.CritBySpeed)) critMult += Math.Max(0, s.Get(CombatStatIds.ProjectileSpeed)) - 1;
        }
        double d = (baseDamage + s.Get(StatIds.RangedFlatDmg)) * critMult * Derived.RangedMultiplier(s) * WeaponMultiplier(s, shot.Kind);
        if (s.IsOn(RangedStatIds.DamageBySize)) d *= Math.Max(0, s.Get(CombatStatIds.ProjectileSize));
        d *= 1 + Math.Max(0, shot.Distance) * s.Get(RangedStatIds.DamagePerDistance);
        d *= Math.Pow(0.75, Math.Max(0, multishotIndex));

        bool headshot = shot.HeadNode || rng.Chance(RolledHeadshotChance(s, shot.Kind));
        if (headshot) d *= s.Get(StatIds.HeadShotDamage);
        var (aimMult, extra) = TrueAim(s, shot, headshot);
        d *= aimMult;

        double pierce = Derived.RangedArmorPierce(s);
        if (shot.Kind == RangedKind.ThrownSpear && s.IsOn(RangedStatIds.SpearArmorStrip))
            pierce = 3 * Derived.RangedArmorPierce(s) + Derived.MeleeArmorPierce(s);
        return new RangedHit(new OutgoingHit(Math.Max(0, d), DamageKind.Physical, crit, pierce), extra, headshot);
    }

    /// <summary>Area damage: a ranged hit splashes <c>hit × area_damage</c> on enemies within the radius with this chance (0 when nothing grants area damage).</summary>
    public static double AreaSplash(StatSheet s, double hitDamage, Rng rng)
    {
        double factor = s.Get(RangedStatIds.AreaDamage);
        if (factor <= 0 || !rng.Chance(Math.Clamp(s.Get(RangedStatIds.AreaChance), 0, 1))) return 0;
        return hitDamage * factor;
    }
}

/// <summary>Melee perk rules (COS-86).</summary>
public static class MeleeRules
{
    public const string HeavyAttackDamage = "heavy_attack_dmg";
    public const string SmashDamage = "smash_damage";

    /// <summary>
    /// A charged melee attack (attack held, then released: the game's heavy attack) deals x heavy attack damage; with a hammer it
    /// is a smash and also deals x smash damage (COS-148). Plain swings are unchanged.
    /// </summary>
    public static double ChargedMultiplier(StatSheet s, bool charged, bool hammer) =>
        !charged ? 1 : Math.Max(0, s.Get(HeavyAttackDamage)) * (hammer ? Math.Max(0, s.Get(SmashDamage)) : 1);

    /// <summary>Ember Heart: melee damage factor while Black Flame burns.</summary>
    public static double FireDanceMultiplier(StatSheet s, bool blackFlameOn) =>
        blackFlameOn && s.IsOn(RangedStatIds.FireDance) ? 1 + Math.Max(0, s.Get(StatIds.AttackSpeed)) * RangedRules.FireDanceFactor : 1;

    /// <summary>Ember Heart: Black Flame's upkeep scales with attack speed.</summary>
    public static double FireDanceUpkeep(StatSheet s) => s.IsOn(RangedStatIds.FireDance) ? Math.Max(0.1, s.Get(StatIds.AttackSpeed)) : 1;

    /// <summary>The swing speed the game is given: Measured Breath fixes it while Black Flame burns, otherwise the attack speed stat.</summary>
    public static double SwingSpeed(StatSheet s, bool blackFlameOn) =>
        blackFlameOn && s.IsOn(RangedStatIds.FireDanceLock) ? RangedRules.FireDanceLockedSpeed : Math.Max(0.1, s.Get(StatIds.AttackSpeed));

    /// <summary>Stamina restored per melee hit that lands.</summary>
    public static double StaminaOnHit(StatSheet s) => Math.Max(0, s.Get(RangedStatIds.StaminaOnHit));
}
