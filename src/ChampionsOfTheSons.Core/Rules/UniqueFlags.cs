using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Unique-item flags (<c>i_*</c> stats) that need state over time. The wording on the items (content/text, `.unique`) is the
/// spec; where it leaves a number open, the value here is this mod's own (docs/mechanics.md, "Unique item flags"):
/// Death Pact, King Qruie's sword, Hexed Pants of Mr. M, Wind Armor and the Infinity Loop amulets.
/// </summary>
public static class UniqueFlagIds
{
    public const string DeathPact = "i_death_pact_enabled";
    public const string KingQruie = "i_king_qruies_sword";
    public const string HexedPants = "i_hexed_pants_of_mr_m_enabled";
    public const string WindArmor = "i_is_wind_armor";
    public const string InfinityLoop = "i_infinity_loop";
    public const string Eruption = "i_eruption_bow";
    public const string Crossfire = "i_crossfire_quiver";
    public const string Sora = "i_sora_bracers";
    public const string WitheredCrown = "i_hazard_crown";
    public const string Greed = "i_is_greed";
    public const string Spark = "i_spark_of_light_after_dark";
}

public static class UniqueTuning
{
    /// <summary>Death Pact: an attack costs this fraction of max health (never lethal) and damage rises per percent of health missing.</summary>
    public const double DeathPactCost = 0.07, DeathPactDamagePerMissingPercent = 0.05;

    /// <summary>Hexed Pants: damage and energy regeneration while moving; after <see cref="HexedStillAfter"/> s standing still, health drains per second (never lethal).</summary>
    public const double HexedMoving = 1.4, HexedStillAfter = 1, HexedDrainPerSecond = 0.01;

    /// <summary>Wind Armor: a dodge gives these for <see cref="WindSeconds"/> s, and heals.</summary>
    public const double WindSeconds = 5, WindMovement = 1.2, WindDamage = 1.35, WindArmorBonus = 2000, WindHeal = 0.05;

    /// <summary>Infinity Loop: seconds taken off one random spell cooldown per melee or ranged hit.</summary>
    public const double InfinityLoopSeconds = 1;

    /// <summary>Eruption: a headshot splashes this fraction of its damage on other enemies within the radius (m).</summary>
    public const double EruptionFraction = 0.5, EruptionRadius = 4;

    /// <summary>Crossfire: a projectile hit fires a free Magic Arrow at the target, at most once per this many seconds (x the cooldown stat).</summary>
    public const double CrossfireGap = 2;

    /// <summary>Grip of Sora: Multishot energy cost x0.8 and this many extra projectiles per volley.</summary>
    public const double SoraCostScale = 0.8;
    public const int SoraExtraProjectiles = 4;

    /// <summary>Withered Crown: extra Blood Infused Arrows per cast (one health cost).</summary>
    public const int CrownExtraArrows = 4;

    /// <summary>Greed: Wide Reach casts itself once per this many seconds.</summary>
    public const double GreedInterval = 1;

    /// <summary>Last Engineer's Note: a Black Hole that hits at least this many different enemies releases a Ball Lightning when it ends.</summary>
    public const int SparkEnemies = 5;

    /// <summary>Crossfire's gap after cooldown reduction.</summary>
    public static double CrossfireGapFor(StatSheet stats) => CrossfireGap * stats.Get(StatIds.Cooldown);
}

/// <summary>State and numbers of the time-based unique flags. The mod feeds it events; it hands back stat modifiers and amounts.</summary>
public sealed class UniqueRuntime
{
    public const string Source = "buff.unique_flags";

    public double LastPhysicalTaken { get; private set; }
    public double StillSeconds { get; private set; }
    public double WindLeft { get; private set; }
    public bool Moving { get; private set; } = true;

    /// <summary>A physical hit the player took (King Qruie's sword remembers its size).</summary>
    public void OnPhysicalDamageTaken(double damage)
    {
        if (damage > 0) LastPhysicalTaken = damage;
    }

    /// <summary>Called on a dodge; returns the fraction of max health to heal (Wind Armor) or 0.</summary>
    public double OnDodge(StatSheet s)
    {
        if (!s.IsOn(UniqueFlagIds.WindArmor)) return 0;
        WindLeft = UniqueTuning.WindSeconds;
        return UniqueTuning.WindHeal;
    }

    /// <summary>Advances timers. Returns the health fraction to drain this step (Hexed Pants standing still), 0 otherwise.</summary>
    public double Tick(double dt, bool moving, StatSheet s)
    {
        Moving = moving;
        WindLeft = Math.Max(0, WindLeft - dt);
        if (moving) { StillSeconds = 0; return 0; }
        StillSeconds += dt;
        return s.IsOn(UniqueFlagIds.HexedPants) && StillSeconds > UniqueTuning.HexedStillAfter ? UniqueTuning.HexedDrainPerSecond * dt : 0;
    }

    /// <summary>Stat changes the current state gives: Hexed Pants while moving, Wind Armor while active.</summary>
    public List<StatModifier> Modifiers(StatSheet s)
    {
        var mods = new List<StatModifier>();
        if (s.IsOn(UniqueFlagIds.HexedPants) && Moving)
        {
            mods.Add(new StatModifier(StatIds.AllDamage, EffectOp.Mult, UniqueTuning.HexedMoving));
            mods.Add(new StatModifier(StatIds.StaminaPerSecRate, EffectOp.Mult, UniqueTuning.HexedMoving));
        }
        if (WindLeft > 0)
        {
            mods.Add(new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, UniqueTuning.WindMovement));
            mods.Add(new StatModifier(StatIds.AllDamage, EffectOp.Mult, UniqueTuning.WindDamage));
            mods.Add(new StatModifier(StatIds.Armor, EffectOp.Add, UniqueTuning.WindArmorBonus));
        }
        return mods;
    }

    /// <summary>Death Pact: damage multiplier from the health missing (1 when the flag is off).</summary>
    public static double DeathPactMultiplier(StatSheet s, double health, double maxHealth)
    {
        if (!s.IsOn(UniqueFlagIds.DeathPact) || maxHealth <= 0) return 1;
        double missingPercent = Math.Clamp(1 - health / maxHealth, 0, 1) * 100;
        return 1 + UniqueTuning.DeathPactDamagePerMissingPercent * missingPercent;
    }

    /// <summary>Death Pact: health an attack costs now (0 when off); never takes the last point.</summary>
    public static double DeathPactCost(StatSheet s, double health, double maxHealth) =>
        s.IsOn(UniqueFlagIds.DeathPact) ? Math.Min(maxHealth * UniqueTuning.DeathPactCost, Math.Max(0, health - 1)) : 0;

    /// <summary>King Qruie's sword: flat damage added to a melee hit.</summary>
    public double KingQruieBonus(StatSheet s) => s.IsOn(UniqueFlagIds.KingQruie) ? LastPhysicalTaken : 0;

    public void Reset()
    {
        LastPhysicalTaken = 0;
        StillSeconds = 0;
        WindLeft = 0;
        Moving = true;
    }
}
