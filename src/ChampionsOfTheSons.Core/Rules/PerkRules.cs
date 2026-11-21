using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Stat IDs of the magic, defense, support and utility perks (COS-87) and the spell-item stats (COS-88).</summary>
public static class PerkStatIds
{
    public const string Infinity = "perk_infinity_magic";
    public const string BlackHolePullImmune = "perk_blackhole_pull_immune";
    public const string BlizzardSlowReduced = "perk_blizzard_slow_reduced";
    public const string ShieldAutocast = "perk_is_shield_autocast";
    public const string SmolderMark = "perk_fire_dmg_increase_on_hit";
    public const string FlashlightIntensity = "perk_flashlight_intensity";
    public const string FlashlightBattery = "perk_flashlight_battery_drain";
    public const string BunnyHop = "perk_bunny_hop";
    public const string BunnyHopUpgrade = "perk_bunny_hop_upgrade";

    public const string BlinkRange = "spell_blink_range";
    public const string BlinkDamage = "spell_blink_damage";
    public const string BlinkExplosion = "spell_blink_do_explosion";
    public const string WarCryArmor = "spell_war_cry_give_armor";
    public const string WarCryResistance = "spell_war_cry_give_damage_resistance";
    public const string BashBleedChance = "spell_bash_bleed_chance";
    public const string BashLifesteal = "spell_bash_lifesteal";
    public const string ParryOnHit = "spell_chance_to_parry_on_hit";
    public const string PurgeDamage = "spell_purge_damage_bonus";
    public const string FrenzyCrit = "spell_frenzy_active_crit_chance";
    public const string FireboltCost = "spell_firebolt_energy_cost";
    public const string DomeCooldownRate = "spell_healing_dome_cooldown_rate";
    public const string DomeCostReduction = "spell_healing_dome_spell_cost_reduction";
    public const string CataclysmArcane = "spell_cataclysm_arcane";
}

/// <summary>Rules and numbers of the COS-87 perks and the COS-88 spell items. Numbers follow the perk and item texts; where a text
/// leaves one open the original's is used (listed in docs/mechanics.md).</summary>
public static class PerkRules
{
    /// <summary>Infinity: each cast multiplies every slot's remaining cooldown by this.</summary>
    public const double InfinityFactor = 0.95;

    /// <summary>Gilded Pace: seconds off Golden Skin's base cooldown.</summary>
    public const double GoldenSkinCut = 15;

    /// <summary>Warmth: inside an enemy Blizzard the player moves at this factor instead of <see cref="ActiveAbilityRules.BlizzardSlow"/>.</summary>
    public const double WarmBlizzardSlow = 0.8;

    /// <summary>Autocast shield: Sustain Shield runs by itself while energy is at or above this fraction of max, for this upkeep per second.</summary>
    public const double AutocastAbove = 0.9, AutocastUpkeep = 10;

    /// <summary>Smolder Mark: how long the mark lasts, and the share of the spell power it adds (melee / ranged).</summary>
    public const double SmolderSeconds = 15, SmolderMelee = 0.35, SmolderRanged = 0.3;

    /// <summary>Momentum Transfer: landing gives move speed ×1.25 for 0.5 s × jump power; Bunny Hopping ×1.6 for 0.75 s × jump power.</summary>
    public const double HopBoost = 1.25, HopSeconds = 0.5, HopBoost2 = 1.6, HopSeconds2 = 0.75;

    /// <summary>Free slowdown perks: buyable only while the slowed stat is above this.</summary>
    public const double SlowdownBuyAbove = 1.1;

    /// <summary>Purgatory: Purge raises all damage by up to this (at 0 health), in proportion to missing health, for this long.</summary>
    public const double PurgeMaxBonus = 3, PurgeSeconds = 10;

    /// <summary>Iron horns: War Cry adds this share of the player's armor; the greater horn also gives damage taken ×0.9.</summary>
    public const double WarCryArmorShare = 0.1, WarCryDamageTaken = 0.9;

    /// <summary>Jagged Edge: Bash bleed per second as a share of the hit, for the slow's duration.</summary>
    public const double BashBleedShare = 0.3;

    /// <summary>Alexander's Shield: chance a hit taken triggers Parry by itself.</summary>
    public const double ParryOnHitChance = 0.2;

