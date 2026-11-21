namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Player stat IDs the rules read directly (all defined in content/player_stats.json).</summary>
public static class StatIds
{
    public const string Strength = "strength";
    public const string Agility = "agility";
    public const string Vitality = "vitality";
    public const string Intelligence = "intelligence";

    public const string MaxHealth = "max_health";
    public const string MaxHealthMult = "max_health_mult";
    public const string MaxHealthFromVit = "max_health_from_vit";
    public const string MaxEnergy = "max_energy";
    public const string MaxEnergyMult = "max_energy_mult";
    public const string MaxEnergyFromAgi = "max_energy_from_agi";

    public const string AllDamage = "all_damage";
    public const string MeleeIncreasedDmg = "melee_increased_dmg";
    public const string RangedIncreasedDmg = "ranged_increased_dmg";
    public const string SpellIncreasedDmg = "spell_increased_dmg";
    public const string MeleeDmgFromStr = "melee_dmg_from_str";
    public const string RangedDmgFromAgi = "ranged_dmg_from_agi";
    public const string SpellDmgFromInt = "spell_dmg_from_int";
    public const string MeleeFlatDmg = "melee_flat_dmg";
    public const string RangedFlatDmg = "ranged_flat_dmg";
    public const string SpellFlatDmg = "spell_flat_dmg";
    public const string HeavyAttackDmg = "heavy_attack_dmg";
    public const string HeadShotDamage = "head_shot_damage";
    public const string CritChance = "crit_chance";
    public const string CritDamage = "crit_damage";
    public const string AllArmorPiercing = "all_armor_piercing";
    public const string MeleeArmorPiercing = "melee_armor_piercing";
    public const string RangedArmorPiercing = "ranged_armor_piercing";
    public const string FireDamage = "fire_damage";
    public const string ChanceToSlow = "chance_to_slow";
    public const string ChanceToBleed = "chance_to_bleed";
    public const string ChanceToWeaken = "chance_to_weaken";
    public const string HealthOnHit = "health_on_hit";
    public const string EnergyOnHit = "energy_on_hit";
    /// <summary>Same pool as energy (one bar, COS-94): adds to energy on hit.</summary>
    public const string StaminaOnHit = "stamina_on_hit";
    public const string PierceChance = "projectile_pierce_chance";

    public const string Armor = "armor";
    public const string AllDamageTaken = "all_damage_taken";
    public const string MagicDamageTaken = "magic_damage_taken";
    public const string FireDamageTaken = "fire_damage_taken";
    public const string GetHitChance = "get_hit_chance";
    public const string Block = "block";
    public const string Thorns = "thorns";

    public const string MagicFind = "magic_find";
    public const string ExpGain = "exp_gain";
    public const string MaxMassacreTime = "max_massacre_time";
    public const string TimeBonusPerKill = "time_bonus_per_kill";

    public const string AttackSpeed = "attack_speed";
    public const string MovementSpeed = "movement_speed";
    public const string JumpPower = "jump_power";

    public const string SpellCost = "spell_cost";
    public const string SpellCostEnergyCost = "spell_cost_energy_cost";
    /// <summary>Share of a cast's cost that comes back over 3 s (Sinew Channel, COS-128).</summary>
    public const string SpellCostRefund = "spell_cost_refund";
    public const string Cooldown = "cooldown";
    public const string CooldownRate = "cooldown_rate";

    public const string HealthRecoveryPerSecond = "health_recovery_per_second";
    public const string HealthPerSecRate = "health_per_sec_rate";
    public const string AllRecoveryMult = "all_recovery_mult";
    public const string EnergyRecoveryFromInt = "energy_recovery_from_int";
    public const string StaminaPerSecRate = "stamina_per_sec_rate";
    /// <summary>Bool: no spell can be cast while on (COS-82; set by debuffs, COS-89).</summary>
    public const string Silenced = "silenced";
    public const string DebuffResistance = "debuff_resistance";
    public const string Rooted = "rooted", Stunned = "stunned", RootImmunity = "root_immunity", DebuffImmunity = "debuff_immunity";
}

/// <summary>Values computed from several stats (COTF's derived formulas).</summary>
public static class Derived
{
    public static double MaxHealth(StatSheet s) =>
        (100 + s.Get(StatIds.Vitality) * s.Get(StatIds.MaxHealthFromVit) + s.Get(StatIds.MaxHealth)) * s.Get(StatIds.MaxHealthMult);

    public static double MaxEnergy(StatSheet s) =>
        (100 + s.Get(StatIds.Agility) * s.Get(StatIds.MaxEnergyFromAgi) + s.Get(StatIds.MaxEnergy)) * s.Get(StatIds.MaxEnergyMult);

    public static double MeleeMultiplier(StatSheet s) =>
        s.Get(StatIds.AllDamage) * s.Get(StatIds.MeleeIncreasedDmg) * (1 + s.Get(StatIds.Strength) * s.Get(StatIds.MeleeDmgFromStr));

    public static double RangedMultiplier(StatSheet s) =>
        s.Get(StatIds.AllDamage) * s.Get(StatIds.RangedIncreasedDmg) * (1 + s.Get(StatIds.Agility) * s.Get(StatIds.RangedDmgFromAgi));

    public static double SpellMultiplier(StatSheet s) =>
        s.Get(StatIds.AllDamage) * s.Get(StatIds.SpellIncreasedDmg) * (1 + s.Get(StatIds.Intelligence) * s.Get(StatIds.SpellDmgFromInt));

    public static double MeleeArmorPierce(StatSheet s) => s.Get(StatIds.AllArmorPiercing) + s.Get(StatIds.MeleeArmorPiercing);

    public static double RangedArmorPierce(StatSheet s) => s.Get(StatIds.AllArmorPiercing) + s.Get(StatIds.RangedArmorPiercing);

    /// <summary>Health regenerated per second on top of the game's own regen.</summary>
    public static double HealthRegenPerSecond(StatSheet s, double vanillaRegen) =>
        (vanillaRegen + 0.0025 * MaxHealth(s) + s.Get(StatIds.HealthRecoveryPerSecond)) *
        (s.Get(StatIds.HealthPerSecRate) + 1) * s.Get(StatIds.AllRecoveryMult);

    public static double EnergyRecoveryMultiplier(StatSheet s) =>
        1 + (1 + s.Get(StatIds.Intelligence) * s.Get(StatIds.EnergyRecoveryFromInt)) * s.Get(StatIds.AllRecoveryMult) * s.Get(StatIds.StaminaPerSecRate);
}
