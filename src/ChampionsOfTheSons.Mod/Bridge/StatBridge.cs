using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using Sons.Items.Core;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// Applies the item stats that PlayerBridge and the enemy hooks don't: attack speed and attack cost (weapon data), projectile speed,
/// size and pierce (bullet data), energy (the game's stamina bar: maximum, regeneration), and the unique items' per-frame effects.
/// Weapon and bullet data are shared assets, so each one's original values are remembered and every write is "original × stat".
/// </summary>
public sealed class StatBridge : IModFeature
{
    private sealed record MeleeOriginal(float Swing, float SwingTired, float Cost);
    private sealed record BulletOriginal(float Speed, float Diameter, float Weight);

    private static readonly Dictionary<IntPtr, MeleeOriginal> MeleeData = new();
    private static readonly Dictionary<IntPtr, BulletOriginal> BulletData = new();
    private static readonly Dictionary<IntPtr, float> FireDelays = new();
    private static float _vanillaStaminaMax = -1;
    private static float _scanTimer;

    private static StatSheet Sheet => PlayerState.Character.Stats;

    // ------------------------------------------------------------------ values the game is given (also what self-tests probe)

    public static double AttackSpeed => Math.Max(0.1, Sheet.Get(StatIds.AttackSpeed));
    public static double AttackCost => Math.Max(0, Sheet.Get(CombatStatIds.AttackStaminaCost));
    public static double ProjectileSpeed => Math.Max(0.1, Sheet.Get(CombatStatIds.ProjectileSpeed));
    public static double ProjectileSize => Math.Max(0.1, Sheet.Get(CombatStatIds.ProjectileSize));
    /// <summary>Pierce raises the bullet's weight (its penetration energy) by up to 4×.</summary>
    public static double ProjectilePenetration => 1 + 3 * Math.Clamp(Sheet.Get(StatIds.PierceChance), 0, 1);
    public static double StaminaMaxFactor => Derived.MaxEnergy(Sheet) / 100.0;
    /// <summary>Extra stamina per second on top of the game's own regeneration, given the game's rate.</summary>
    public static double ExtraStaminaPerSecond(double gameRate)
    {
        double rel = Derived.EnergyRecoveryMultiplier(Sheet) / 2.0; // neutral sheet = 2
        return Sheet.Get(CombatStatIds.StaminaPerSecond) + Sheet.Get(CombatStatIds.EnergyPerSecond) + gameRate * (rel - 1);
    }

    /// <summary>The held melee weapon's swing speed as the game now has it (ratio to the original), or null with no weapon seen.</summary>
    public static double? MeleeSwingRatio => MeleeData.Count == 0 ? null : LastMeleeRatio;
    public static double? BulletSpeedRatio => BulletData.Count == 0 ? null : LastBulletRatio;
    public static double? BulletSizeRatio => BulletData.Count == 0 ? null : LastBulletSize;
    private static double LastMeleeRatio, LastBulletRatio, LastBulletSize;

    public void Update(float dt, bool inWorld)
    {
        if (inWorld) Frame(dt);
    }

    /// <summary>One frame of per-frame stat effects (self-tests step it themselves).</summary>
    public static void Frame(float dt)
    {
        if (LocalPlayer.Vitals == null) return;
        PlayerEffects.Tick(dt);
        ApplyEnergy(dt);
        ApplyUniqueRegen(dt);

        _scanTimer -= dt;
        if (_scanTimer <= 0 || dt >= 0.5f)
        {
            _scanTimer = 0.5f;
            ScanWeapons();
        }
    }

    public void RegisterSelfTests() => StatSelfTest.Register();

    public void OnGameStart()
    {
        PlayerEffects.Reset();
        MeleeData.Clear();
        BulletData.Clear();
        FireDelays.Clear();
        _vanillaStaminaMax = -1;
    }

