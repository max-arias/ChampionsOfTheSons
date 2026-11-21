using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Numbers and rules of the 26 spells besides Blink. Most numbers are player stats named <c>spell_*</c> (the original values,
/// so perks can change them); the ones listed here are the original's fixed numbers (COS-83) or this mod's own, documented in docs/mechanics.md.
/// </summary>
public static class SpellTuning
{
    // Fixed numbers (the original's unless docs/mechanics.md says own) ----------------------------------------------
    public const double WarCryDuration = 120, WarCryMaxSpeed = 1.75;
    public const double GoldenSkinDuration = 40, GoldenSkinAttackSpeed = 1.2, GoldenSkinDebuffResistance = 0.75;
    public const double FocusDuration = 4;
    public const double FrenzyDuration = 20;
    public const double TauntDuration = 8, TauntArmor = 100;
    public const double PortalMaxRange = 40, PortalReenterLock = 1.5, PortalRadius = 1.5;
    public const double FireboltDamage = 30, FireboltBurnSeconds = 4, FireboltBurnFraction = 0.25, ProjectileSpeed = 40;
    public const double MagicArrowDamage = 55, MagicArrowDamageMult = 2, MagicArrowRangedShare = 0.5, MagicArrowRange = 70, MagicArrowSpeed = 70;
    public const double MagicArrowSlow = 0.6, MagicArrowDoubleSlow = 0.2, MagicArrowDamageTaken = 1.5;
    public const double SeekingArrowDamage = 30, SeekingArrowScaling = 1, SeekingArrowSpeed = 30, SeekingArrowRange = 40;
    public const double BlackFlameRadius = 6, BlackFlameDamage = 8, BlackFlameTick = 1;
    public const double BlackHoleRange = 35, BlackHoleTick = 0.5, BlackHoleCatchSeconds = 2.4, BlackHoleKeepAway = 2;
    public const double HealingDomeRadius = 14, HealingDomeEnergyFraction = 0.05, HealingDomeRegenSquare = 10, HealingDomeBase = 43.5, HealingDomeFlatShare = 0.1;
    public const double SunFlareTick = 0.5, SunFlareWarmup = 2.5, SunFlareHealBase = 11, SunFlareHealFlatDivisor = 20, SunFlareResistance = 0.5;
    public const double SnapFreezeDamage = 23, SnapFreezeStop = 0.65;
    public const double ShieldFlatDivisor = 35, ShieldDecaySeconds = 5;
    public const double BlinkFallSeconds = 1;
    public const double BerserkDamageTaken = 2, BerserkLeakSeconds = 15, BerserkLeakPerSecond = 15;
    public const double BallLightningSpeed = 12, BallLightningMaxSeconds = 30, BallLightningFuse = 4.55, BallLightningRadius = 28, BallLightningTouch = 1.5,
        BallLightningBurnFraction = 0.25, BallLightningBurnSeconds = 4;
    public const double BashRange = 4.5, BashAngle = 80, BashDamage = 30, BashScaling = 1, BashKnockback = 3;
    public const double CataclysmTick = 1.0 / 3, CataclysmWarmup = 2, CataclysmCasterSlow = 0.5, CataclysmCasterSlowSeconds = 2;
    public const double CataclysmFireDamage = 0.5, CataclysmFireSlow = 0.6, CataclysmFireBurnFraction = 0.25, CataclysmFireBurnSeconds = 4;
    public const double CataclysmArcaneDamage = 2, CataclysmArcaneStop = 2, CataclysmArcaneDamageTaken = 1.65;
    public const double SnowStormRadius = 10, SnowStormSeconds = 8, SnowStormDamage = 8, SnowStormSlow = 0.6;
    public const double RoarKnockbackScale = 1.5;
    public const double RoarWindup = 7.2, RoarWindupSlow = 0.3, RoarAirRadiusShare = 0.5, RoarAirCooldownShare = 1.0 / 3, RoarDotShare = 0.1, RoarDotSeconds = 10;
    public const double RoarLaunchForward = 20, RoarLaunchUp = 14, RoarGravity = 25;
    public const double ParryWindow = 1.5;
    public const double MultishotInterval = 0.75, MultishotRange = 30, MultishotDamage = 15, MultishotScaling = 1;
    public const double PurgeImmunitySeconds = 3, PurgeHealMissingHealth = 0.4, PurgeHealMissingEnergy = 0.3;
    public const double BloodArrowRange = 70, BloodArrowSpeed = 60;

    // Buff modifiers -------------------------------------------------------------------------------------------------

    /// <summary>War Cry's speed factor (attack and movement): <c>spell_war_cry_atk_speed</c> times the spell multiplier, at most x1.75.</summary>
    public static double WarCrySpeed(StatSheet s) =>
        Math.Min(WarCryMaxSpeed, Math.Max(1, s.Get("spell_war_cry_atk_speed")) * Math.Max(1, Derived.SpellMultiplier(s)));

    /// <summary>War Cry: attack and movement speed; the damage bonus only with its perk or item (<c>spell_war_cry_give_damage</c>).</summary>
    public static StatModifier[] WarCry(StatSheet s)
    {
        double speed = WarCrySpeed(s);
        var mods = new List<StatModifier>
        {
            new(StatIds.AttackSpeed, EffectOp.Mult, speed),
            new(StatIds.MovementSpeed, EffectOp.Mult, speed),
        };
        if (s.IsOn(WarCryGiveDamage)) mods.Add(new StatModifier(StatIds.AllDamage, EffectOp.Mult, Math.Max(1, s.Get("spell_war_cry_damage"))));
        return mods.ToArray();
    }

    public static StatModifier[] Berserk(StatSheet s) => new[]
    {
        new StatModifier(StatIds.AllDamage, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_damage"))),
        new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_movement_speed"))),
        new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_attack_speed"))),
        new StatModifier(StatIds.MaxHealthMult, EffectOp.Mult, Math.Max(0.1, s.Get("spell_berserk_max_hp"))),
        new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, BerserkDamageTaken),
    };

    /// <summary>The energy-leak debuff after Berserk ends (<see cref="BerserkLeakSeconds"/> s): energy drains every second.</summary>
    public static StatModifier[] BerserkLeak() => new[] { new StatModifier(CombatStatIds.EnergyPerSecond, EffectOp.Add, -BerserkLeakPerSecond) };

    /// <summary>Golden Skin: stun immunity, debuff resistance and attack speed x1.2; damage taken x0.5 only with Golden Resolve (perk or the gold set).</summary>
    public static StatModifier[] GoldenSkin(StatSheet s)
    {
        var mods = new List<StatModifier>
        {
            new(CombatStatIds.StunImmunity, EffectOp.Add, 1),
            new(DebuffResistance, EffectOp.Add, GoldenSkinDebuffResistance),
            new(StatIds.AttackSpeed, EffectOp.Mult, GoldenSkinAttackSpeed),
        };
        if (s.IsOn(GoldenResolve)) mods.Add(new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, GoldenResolveDamageTaken));
        return mods.ToArray();
    }

    /// <summary>Purge: three seconds of stun and debuff immunity.</summary>
    public static StatModifier[] PurgeImmunity() => new[]
    {
        new StatModifier(CombatStatIds.StunImmunity, EffectOp.Add, 1),
        new StatModifier(DebuffImmunity, EffectOp.Add, 1),
    };

    /// <summary>Purge with its heal perk: 40% of the missing health and 30% of the missing energy.</summary>
    public static (double Health, double Energy) PurgeHeal(double health, double maxHealth, double energy, double maxEnergy) =>
        (Math.Max(0, maxHealth - health) * PurgeHealMissingHealth, Math.Max(0, maxEnergy - energy) * PurgeHealMissingEnergy);

    public const string GoldenResolve = "perk_golden_resolve", ParryAnything = "perk_parry_anything",
        MultishotCount = "perk_multishot_projectile_count", MultishotPenalty = "perk_multishot_damage_pennalty",
        SnowStormCharge = "spell_snowstorm_max_charge", DebuffResistance = "debuff_resistance", DebuffImmunity = "debuff_immunity",
        WarCryGiveDamage = "spell_war_cry_give_damage";
    public const double GoldenResolveDamageTaken = 0.5, SnowStormBaseCharge = 10, MaxDebuffResistance = 0.75;

    /// <summary>Multishot: arrows per volley (<c>perk_multishot_projectile_count</c>, 1 without perks or items).</summary>
    public static int MultishotShots(StatSheet s) => Math.Max(1, (int)Math.Round(s.Get(MultishotCount)));

    /// <summary>Multishot: the i-th arrow of a volley (0 = first) deals the penalty to the i-th power (×0.75, ×0.56, ...).</summary>
    public static double MultishotCopyDamage(StatSheet s, int i)
    {
        double penalty = s.Get(MultishotPenalty);
        return Math.Pow(penalty > 0 ? Math.Min(1, penalty) : 0.75, i);
    }

    /// <summary>Parry window; Parry Anything (perk or the first survivor's shield) doubles it.</summary>
    public static double ParryWindowFor(StatSheet s) => s.IsOn(ParryAnything) ? ParryWindow * 2 : ParryWindow;

    /// <summary>Snow Storm grows with max charge (10 by default): duration × charge/10, radius × √(charge/10).</summary>
    public static double SnowStormScale(StatSheet s) => Math.Max(SnowStormBaseCharge, s.Get(SnowStormCharge)) / SnowStormBaseCharge;

    /// <summary>Debuff resistance (a fraction, the gold set gives 0.25) shortens slows, stuns and poison on the player, at most by 75%; debuff immunity (Purge, Healing Dome perk) blocks them.</summary>
    public static double DebuffSeconds(StatSheet s, double seconds) =>
        s.Get(DebuffImmunity) > 0 ? 0 : seconds * (1 - Math.Clamp(s.Get(DebuffResistance), 0, MaxDebuffResistance));

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

    /// <summary>Health per second the Healing Dome gives a player standing in it: <c>(regen^2 x 10 + 43.5) x recovery + spell flat x 0.1</c>,
    /// where regen is the character's health per second.</summary>
    public static double HealingDomeHeal(StatSheet s)
    {
        double regen = Derived.HealthRegenPerSecond(s, 0);
        return (regen * regen * HealingDomeRegenSquare + HealingDomeBase) * s.Get(StatIds.AllRecoveryMult) + s.Get(StatIds.SpellFlatDmg) * HealingDomeFlatShare;
    }

    /// <summary>Energy per second the Healing Dome gives with Humming Ground (<c>spell_healing_dome_reg_energy</c>): a share of max energy.</summary>
    public static double HealingDomeEnergy(double maxEnergy, StatSheet s) =>
        s.IsOn("spell_healing_dome_reg_energy") ? maxEnergy * HealingDomeEnergyFraction * s.Get(StatIds.AllRecoveryMult) : 0;

    /// <summary>Boost for players inside the dome: all damage up by <c>spell_healing_dome_damage_increase</c> and damage taken down by
    /// <c>spell_healing_dome_damage_resistance</c> (Rally Ring), stun and debuff immunity with its perk; empty without them.</summary>
    public static StatModifier[] HealingDomeBoost(StatSheet s)
    {
        var mods = new List<StatModifier>();
        double up = s.Get("spell_healing_dome_damage_increase"), resist = s.Get("spell_healing_dome_damage_resistance");
        if (up > 0) mods.Add(new StatModifier(StatIds.AllDamage, EffectOp.Mult, 1 + up));
        if (resist > 0) mods.Add(new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, Math.Max(0, 1 - resist)));
        if (s.IsOn("spell_healing_dome_gives_immunity"))
        {
            mods.Add(new StatModifier(CombatStatIds.StunImmunity, EffectOp.Add, 1));
            mods.Add(new StatModifier(DebuffImmunity, EffectOp.Add, 1));
        }
        return mods.ToArray();
    }

    /// <summary>Sun Flare: health per second for a player inside: <c>11 + spell flat / 20 + health regen</c>.</summary>
    public static double SunFlareHeal(StatSheet s) =>
        SunFlareHealBase + s.Get(StatIds.SpellFlatDmg) / SunFlareHealFlatDivisor + Derived.HealthRegenPerSecond(s, 0);

    /// <summary>Sun Flare for players inside: movement x<c>spell_flare_boost</c> and half the damage taken.</summary>
    public static StatModifier[] SunFlareBoost(StatSheet s) => new[]
    {
        new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_flare_boost"))),
        new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, SunFlareResistance),
    };

    /// <summary>Black Hole: how far from its centre an enemy is caught, <paramref name="elapsed"/> seconds after the cast (grows over 2.4 s).</summary>
    public static double BlackHoleCatchRadius(double radius, double elapsed) => radius * Math.Clamp(elapsed / BlackHoleCatchSeconds, 0, 1);

    /// <summary>Snap Freeze: <c>23 + spell flat x 6.5</c> (the scaling is <c>spell_snap_damage_scaling</c>).</summary>
    public static OutgoingHit SnapFreezeHit(StatSheet s, Rng rng) => PlayerAttack.Spell(s, SnapFreezeDamage, s.Get("spell_snap_damage_scaling"), rng);

    /// <summary>Magic Arrow: <c>(55 + 4.2 x spell flat + ranged flat / 2) x 2</c> times the spell multiplier.</summary>
    public static OutgoingHit MagicArrowHit(StatSheet s, Rng rng) =>
        PlayerAttack.Spell(s, (MagicArrowDamage + s.Get(StatIds.RangedFlatDmg) * MagicArrowRangedShare) * MagicArrowDamageMult,
            s.Get("spell_magic_arrow_damage_scaling") * MagicArrowDamageMult, rng, canCrit: s.IsOn("spell_magic_arrow_crit"));

    /// <summary>Ball Lightning's explosion: <c>1220 + 75 x spell flat</c> (the stats), then a burn.</summary>
    public static OutgoingHit BallLightningBlast(StatSheet s, Rng rng) =>
        PlayerAttack.Spell(s, s.Get("spell_ball_lightning_damage"), s.Get("spell_ball_lightning_damage_scaling"), rng, canCrit: s.IsOn("spell_ball_lightning_crit"));

    /// <summary>Cataclysm damage factor: fire half, arcane (perk or item) double.</summary>
    public static double CataclysmDamageFactor(bool arcane) => arcane ? CataclysmArcaneDamage : CataclysmFireDamage;
}

