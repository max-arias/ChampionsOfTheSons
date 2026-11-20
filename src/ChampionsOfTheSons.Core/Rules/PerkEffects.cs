using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Perks whose effect is an event or a game limit rather than a stat (the 'manual' perks that COS-49 implemented).
/// The original's behavior text is not available here: numbers follow the perk descriptions in content/text, anything the
/// description leaves open is this mod's own choice, listed in docs/mechanics.md ("Event perks").
/// </summary>
public static class PerkEffectIds
{
    public const string ImprovedDodges = "perk.improved_dodges";
    public const string DeployableShield = "perk.deployable_shield";
    public const string DeployableShield2 = "perk.deployable_shield_2";
    public const string AdrenalineRush = "perk.adrenaline_rush";
    public const string AdrenalineRush2 = "perk.adrenaline_rush_ii";
    public const string QuickSilver = "perk.quick_silver";
    public const string BlinkSpell = "spell.blink";
    public const string TimeOfNeed = "perk.time_of_need";
    public const string Falter = "perk.falter";
    public const string EnchantWeapon = "perk.enchant_weapon";
    public const string Afterburn = "perk.afterburn";
    public const string Netherflame = "perk.netherflame";
}

/// <summary>Numbers for the event perks.</summary>
public static class PerkTuning
{
    /// <summary>Improved Dodges: health healed on a dodge, as a fraction of max health.</summary>
    public const double DodgeHealFraction = 0.05;

    /// <summary>Deployable Shield: shield granted on a dodge (fraction of max health); Bulwark Reflex adds to it. It fades after <see cref="ShieldSeconds"/>.</summary>
    public const double ShieldFraction = 0.15, ShieldFraction2 = 0.10, ShieldSeconds = 8;

    /// <summary>Adrenaline Rush: triggers when a hit leaves health at or below this fraction; once per cooldown.</summary>
    public const double RushBelowHealth = 0.35, RushCooldown = 60;

    /// <summary>Adrenaline Rush / II: movement and attack speed bonus and duration.</summary>
    public const double RushBonus = 0.25, RushSeconds = 6, RushBonus2 = 0.5, RushSeconds2 = 10;

    /// <summary>Quick Silver: seconds off the Blink cooldown (applied before cooldown multipliers).</summary>
    public const double QuickSilverBlink = 6;

    public static double ShieldFor(PerkTree perks, double maxHealth) =>
        perks.TimesBought(PerkEffectIds.DeployableShield) == 0
            ? 0
            : maxHealth * (ShieldFraction + (perks.TimesBought(PerkEffectIds.DeployableShield2) > 0 ? ShieldFraction2 : 0));

    /// <summary>Spell perks that cut a spell's base cooldown by flat seconds (numbers from the perk texts).</summary>
    public static readonly IReadOnlyList<(string Perk, string Spell, double Seconds)> CooldownCuts = new[]
    {
        (PerkEffectIds.QuickSilver, PerkEffectIds.BlinkSpell, QuickSilverBlink),
        ("perk.endless_stream", "spell.magic_arrow", 15.0),
        ("perk.storm_season", "spell.ball_lightning", 40.0),
        ("perk.endless_storm", "spell.ball_lightning", 25.0),
        ("perk.wrath_of_the_sun", "spell.sun_flare", 20.0),
        ("perk.sudden_rampage", "spell.berserk", 90.0),
        ("perk.golden_cooldown", "spell.golden_skin", PerkRules.GoldenSkinCut),
    };

    /// <summary>Time of Need: Healing Dome and Purge cooldowns x0.5.</summary>
    public const double TimeOfNeedScale = 0.5;

    /// <summary>Falter: Taunt slows enemies to this speed factor (60% slower) instead of speeding them up.</summary>
    public const double FalterSpeed = 0.4;

    /// <summary>Enchant Weapon: all damage x1.33 while Black Flame is on. Netherflame: Black Flame damage x2.</summary>
    public const double EnchantWeaponDamage = 1.33, NetherflameDamage = 2;