    private static void ApplyEnergy(float dt)
    {
        var stamina = LocalPlayer.Vitals.Stamina;
        if (stamina == null) return;
        if (_vanillaStaminaMax <= 0) _vanillaStaminaMax = stamina.GetMax();
        float wantMax = (float)(_vanillaStaminaMax * StaminaMaxFactor);
        if (wantMax > 0 && Mathf.Abs(stamina.GetMax() - wantMax) > 0.01f)
        {
            float ratio = stamina.GetMax() > 0 ? stamina._currentValue / stamina.GetMax() : 1;
            stamina.SetMax(wantMax);
            stamina.SetCurrentValue(Mathf.Clamp(ratio * wantMax, 0, wantMax));
        }
        double extra = ExtraStaminaPerSecond(stamina.GetRegenRate()) * dt;
        if (extra > 0 && stamina._currentValue < stamina.GetMax()) PlayerEffects.AddEnergy(extra);
    }

    /// <summary>Lama Mega's Blood Bag: regenerate while low.</summary>
    private static void ApplyUniqueRegen(float dt)
    {
        var health = LocalPlayer.Vitals.Health;
        if (health == null || health._currentValue <= 0 || health.GetMax() <= 0) return;
        double rate = UniqueItems.BloodBagRegen(PlayerState.Character.Inventory, health._currentValue / health.GetMax());
        if (rate > 0) PlayerEffects.Heal(rate * health.GetMax() * dt);
    }

    private static void ScanWeapons()
    {
        try
        {
            double speed = AttackSpeed, cost = AttackCost;
            // Measured Breath: while Black Flame burns, the swing speed is fixed (COS-86).
            double swing = MeleeRules.SwingSpeed(Sheet, PerkHooks.PerkHooksFeature.SpellRunning("spell.black_flame"));
            foreach (var weapon in Resources.FindObjectsOfTypeAll<MeleeWeaponController>())
            {
                var data = weapon._meleeWeaponData;
                if (data == null || !weapon.gameObject.activeInHierarchy) continue;
                if (!MeleeData.TryGetValue(data.Pointer, out var orig))
                    MeleeData[data.Pointer] = orig = new MeleeOriginal(data.SwingSpeed, data.SwingSpeedTired, data.StaminaCost);
                data.SwingSpeed = orig.Swing * (float)swing;
                data.SwingSpeedTired = orig.SwingTired * (float)swing;
                data.StaminaCost = orig.Cost * (float)cost;
                LastMeleeRatio = orig.Swing > 0 ? data.SwingSpeed / orig.Swing : swing;
            }

            double size = ProjectileSize, projSpeed = ProjectileSpeed, pen = ProjectilePenetration;
            foreach (var weapon in Resources.FindObjectsOfTypeAll<RangedWeapon>())
            {
                var info = weapon.bulletInfo;
                if (info == null || !weapon._isLocalPlayer) continue;
                if (!BulletData.TryGetValue(info.Pointer, out var orig))
                    BulletData[info.Pointer] = orig = new BulletOriginal(info.maxSpeed, info.diameter, info.weight);
                info.maxSpeed = orig.Speed * (float)projSpeed;
                info.diameter = orig.Diameter * (float)size;
                info.weight = orig.Weight * (float)pen;
                LastBulletRatio = orig.Speed > 0 ? info.maxSpeed / orig.Speed : projSpeed;
                LastBulletSize = orig.Diameter > 0 ? info.diameter / orig.Diameter : size;
            }

            foreach (var controller in Resources.FindObjectsOfTypeAll<RangedWeaponController>())
            {
                if (!controller.gameObject.activeInHierarchy) continue;
                if (!FireDelays.TryGetValue(controller.Pointer, out var delay)) FireDelays[controller.Pointer] = delay = controller._fireDelay;
                controller._fireDelay = delay / (float)speed;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: weapon stats failed: {e}");
        }
    }
}

/// <summary>Which player stat feeds which part of the game, so a missing hookup is a failing check and not a silent no-op (COS-16).</summary>
public static class StatCoverage
{
    /// <summary>Stats consumed by the rules core (damage, defense, derived values, XP, loot, spells) and the player bridge.</summary>
    public static readonly HashSet<string> Core = new()
    {
        StatIds.Strength, StatIds.Agility, StatIds.Vitality, StatIds.Intelligence,
        StatIds.MaxHealth, StatIds.MaxHealthMult, StatIds.MaxHealthFromVit,
        StatIds.AllDamage, StatIds.MeleeIncreasedDmg, StatIds.RangedIncreasedDmg, StatIds.SpellIncreasedDmg,
        StatIds.MeleeDmgFromStr, StatIds.RangedDmgFromAgi, StatIds.SpellDmgFromInt,
        StatIds.MeleeFlatDmg, StatIds.RangedFlatDmg, StatIds.SpellFlatDmg,
        StatIds.CritChance, StatIds.CritDamage, StatIds.AllArmorPiercing, StatIds.MeleeArmorPiercing, StatIds.RangedArmorPiercing,
        StatIds.Armor, StatIds.AllDamageTaken, StatIds.MagicDamageTaken, StatIds.GetHitChance, StatIds.Block,
        StatIds.MagicFind, StatIds.ExpGain, StatIds.MaxMassacreTime,
        StatIds.MovementSpeed, StatIds.JumpPower,
        StatIds.HealthRecoveryPerSecond, StatIds.HealthPerSecRate, StatIds.AllRecoveryMult,
        StatIds.SpellCost, StatIds.SpellCostEnergyCost, StatIds.Cooldown,
    };

