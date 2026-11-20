using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Numbers and rules of the 26 spells besides Blink. Most numbers are player stats named <c>spell_*</c> (the original values,
/// so perks can change them); the ones listed here are this mod's own and documented in docs/mechanics.md.
/// </summary>
public static class SpellTuning
{
    // Own numbers (the original spell text is not available) ---------------------------------------------------------
    public const double WarCryDuration = 20;
    public const double GoldenSkinDuration = 20, GoldenSkinArmor = 250, GoldenSkinDamageTaken = 0.75;
    public const double FocusDuration = 4;
    public const double FrenzyDuration = 20;
    public const double TauntDuration = 8, TauntArmor = 100;
    public const double PortalMaxRange = 40, PortalReenterLock = 1.5, PortalRadius = 1.5;
    public const double FireboltDamage = 30, FireboltBurnSeconds = 4, FireboltBurnFraction = 0.25, ProjectileSpeed = 40;
    public const double MagicArrowDamage = 60, MagicArrowRange = 70, MagicArrowSpeed = 70;
    public const double SeekingArrowDamage = 30, SeekingArrowScaling = 1, SeekingArrowSpeed = 30, SeekingArrowRange = 40;
    public const double BlackFlameRadius = 6, BlackFlameDamage = 8, BlackFlameTick = 1;
    public const double BlackHoleTick = 1, BlackHolePullDivisor = 5;
    public const double HealingDomeRadius = 8, HealingDomeHealFraction = 0.05;
    public const double SunFlareTick = 1;
    public const double SnapFreezeDamage = 40;
    public const double BallLightningRadius = 7, BallLightningSeconds = 10, BallLightningSpeed = 6, BallLightningTick = 1, BallLightningShare = 0.1;
    public const double BashRange = 4.5, BashAngle = 80, BashDamage = 30, BashScaling = 1, BashKnockback = 3;
    public const double CataclysmTick = 0.5;
    public const double SnowStormRadius = 10, SnowStormSeconds = 8, SnowStormDamage = 8, SnowStormSlow = 0.6;
    public const double RoarKnockbackScale = 1.5;
    public const double ParryWindow = 1.5;
    public const double MultishotInterval = 0.75, MultishotRange = 30, MultishotDamage = 15, MultishotScaling = 1;
    public const double PurgeHealFraction = 0.2;
    public const double BloodArrowRange = 70, BloodArrowSpeed = 60;

    // Buff modifiers -------------------------------------------------------------------------------------------------

    public static StatModifier[] WarCry(StatSheet s) => new[]
    {
        new StatModifier(StatIds.AllDamage, EffectOp.Mult, Math.Max(1, s.Get("spell_war_cry_damage"))),
        new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_war_cry_atk_speed"))),
    };

    public static StatModifier[] Berserk(StatSheet s) => new[]
    {
        new StatModifier(StatIds.AllDamage, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_damage"))),
        new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_movement_speed"))),
        new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_attack_speed"))),
        new StatModifier(StatIds.MaxHealthMult, EffectOp.Mult, Math.Max(0.1, s.Get("spell_berserk_max_hp"))),
    };

    public static StatModifier[] GoldenSkin() => new[]
    {
        new StatModifier(StatIds.Armor, EffectOp.Add, GoldenSkinArmor),
        new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, GoldenSkinDamageTaken),
    };

    public static StatModifier[] Focus(StatSheet s) => new[]
    {
        new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_focus_on_atk_speed"))),
        new StatModifier(StatIds.CritChance, EffectOp.Add, s.Get("spell_focus_on_bs")),
        new StatModifier(StatIds.HeadShotDamage, EffectOp.Mult, 1 + s.Get("spell_focus_on_hs")),
    };

    /// <summary>Frenzy with <paramref name="stacks"/> stacks: +2% attack speed and +7.5% damage each (stats); while active, the
    /// crit chance items add (<c>spell_frenzy_active_crit_chance</c>, COS-88).</summary>
    public static StatModifier[] Frenzy(StatSheet s, int stacks)
    {
        var mods = new List<StatModifier>();
        if (stacks > 0)
        {
            mods.Add(new StatModifier(StatIds.AllDamage, EffectOp.Mult, 1 + stacks * s.Get("spell_frenzy_dmg")));
            mods.Add(new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, 1 + stacks * s.Get("spell_frenzy_atk_speed")));
        }
        double crit = s.Get(PerkStatIds.FrenzyCrit);
        if (crit > 0) mods.Add(new StatModifier(StatIds.CritChance, EffectOp.Add, crit));
        return mods.ToArray();
    }

    public static StatModifier[] Taunt() => new[] { new StatModifier(StatIds.Armor, EffectOp.Add, TauntArmor) };

    public static StatModifier[] Speed(double mult) => new[] { new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, mult) };

    // Damage ---------------------------------------------------------------------------------------------------------

    /// <summary>A spell hit: (base + spellFlat × scaling) × spell multiplier, magic damage.</summary>
    public static OutgoingHit Hit(StatSheet s, double baseDamage, double scaling, Rng rng, bool canCrit = false) =>
        PlayerAttack.Spell(s, baseDamage, scaling, rng, canCrit);

    /// <summary>Blood Infused Arrow: spends <c>spell_bia_health_taken</c> of the current health (never the last point); the arrow does
    /// health spent × <c>spell_bia_health_dm_mult</c> plus spell damage × <c>spell_bia_spell_dm_mult</c>.</summary>
    public static (double HealthCost, OutgoingHit Hit) BloodArrow(StatSheet s, double currentHealth, Rng rng)
    {
        double cost = Math.Clamp(currentHealth * s.Get("spell_bia_health_taken"), 0, Math.Max(0, currentHealth - 1));
        var hit = PlayerAttack.Spell(s, cost * s.Get("spell_bia_health_dm_mult") + s.Get("spell_bia_accumulated_damage"),
            s.Get("spell_bia_spell_dm_mult"), rng, canCrit: s.IsOn("spell_bia_crit"));
        return (cost, hit);
    }

    /// <summary>Health per second the Healing Dome gives to a player standing in it.</summary>
    public static double HealingDomeHeal(double maxHealth, StatSheet s) => maxHealth * HealingDomeHealFraction * s.Get(StatIds.AllRecoveryMult);

    /// <summary>Energy per second the Healing Dome gives with Humming Ground (<c>spell_healing_dome_reg_energy</c>): the heal's share of max energy.</summary>
    public static double HealingDomeEnergy(double maxEnergy, StatSheet s) =>
        s.IsOn("spell_healing_dome_reg_energy") ? maxEnergy * HealingDomeHealFraction * s.Get(StatIds.AllRecoveryMult) : 0;

    /// <summary>Ball Lightning: each zap does a tenth of (<c>spell_ball_lightning_damage</c> + flat × scaling).</summary>
    public static OutgoingHit BallLightningZap(StatSheet s, Rng rng) =>
        PlayerAttack.Spell(s, s.Get("spell_ball_lightning_damage") * BallLightningShare, s.Get("spell_ball_lightning_damage_scaling") * BallLightningShare, rng,
            canCrit: s.IsOn("spell_ball_lightning_crit"));
}