/// <summary>Sustain Shield: while channeled it fills a shield (<c>3 + spell flat / 35</c> points per second, up to <c>15 + spell flat</c>,
/// both times the spell multiplier); damage drains it first; after release what is left lasts <c>spell_shield_persistance_lifetime</c> s, then fades over a few seconds.</summary>
public sealed class SpellShield
{
    private double _amount, _lifetimeLeft, _decayRate;

    public double Amount => _amount;
    public bool Channeling { get; set; }

    /// <summary>Points per second while channeled.</summary>
    public static double GainPerSecond(StatSheet s) =>
        (s.Get("spell_shield_per_second") + s.Get(StatIds.SpellFlatDmg) / SpellTuning.ShieldFlatDivisor) * Derived.SpellMultiplier(s);

    /// <summary>Most points the shield holds.</summary>
    public static double Cap(StatSheet s) => (s.Get("spell_shield_max") + s.Get(StatIds.SpellFlatDmg)) * Derived.SpellMultiplier(s);

    /// <summary>Fills while channeled, counts the lifetime down while not and then fades.</summary>
    public void Tick(double dt, StatSheet s)
    {
        if (Channeling)
        {
            _amount = Math.Min(Cap(s), _amount + GainPerSecond(s) * dt);
            _lifetimeLeft = s.Get("spell_shield_persistance_lifetime");
            _decayRate = 0;
        }
        else if (_amount > 0)
        {
            if (_lifetimeLeft > 0) _lifetimeLeft -= dt;
            if (_lifetimeLeft <= 0)
            {
                if (_decayRate <= 0) _decayRate = _amount / SpellTuning.ShieldDecaySeconds;
                _amount = Math.Max(0, _amount - _decayRate * dt);
            }
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
        _decayRate = 0;
        Channeling = false;
    }
}

/// <summary>Parry: casting opens a short window in which the next hit on the player is negated.</summary>
public sealed class ParryState
{
    private double _left;

    public bool Open => _left > 0;

    public void Cast(double window = SpellTuning.ParryWindow) => _left = window;

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