    /// <summary>Stats this bridge applies to the game (StatBridge, EnemyPatches, PlayerBridge).</summary>
    public static readonly HashSet<string> Bridge = new()
    {
        StatIds.MaxEnergy, StatIds.MaxEnergyMult, StatIds.MaxEnergyFromAgi, StatIds.StaminaPerSecRate,
        CombatStatIds.StaminaPerSecond, CombatStatIds.EnergyPerSecond,
        StatIds.AttackSpeed, CombatStatIds.AttackStaminaCost,
        StatIds.HealthOnHit, StatIds.EnergyOnHit, StatIds.ChanceToSlow, StatIds.ChanceToBleed, StatIds.ChanceToWeaken,
        StatIds.Thorns, CombatStatIds.ThornsDmgMult, CombatStatIds.ThornsPerVit, CombatStatIds.ThornsPerStrength,
        CombatStatIds.ProjectileSpeed, CombatStatIds.ProjectileSize, StatIds.PierceChance,
        StatIds.HeadShotDamage, StatIds.FireDamage, CombatStatIds.ExplosionDamage,
        CombatStatIds.HammerStun, CombatStatIds.HammerStunChance, CombatStatIds.HammerStunDuration,
        CombatStatIds.StunImmunity, CombatStatIds.ThrownSpearDamage,
    };

