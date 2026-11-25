namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Where a hit on the player came from (COS-92).</summary>
public enum PlayerDamageSource
{
    /// <summary>An enemy's attack: the only kind that can be dodged or blocked, and the only one armor reduces.</summary>
    Enemy,
    /// <summary>Something else of the world the game sends through its hit path (cold, unknown sources).</summary>
    Environment,
    Fire,
    Poison,
    Trap,
    Fall,
    Drowning,
    Explosion,
    Thirst,
}

public enum TrapKind
{
    /// <summary>Any trap without its own number.</summary>
    Generic,
    /// <summary>Spike and deadfall traps.</summary>
    Heavy,
    /// <summary>The swinging rock (mace) trap.</summary>
    Swinging,
}

public enum HealKind
{
    Other,
    Meds,
    Herb,
}

/// <summary>
/// COTF's changes to the game's own rules (COS-92, Max 2026-11-21: all of them), so scaled enemies and scaled players stay
/// consistent: traps and explosions cannot one-shot a scaled enemy, environmental damage, falls and healing scale with max
/// health, hunger and thirst drain slower, worn armor soaks less, and only an enemy's attack can be dodged or blocked.
/// </summary>
public static class GameRules
{
    // ---------------------------------------------------------------- enemies

    /// <summary>A trap's hit on an enemy, instead of killing it.</summary>
    public static double TrapDamage(TrapKind kind) => kind switch
    {
        TrapKind.Swinging => 2000,
        TrapKind.Heavy => 1000,
        _ => 400,
    };

    /// <summary>A trap's hit on a player.</summary>
    public const double TrapPlayerDamage = 150;

    /// <summary>An explosion's hit on an enemy: the game's amount (it falls off with distance) held to COTF's 40..850, times
    /// (1 + explosion damage).</summary>
    public static double ExplosionToEnemy(double gameAmount, double explosionDamage) =>
        Math.Clamp(gameAmount, 40, 850) * (1 + Math.Max(0, explosionDamage));

    /// <summary>An explosion's hit on a player: a fixed amount by difficulty tier (COTF: 85 on Easy up to 40000 on Hell; the
    /// tiers between are ours, geometric), and only with friendly fire on.</summary>
    public static double ExplosionToPlayer(int difficultyTier, bool friendlyFire, int maxTier = 10)
    {
        if (!friendlyFire) return 0;
        double t = Math.Clamp(difficultyTier, 0, maxTier) / (double)Math.Max(1, maxTier);
        return Math.Round(85 * Math.Pow(40000 / 85.0, t));
    }

    /// <summary>A death without a player's hit still pays XP and loot, except drowning.</summary>
    public static bool DeathPays(bool killedByPlayer, bool drowned) => killedByPlayer || !drowned;

    // ---------------------------------------------------------------- players

    /// <summary>Only an enemy's attack can be dodged or blocked; everything else skips dodge, armor and block.</summary>
    public static bool Avoidable(PlayerDamageSource source) => source == PlayerDamageSource.Enemy;

    /// <summary>
    /// A non-enemy hit on the player after COTF's rule, from the game's own amount: drowning 3 + 10%, poison 2% + 2, burning
    /// 1% + 3 (times the fire damage taken stat), traps 150, thirst 20% of max health; others keep the game's amount.
    /// </summary>
    public static double PlayerHit(PlayerDamageSource source, double gameAmount, double maxHealth, double fireTaken = 1) => source switch
    {
        PlayerDamageSource.Drowning => 3 + 0.10 * maxHealth,
        PlayerDamageSource.Poison => 2 + 0.02 * maxHealth,
        PlayerDamageSource.Fire => (3 + 0.01 * maxHealth) * Math.Max(0, fireTaken),
        PlayerDamageSource.Trap => TrapPlayerDamage,
        PlayerDamageSource.Thirst => 0.20 * maxHealth,
        _ => gameAmount,
    };

    /// <summary>Cold: 1.5% of max health per tick.</summary>
    public static double FrostDamage(double maxHealth) => 0.015 * maxHealth;

    /// <summary>The game's player health before the mod (cold's vitality loss is measured against it).</summary>
    public const double VanillaMaxHealth = 100;

    /// <summary>
    /// Cold (COS-216): Sons of the Forest's cold takes away max vitality rather than dealing damage. The game's loss is sized for
    /// its own 100 health, so it scales with the player's max health to bite the same share (COTF's environment damage scales with
    /// max health too).
    /// </summary>
    public static double ColdVitalityLoss(double gameLoss, double maxHealth) =>
        Math.Max(0, gameLoss) * Math.Max(1, maxHealth / VanillaMaxHealth);

    /// <summary>Bad water: 1% of max health per unit drunk.</summary>
    public static double BadWaterDamage(double maxHealth, double units) => 0.01 * maxHealth * Math.Max(0, units);

    /// <summary>Landing faster than this (m/s) hurts; much faster kills.</summary>
    public const double FallSpeedThreshold = 12, FallLethalSpeed = 40;

    /// <summary>Fall damage: the game's amount plus 0.8% of max health per m/s over the threshold; a very long fall kills.</summary>
    public static double FallDamage(double landingSpeed, double gameAmount, double maxHealth)
    {
        double over = Math.Abs(landingSpeed) - FallSpeedThreshold;
        if (over <= 0) return gameAmount;
        if (Math.Abs(landingSpeed) >= FallLethalSpeed) return maxHealth * 10;
        return gameAmount + 0.008 * maxHealth * over;
    }

    /// <summary>A heal: meds 60% of max health, the herb 6%, anything else its amount plus 0.2% of max health per point.</summary>
    public static double Heal(HealKind kind, double amount, double maxHealth) => kind switch
    {
        HealKind.Meds => 0.60 * maxHealth,
        HealKind.Herb => 0.06 * maxHealth,
        _ => amount + 0.002 * maxHealth * Math.Max(0, amount),
    };

    /// <summary>Hunger and thirst drain at 40% of the game's rate, times the perk rate (perks lower it further).</summary>
    public static double NeedsDrainFactor(double perkRate) => 0.4 * Math.Max(0, perkRate);

    /// <summary>Worn armor soaks only 70% of what it would.</summary>
    public static double WornArmorRemaining(double initial, double afterArmor) => initial - 0.7 * Math.Max(0, initial - afterArmor);
}
