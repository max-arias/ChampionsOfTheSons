using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Near Death Experience perk (COS-105, COTF numbers by Max's choice): a lethal hit instead restores full health and gives
/// 10 s of 90% damage reduction, stun immunity, +35 health and faster stamina per second; then a 300 s lockout. Separate from
/// Undying Promise (the item), which has its own cooldown.
/// </summary>
public sealed class NearDeathState
{
    public const string PerkFlag = "perk_near_death_experience_unlocked";
    public const double Cooldown = 300, Seconds = 10, DamageTaken = 0.1, HealthPerSecond = 35, StaminaPerSecond = 10;

    public double CooldownLeft { get; private set; }

    public bool Ready => CooldownLeft <= 0;

    public void Tick(double dt) => CooldownLeft = Math.Max(0, CooldownLeft - dt);

    /// <summary>True (and starts the lockout) when the perk is on, the lockout is over and <paramref name="damage"/> would kill.</summary>
    public bool TryTrigger(bool perkOn, double health, double damage)
    {
        if (CooldownLeft > 0 || damage < health || !perkOn) return false;
        CooldownLeft = Cooldown;
        return true;
    }

    public void Reset() => CooldownLeft = 0;

    /// <summary>The 10-second state after it triggers.</summary>
    public static StatModifier[] Buff() => new[]
    {
        new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, DamageTaken),
        new StatModifier(CombatStatIds.StunImmunity, EffectOp.Add, 1),
        new StatModifier(StatIds.HealthRecoveryPerSecond, EffectOp.Add, HealthPerSecond),
        new StatModifier(CombatStatIds.StaminaPerSecond, EffectOp.Add, StaminaPerSecond),
    };
}