    /// <summary>Unique-item and set-bonus stats read by the spells, the perk hooks and the unique-flag code (COS-88): the self-test fails
    /// for a unique or set stat that is in none of these sets.</summary>
    public static readonly HashSet<string> Readers = new()
    {
        "all_damage", "all_damage_taken", "armor", "attack_speed", "attack_stamina_cost", "chance_to_bleed", "chance_to_weaken",
        "cooldown_rate", "crit_chance", "crit_damage", "energy_recoveryper_second", "head_shot_damage", "health_on_hit",
        "i_crossfire_quiver", "i_death_pact_enabled", "i_eruption_bow", "i_hammer_stun_amount",
        "i_hammer_stun_duration", "i_hazard_crown", "i_hexed_pants_of_mr_m_enabled", "i_infinity_loop", "i_is_greed", "i_is_wind_armor",
        "i_king_qruies_sword", "i_sora_bracers", "i_spark_of_light_after_dark", "melee_increased_dmg", "movement_speed",
        "perk_bullet_crit_chance", "perk_bullet_damage_mult", "perk_crossbow_damage_mult", "perk_projectile_no_consume_chance",
        "perk_true_aim", "perk_true_aim_upgrade", "projectile_speed", "ranged_armor_piercing", "ranged_increased_dmg",
        "spell_bash_bleed_chance", "spell_bash_damage_debuff_amount", "spell_bash_lifesteal", "spell_berserk_attack_speed",
        "spell_berserk_damage", "spell_berserk_duration", "spell_berserk_movement_speed", "spell_bia_health_dm_mult",
        "spell_blackhole_duration", "spell_blackhole_radius", "spell_blink_damage", "spell_blink_do_explosion", "spell_blink_range",
        "spell_cataclysm_arcane", "spell_cataclysm_radius", "spell_chance_to_parry_on_hit", "spell_cost", "spell_firebolt_damage_scaling",
        "spell_firebolt_energy_cost", "spell_focus_on_atk_speed_duration", "spell_frenzy_active_crit_chance", "spell_frenzy_atk_speed",
        "spell_frenzy_dmg", "spell_frenzy_max_stacks", "spell_healing_dome_cooldown_rate", "spell_healing_dome_spell_cost_reduction",
        "spell_increased_dmg", "spell_magic_arrow_damage_scaling", "spell_magic_arrow_duration", "spell_magic_arrow_volley_count",
        "spell_portal_duration", "spell_purge_damage_bonus", "spell_snap_damage_scaling", "spell_snap_freeze_duration",
        "spell_snowstorm_damage_mult", "spell_snowstorm_hit_delay", "spell_snowstorm_pull_enemies_in", "spell_war_cry_give_armor",
        "spell_war_cry_give_damage_resistance", "stun_immunity", "thorns", "thorns_dmg_mult", "thorns_per_vit",
    };

    /// <summary>Kept in the data but deliberately without an in-game effect, with the reason.</summary>
    public static readonly Dictionary<string, string> Dropped = new()
    {
        [CombatStatIds.WeaponRange] = "needs per-weapon collider changes; no safe hook found",
        ["heavy_attack_dmg"] = "no verified heavy-attack signal",
        ["smash_damage"] = "no verified heavy-attack signal",
        ["fire_duration"] = "a test enemy could not be ignited through the game's burn API",
        ["fire_tick_rate"] = "a test enemy could not be ignited through the game's burn API",
        ["i_great_bow_ignites"] = "a test enemy could not be ignited through the game's burn API",
        ["i_archangel_bow"] = "needs a second player (co-op, COS-13)",
        ["i_smokey_crossbow_quiver"] = "needs the crossbow bolt to explode; no verified hook (COS-86 can tell crossbow shots apart now)",
        ["spell_snowstorm_max_charge"] = "Snow Storm has no charges yet (COS-83)",
        ["debuff_resistance"] = "player buff and debuff rules are COS-89",
        ["perk_golden_resolve"] = "the gold rule is not defined yet",
        ["perk_raft_speed_multipier"] = "rafts are a vanilla survival feature we leave alone",
        ["spell_blackhole_radius"] = "spell numbers: read by the spell system (COS-20/COS-25) when the spell is cast",
        ["spell_blackhole_duration"] = "spell numbers: read by the spell system",
        ["spell_blackhole_pullforce"] = "spell numbers: read by the spell system",
        ["spell_blackhole_damage"] = "spell numbers: read by the spell system",
        ["spell_snap_freeze_duration"] = "spell numbers: read by the spell system",
    };

    public static string? Classify(string playerStat) =>
        Core.Contains(playerStat) ? "core" : Bridge.Contains(playerStat) ? "bridge" : Readers.Contains(playerStat) ? "reader"
        : Dropped.ContainsKey(playerStat) ? "dropped" : null;
}