    /// <summary>Tearing Spacetime: exit explosion damage = base × movement speed, radius = blink distance × share.</summary>
    public const double BlinkExplosionDamage = 40, BlinkExplosionScaling = 2, BlinkExplosionRadiusShare = 0.2, BlinkExplosionMinRadius = 2;

    /// <summary>Smolder Mark bonus: (1 + spell flat / 3) × spell multiplier × (1 + fire damage) × 0.35 (0.3 for ranged hits).</summary>
    public static double SmolderMarkBonus(StatSheet s, bool ranged) =>
        !s.IsOn(PerkStatIds.SmolderMark) ? 0
        : (1 + s.Get(StatIds.SpellFlatDmg) / 3) * Derived.SpellMultiplier(s) * (1 + s.Get(StatIds.FireDamage)) * (ranged ? SmolderRanged : SmolderMelee);

    /// <summary>Momentum Transfer on landing: (move speed factor, seconds); (1, 0) without the perk.</summary>
    public static (double Mult, double Seconds) LandingBoost(StatSheet s)
    {
        if (!s.IsOn(PerkStatIds.BunnyHop)) return (1, 0);
        double jump = Math.Max(0, s.Get(StatIds.JumpPower));
        return s.IsOn(PerkStatIds.BunnyHopUpgrade) ? (HopBoost2, HopSeconds2 * jump) : (HopBoost, HopSeconds * jump);
    }

    /// <summary>Light the Way: flashlight intensity factor (the stat, 100% by default, +100% per stack) and battery drain factor (divided by the battery stat).</summary>
    public static (double Intensity, double Drain) Flashlight(StatSheet s) =>
        (Math.Max(0.1, s.Get(PerkStatIds.FlashlightIntensity)), 1 / Math.Max(0.1, s.Get(PerkStatIds.FlashlightBattery)));

    /// <summary>Autocast shield: should Sustain Shield run by itself at this energy?</summary>
    public static bool ShieldAutocasts(StatSheet s, double energy, double maxEnergy) =>
        s.IsOn(PerkStatIds.ShieldAutocast) && maxEnergy > 0 && energy >= maxEnergy * AutocastAbove;

    /// <summary>Purgatory: all-damage factor after a Purge at this health fraction.</summary>
    public static double PurgeDamage(StatSheet s, double healthFraction) =>
        s.IsOn(PerkStatIds.PurgeDamage) ? 1 + PurgeMaxBonus * Math.Clamp(1 - healthFraction, 0, 1) : 1;

    /// <summary>The buy condition of a perk with a custom one (canBuyManual). Unknown custom perks stay buyable.</summary>
    public static bool ManualBuyCheck(PerkDef perk, StatSheet s) => perk.Id switch
    {
        "perk.power_slowdown" => s.Get(StatIds.AttackSpeed) > SlowdownBuyAbove,
        "perk.power_slowdown_crit" => s.Get(CombatStatIds.ProjectileSpeed) > SlowdownBuyAbove,
        _ => true,
    };

    /// <summary>Energy cost added to a spell by items (Flame Pauldrons on Firebolt).
    /// The Firebolt stat starts at the original's base (5); only what perks and items add to it changes the cost.</summary>
    public static double CostAdd(string spellId, StatSheet s) =>
        spellId == "spell.firebolt" ? s.Get(PerkStatIds.FireboltCost) - s.GetDefault(PerkStatIds.FireboltCost) : 0;

    /// <summary>Cost factor of one spell: the Healing Dome bracer's stat starts at 1 and items lower it.
    /// Ember Heart: Black Flame's upkeep scales with attack speed.</summary>
    public static double CostScale(string spellId, StatSheet s) => spellId switch
    {
        "spell.healing_dome" => Math.Max(0, s.Get(PerkStatIds.DomeCostReduction)),
        "spell.black_flame" => MeleeRules.FireDanceUpkeep(s),
        _ => 1,
    };

    /// <summary>Extra cooldown rate for one spell (the Healing Dome bracer: +1.0 means twice as fast).</summary>
    public static double CooldownRate(string spellId, StatSheet s) =>
        spellId == "spell.healing_dome" ? 1 + Math.Max(0, s.Get(PerkStatIds.DomeCooldownRate)) : 1;
}