    /// <summary>Afterburn (this mod's numbers; the text says "a chance", "a few seconds"): chance per Black Flame hit to weaken
    /// the enemy so it takes x1.2 damage for 4 s.</summary>
    public const double AfterburnChance = 0.25, AfterburnDamageTaken = 1.2, AfterburnSeconds = 4;

    /// <summary>Flat cooldown reduction in seconds a spell gets from perks.</summary>
    public static double CooldownReduction(PerkTree perks, string spellId) =>
        CooldownCuts.Where(c => c.Spell == spellId && perks.TimesBought(c.Perk) > 0).Sum(c => c.Seconds);

    /// <summary>Factor on a spell's cooldown from perks (after the flat cuts).</summary>
    public static double CooldownScale(PerkTree perks, string spellId) =>
        perks.TimesBought(PerkEffectIds.TimeOfNeed) > 0 && spellId is "spell.healing_dome" or "spell.purge" ? TimeOfNeedScale : 1;
}

/// <summary>A shield on the player that soaks damage until it is used up or fades.</summary>
public sealed class DodgeShield
{
    public double Amount { get; private set; }
    public double TimeLeft { get; private set; }

    public void Grant(double amount)
    {
        if (amount <= 0) return;
        Amount = amount;
        TimeLeft = PerkTuning.ShieldSeconds;
    }

    /// <summary>Soaks what it can; returns the damage left over.</summary>
    public double Absorb(double damage)
    {
        if (Amount <= 0 || damage <= 0) return damage;
        double soaked = Math.Min(Amount, damage);
        Amount -= soaked;
        if (Amount <= 0) TimeLeft = 0;
        return damage - soaked;
    }

    public void Tick(double dt)
    {
        if (TimeLeft <= 0) return;
        TimeLeft -= dt;
        if (TimeLeft <= 0) Amount = 0;
    }

    public void Reset()
    {
        Amount = 0;
        TimeLeft = 0;
    }
}

/// <summary>Adrenaline Rush: a low-health surge of movement and attack speed.</summary>
public sealed class AdrenalineRush
{
    public const string Source = "buff.adrenaline_rush";

    public double Active { get; private set; }
    public double CooldownLeft { get; private set; }

    public bool IsActive => Active > 0;

    /// <summary>Starts the surge when the perk is bought, ready, and health fell to the trigger line. True when it started.</summary>
    public bool TryTrigger(PerkTree perks, double healthAfterHit, double maxHealth)
    {
        if (perks.TimesBought(PerkEffectIds.AdrenalineRush) == 0 || CooldownLeft > 0 || maxHealth <= 0) return false;
        if (healthAfterHit > maxHealth * PerkTuning.RushBelowHealth) return false;
        Active = Seconds(perks);
        CooldownLeft = PerkTuning.RushCooldown;
        return true;
    }

    public static double Seconds(PerkTree perks) => perks.TimesBought(PerkEffectIds.AdrenalineRush2) > 0 ? PerkTuning.RushSeconds2 : PerkTuning.RushSeconds;

    public static double Bonus(PerkTree perks) => perks.TimesBought(PerkEffectIds.AdrenalineRush2) > 0 ? PerkTuning.RushBonus2 : PerkTuning.RushBonus;

    public void Tick(double dt)
    {
        CooldownLeft = Math.Max(0, CooldownLeft - dt);
        Active = Math.Max(0, Active - dt);
    }

    public List<StatModifier> Modifiers(PerkTree perks)
    {
        if (!IsActive) return new List<StatModifier>();
        double m = 1 + Bonus(perks);
        return new List<StatModifier>
        {
            new(StatIds.MovementSpeed, EffectOp.Mult, m),
            new(StatIds.AttackSpeed, EffectOp.Mult, m),
        };
    }

    public void Reset()
    {
        Active = 0;
        CooldownLeft = 0;
    }
}