/// <summary>Sustain Shield: while channeled it fills a shield (<c>spell_shield_per_second</c> % of max health per second up to
/// <c>spell_shield_max</c> %); damage drains it first; after release what is left lasts <c>spell_shield_persistance_lifetime</c> s.</summary>
public sealed class SpellShield
{
    private double _amount, _lifetimeLeft;

    public double Amount => _amount;
    public bool Channeling { get; set; }

    /// <summary>Fills while channeled, counts the lifetime down while not.</summary>
    public void Tick(double dt, double maxHealth, StatSheet s)
    {
        double cap = maxHealth * s.Get("spell_shield_max") / 100;
        if (Channeling)
        {
            _amount = Math.Min(cap, _amount + maxHealth * s.Get("spell_shield_per_second") / 100 * dt);
            _lifetimeLeft = s.Get("spell_shield_persistance_lifetime");
        }
        else if (_amount > 0 && (_lifetimeLeft -= dt) <= 0)
        {
            _amount = 0;
        }
    }

    /// <summary>Takes the shield's share out of an incoming hit and returns what gets through.</summary>
    public double Absorb(double damage)
    {
        double taken = Math.Min(_amount, Math.Max(0, damage));
        _amount -= taken;
        return damage - taken;
    }

    public void Reset()
    {
        _amount = 0;
        _lifetimeLeft = 0;
        Channeling = false;
    }
}

/// <summary>Parry: casting opens a short window in which the next hit on the player is negated.</summary>
public sealed class ParryState
{
    private double _left;

    public bool Open => _left > 0;

    public void Cast() => _left = SpellTuning.ParryWindow;

    public void Tick(double dt) => _left = Math.Max(0, _left - dt);

    /// <summary>True (and the window closes) when an incoming hit is parried.</summary>
    public bool TryParry()
    {
        if (_left <= 0) return false;
        _left = 0;
        return true;
    }

    public void Reset() => _left = 0;
}

/// <summary>Frenzy: for its duration every hit adds a stack (to <c>spell_frenzy_max_stacks</c>); stacks drop when it ends.</summary>
public sealed class FrenzyState
{
    private double _left;

    public int Stacks { get; private set; }
    public bool Active => _left > 0;

    public void Cast() => _left = SpellTuning.FrenzyDuration;

    /// <summary>A hit lands; returns true when the stack count changed.</summary>
    public bool OnHit(StatSheet s)
    {
        if (!Active) return false;
        int max = (int)Math.Max(1, s.Get("spell_frenzy_max_stacks"));
        if (Stacks >= max) return false;
        Stacks++;
        return true;
    }

    /// <summary>Returns true when Frenzy just ended.</summary>
    public bool Tick(double dt)
    {
        if (_left <= 0) return false;
        _left -= dt;
        if (_left > 0) return false;
        Stacks = 0;
        return true;
    }

    public void Reset()
    {
        _left = 0;
        Stacks = 0;
    }
}