/// <summary>
/// Utility perks: more of an item per day, or a higher carry limit. Items are named by the game's own item name (as in its item
/// list, e.g. "TimeBomb", "Pills"), compared case-insensitively.
/// </summary>
public enum UtilityKind
{
    /// <summary>Gain <see cref="UtilityEffect.Amount"/> per stack each in-game day (what exceeds the carry limit is lost).</summary>
    DailyGrant,
    /// <summary>Raise the carry limit by <see cref="UtilityEffect.Amount"/> per stack.</summary>
    Capacity,
}

public sealed record UtilityEffect(string PerkId, UtilityKind Kind, string ItemKeyword, int Amount);

public static class UtilityPerks
{
    public static readonly IReadOnlyList<UtilityEffect> All = new UtilityEffect[]
    {
        new("perk.alternative_cloth_sources", UtilityKind.DailyGrant, "Cloth", 10),
        new("perk.demolition", UtilityKind.DailyGrant, "TimeBomb", 2),
        new("perk.demolition_expert", UtilityKind.DailyGrant, "C4Brick", 5),
        new("perk.meds", UtilityKind.DailyGrant, "Pills", 3),
        new("perk.booze", UtilityKind.DailyGrant, "VodkaBottle", 5),

        new("perk.pockets_for_explosives", UtilityKind.Capacity, "TimeBomb", 15),
        new("perk.pockets_for_explosives", UtilityKind.Capacity, "C4Brick", 15),
        new("perk.more_booze", UtilityKind.Capacity, "VodkaBottle", 15),
        new("perk.more_meds", UtilityKind.Capacity, "Pills", 20),
        new("perk.more_meat", UtilityKind.Capacity, "Meat", 5),
        new("perk.more_meat", UtilityKind.Capacity, "Fish", 5),
        new("perk.more_snacks", UtilityKind.Capacity, "EnergyBar", 20),
        new("perk.more_snacks", UtilityKind.Capacity, "EnergyDrink", 20),
        new("perk.more_bolts", UtilityKind.Capacity, "CrossbowAmmoBolt", 20),
        new("perk.corpse_collecting", UtilityKind.Capacity, "Bone", 100),
        new("perk.corpse_collecting", UtilityKind.Capacity, "Skull", 20),
        new("perk.more_limbs", UtilityKind.Capacity, "SeveredArm", 10),
        new("perk.more_limbs", UtilityKind.Capacity, "SeveredLeg", 10),
        new("perk.more_limbs", UtilityKind.Capacity, "SeveredHead", 10),
        new("perk.more_building_resources", UtilityKind.Capacity, "Stick", 6),
        new("perk.more_building_resources", UtilityKind.Capacity, "Rock", 4),
        new("perk.more_building_resources", UtilityKind.Capacity, "Rope", 2),
        new("perk.more_miscellaneous_items", UtilityKind.Capacity, "CookingPot", 5),
        new("perk.more_miscellaneous_items", UtilityKind.Capacity, "TurtleShell", 5),
        new("perk.more_miscellaneous_items", UtilityKind.Capacity, "Wristwatch", 5),
        new("perk.more_miscellaneous_items", UtilityKind.Capacity, "CircuitBoard", 5),
        new("perk.more_miscellaneous_items", UtilityKind.Capacity, "AirCanister", 5),
        new("perk.more_miscellaneous_items", UtilityKind.Capacity, "Flare", 5),
        new("perk.more_ammo", UtilityKind.Capacity, "CraftedSpear", 5),
        new("perk.more_ammo", UtilityKind.Capacity, "Molotov", 5),
        new("perk.more_ammo", UtilityKind.Capacity, "SmallRock", 50),
    };

    /// <summary>The total change per item keyword for a kind, summed over every bought stack.</summary>
    public static Dictionary<string, int> Totals(PerkTree perks, UtilityKind kind)
    {
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in All)
        {
            if (e.Kind != kind) continue;
            int times = perks.TimesBought(e.PerkId);
            if (times > 0) totals[e.ItemKeyword] = totals.GetValueOrDefault(e.ItemKeyword) + e.Amount * times;
        }
        return totals;
    }
}
