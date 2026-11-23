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
    public const double TauntDuration = 8, TauntArmor = 100, TauntRadius = 30;
    public const double PortalMaxRange = 40, PortalReenterLock = 1.5, PortalRadius = 1.5;
    public const double FireboltDamage = 30, FireboltBurnSeconds = 4, FireboltBurnFraction = 0.25, ProjectileSpeed = 40;
    public const double MagicArrowDamage = 60, MagicArrowRange = 70, MagicArrowSpeed = 70;
    public const double SeekingArrowDamage = 30, SeekingArrowScaling = 1, SeekingArrowSpeed = 30, SeekingArrowRange = 40;
    public const double BlackFlameRadius = 6, BlackFlameDamage = 8, BlackFlameTick = 1;
    public const double BlackHoleTick = 1, BlackHolePullDivisor = 5, BlackHoleCatchSeconds = 2;
    public const double HealingDomeRadius = 8, HealingDomeHealFraction = 0.05, HealingDomeBuffSeconds = 1;
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

    // Weapon-linked spells (COS-84): this mod's own numbers where the stand-ins had none --------------------------------
    /// <summary>Taunt is aimed: how far the point may be.</summary>
    public const double TauntAimRange = 60;
    /// <summary>Portal: a first gate waits this long for the second cast.</summary>
    public const double PortalPendingSeconds = 120;
    /// <summary>Multishot: energy per shot is <c>base x extra^1.5</c>; the extra arrows fan out this many degrees apart.</summary>
    public const double MultishotShotEnergy = 5, MultishotShotExponent = 1.5, MultishotSpreadDegrees = 4;
    /// <summary>Black Flame: seconds a weapon hit's burn lasts, the chance an arrow ignites (melee always).</summary>
    public const double BlackFlameBurnSeconds = 5;
    /// <summary>Frenzy: seconds a stack lasts (every attack refreshes it); casting spends the stacks for energy, per stack and second, as a share of max energy.</summary>
    public const double FrenzyStackSeconds = 4, FrenzyBurstShare = 0.01, FrenzyMoveSpeedPerStack = 0.05;
    /// <summary>Repeat Offender: flat damage per earlier hit on the same target while Frenzy has stacks.</summary>
    public const double FuryMeleePerHit = 10, FuryRangedPerHit = 3;
    /// <summary>Focus: the stored bonus waits this long for the next shot.</summary>
    public const double FocusStoredSeconds = 20;
    /// <summary>Parry: stun immunity after a parry, seconds the stored counter-strike bonus waits for the next melee hit.</summary>
    public const double ParryStunImmunitySeconds = 10, ParryStoredSeconds = 20;
    /// <summary>Bash (passive): most hits that stack on one enemy, the extra damage per stack per point of <c>spell_bash_damage_buff</c>.</summary>
    public const double LuckyBashCritDamage = 1, LuckyBashSeconds = 2, BloodArrowStunSeconds = 5;
    /// <summary>Seeking Arrow: ranged damage factor while a mark is up; the armed mark and Blood Infused Arrow's stored damage wait this long.</summary>
    public const double SeekingRangedMult = 0.9, ArrowChargeSeconds = 30;
    /// <summary>Blood Infused Arrow with the triple perk: damage x3 and an energy leak.</summary>
    public const double BloodArrowTriple = 3, BloodArrowLeakPerSecond = 10, BloodArrowLeakSeconds = 10;

    // Buff modifiers -------------------------------------------------------------------------------------------------

    public static StatModifier[] WarCry(StatSheet s) => new[]
    {
        new StatModifier(StatIds.AllDamage, EffectOp.Mult, Math.Max(1, s.Get("spell_war_cry_damage"))),
        new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_war_cry_atk_speed"))),
    };

    /// <summary>Healing Dome perks for a player inside: all damage up by <c>spell_healing_dome_damage_increase</c>, damage taken down by
    /// <c>spell_healing_dome_damage_resistance</c>; empty without them. (The immunity perk is two timed buffs, see the spell.)</summary>
    public static StatModifier[] HealingDomeBoost(StatSheet s)
    {
        var mods = new List<StatModifier>();
        double up = s.Get("spell_healing_dome_damage_increase"), resist = s.Get("spell_healing_dome_damage_resistance");
        if (up > 0) mods.Add(new StatModifier(StatIds.AllDamage, EffectOp.Mult, 1 + up));
        if (resist > 0) mods.Add(new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, Math.Max(0, 1 - resist)));
        return mods.ToArray();
    }

    /// <summary>Black Hole: how far from its centre an enemy gets caught, <paramref name="elapsed"/> seconds after the cast (grows from 0 to the radius over <see cref="BlackHoleCatchSeconds"/>).</summary>
    public static double BlackHoleCatchRadius(double radius, double elapsed) => radius * Math.Clamp(elapsed / BlackHoleCatchSeconds, 0, 1);

    public static StatModifier[] Berserk(StatSheet s) => new[]
    {
        new StatModifier(StatIds.AllDamage, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_damage"))),
        new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_movement_speed"))),
        new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_berserk_attack_speed"))),
        new StatModifier(StatIds.MaxHealthMult, EffectOp.Mult, Math.Max(0.1, s.Get("spell_berserk_max_hp"))),
    };

    public static StatModifier[] GoldenSkin(StatSheet s)
    {
        var mods = new List<StatModifier>
        {
            new(StatIds.Armor, EffectOp.Add, GoldenSkinArmor),
            new(StatIds.AllDamageTaken, EffectOp.Mult, GoldenSkinDamageTaken),
        };
        // Golden Resolve (perk or the gold set, COS-105 wave: dead stats get a use): gold halves the damage taken on top.
        if (s.IsOn(GoldenResolve)) mods.Add(new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, GoldenResolveDamageTaken));
        return mods.ToArray();
    }

    public const string GoldenResolve = "perk_golden_resolve", ParryAnything = "perk_parry_anything",
        MultishotCount = "perk_multishot_projectile_count", MultishotPenalty = "perk_multishot_damage_pennalty",
        SnowStormCharge = "spell_snowstorm_max_charge", DebuffResistance = "debuff_resistance";
    public const double GoldenResolveDamageTaken = 0.5, SnowStormBaseCharge = 10, MaxDebuffResistance = 0.75;

    /// <summary>Multishot: arrows per volley (<c>perk_multishot_projectile_count</c>, 1 without perks or items).</summary>
    public static int MultishotShots(StatSheet s) => Math.Max(1, (int)Math.Round(s.Get(MultishotCount)));

    /// <summary>Multishot: the i-th arrow of a volley (0 = first) deals the penalty to the i-th power (×0.75, ×0.56, ...).</summary>
    public static double MultishotCopyDamage(StatSheet s, int i)
    {
        double penalty = s.Get(MultishotPenalty);
        return Math.Pow(penalty > 0 ? Math.Min(1, penalty) : 0.75, i);
    }

    /// <summary>Multishot: extra arrows per shot, at least one: the count stat's volley minus the first arrow, plus the bracers' four.</summary>
    public static int MultishotExtras(StatSheet s) =>
        Math.Max(1, MultishotShots(s) - 1) + (s.IsOn(UniqueFlagIds.Sora) ? UniqueTuning.SoraExtraProjectiles : 0);

    /// <summary>Multishot: energy one shot with <paramref name="extras"/> extra arrows costs (the bracers make it cheaper).</summary>
    public static double MultishotShotCost(StatSheet s, int extras) =>
        MultishotShotEnergy * Math.Pow(Math.Max(0, extras), MultishotShotExponent) * (s.IsOn(UniqueFlagIds.Sora) ? UniqueTuning.SoraCostScale : 1);

    /// <summary>Black Flame: burn per second a weapon hit sets: <c>(8 x Netherflame + flat x scaling) x spell multiplier</c>.</summary>
    public static double BlackFlameBurn(StatSheet s, bool nether) =>
        (BlackFlameDamage * (nether ? PerkTuning.NetherflameDamage : 1) + s.Get(StatIds.SpellFlatDmg) * s.Get("spell_black_flame_damage_scaling"))
        * Derived.SpellMultiplier(s);

    /// <summary>Black Flame: chance a weapon hit ignites (melee always, arrows half the time).</summary>
    /// <summary>Black Flame's damage per tick to each enemy in its radius, before spell damage scaling (COS-171).</summary>
    public static double BlackFlameTickDamage(bool nether) => BlackFlameDamage * (nether ? PerkTuning.NetherflameDamage : 1);

    /// <summary>Focus: the bonus a shot stores for the next one, by where it hit (headshot or body).</summary>
    public static double FocusBonus(StatSheet s, bool head) =>
        (head ? s.Get("spell_focus_on_hs") : s.Get("spell_focus_on_bs")) + s.Get("spell_focus_bonus_dmg");

    /// <summary>Frenzy: energy per second spending <paramref name="stacks"/> stacks gives (a share of max energy each).</summary>
    public static double FrenzyBurst(int stacks, double maxEnergy) => stacks * FrenzyBurstShare * maxEnergy;

    /// <summary>Repeat Offender: flat damage after <paramref name="repeats"/> earlier hits on the same target.</summary>
    public static double FuryDamage(StatSheet s, bool melee, int repeats) =>
        s.IsOn("spell_fury_swipes") ? repeats * (melee ? FuryMeleePerHit : FuryRangedPerHit) : 0;

    /// <summary>Parry: the multiplier the next melee hit gets (the <c>spell_parry_dmg_bonus</c> total, 1 without it).</summary>
    public static double ParryStoredMult(StatSheet s) => Math.Max(1, s.Get("spell_parry_dmg_bonus"));

    /// <summary>Bash (passive): the melee hit's factor with <paramref name="stacks"/> earlier Bash hits on the enemy.</summary>
    /// <summary>Lucky Bashes: after a Bash that hit, crit damage +100% per perk stack for <see cref="LuckyBashSeconds"/> s. Null without the perk.</summary>
    public static StatModifier[]? LuckyBash(StatSheet s)
    {
        double stacks = s.Get("spell_bash_damage_buff");
        return stacks <= 0 ? null : new[] { new StatModifier(StatIds.CritDamage, EffectOp.Add, LuckyBashCritDamage * stacks) };
    }

    /// <summary>Seeking Arrow's bolt: Head Hunting (perk, <c>spell_seeking_arrow_head_damage</c>) adds its share to every hit.</summary>
    public static OutgoingHit SeekingArrowHit(OutgoingHit hit, StatSheet s) =>
        hit with { Amount = hit.Amount * (1 + s.Get("spell_seeking_arrow_damage_bonus")) * (1 + Math.Max(0, s.Get("spell_seeking_arrow_head_damage"))) };

    /// <summary>Seeking Arrow: the factor on a headshot while a mark is up (<c>spell_seeking_arrow_head_damage</c>, at most 1).</summary>
    public static double SeekingHeadMult(StatSheet s) => Math.Clamp(s.Get("spell_seeking_arrow_head_damage"), 0, 1);

    /// <summary>Blood Infused Arrow: the flat damage stored for the arrow (tripled with its perk).</summary>
    public static double BloodArrowStored(StatSheet s, OutgoingHit hit) => hit.Amount * (s.IsOn("spell_bia_triple_dmg") ? BloodArrowTriple : 1);

    /// <summary>Parry window; Parry Anything (perk or the first survivor's shield) doubles it.</summary>
    public static double ParryWindowFor(StatSheet s) => s.IsOn(ParryAnything) ? ParryWindow * 2 : ParryWindow;

    /// <summary>Snow Storm grows with max charge (10 by default): duration × charge/10, radius × √(charge/10).</summary>
    public static double SnowStormScale(StatSheet s) => Math.Max(SnowStormBaseCharge, s.Get(SnowStormCharge)) / SnowStormBaseCharge;

    /// <summary>Debuff resistance (a fraction, the gold set gives 0.25) shortens slows, stuns and poison on the player, at most by 75%.</summary>
    public static double DebuffSeconds(StatSheet s, double seconds) => seconds * (1 - Math.Clamp(s.Get(DebuffResistance), 0, MaxDebuffResistance));

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
            // Red Tempo's upgrade (spell_frenzy_ms): movement speed per stack too.
            if (s.IsOn("spell_frenzy_ms")) mods.Add(new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, 1 + stacks * FrenzyMoveSpeedPerStack));
        }
        double crit = s.Get(PerkStatIds.FrenzyCrit);
        if (crit > 0) mods.Add(new StatModifier(StatIds.CritChance, EffectOp.Add, crit));
        return mods.ToArray();
    }

    public static StatModifier[] Taunt() => new[] { new StatModifier(StatIds.Armor, EffectOp.Add, TauntArmor) };

    public static StatModifier[] Speed(double mult) => new[] { new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, mult) };

    // Damage ---------------------------------------------------------------------------------------------------------

    /// <summary>A spell hit: (base + spellFlat × scaling) × spell multiplier, magic damage.</summary>
    /// <summary>COS-183 (Max: spells were barely denting enemies of the player's level): every spell's base damage is multiplied by how
    /// much tougher a normal enemy of the caster's level is than one of level 1, so a spell keeps its level-1 share of a same-level
    /// enemy's health at every level. Spell-damage gear and Intelligence add on top.</summary>
    public static double SpellLevelScale(int level) => EnemyScaler.HealthCurve.At(Math.Max(1, level)) / EnemyScaler.HealthCurve.At(1);

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

/// <summary>Frenzy: while slotted every attack adds a stack (to <c>spell_frenzy_max_stacks</c>) and refreshes them; they drop
/// <see cref="SpellTuning.FrenzyStackSeconds"/> after the last attack. Casting spends them (<see cref="Spend"/>).</summary>
public sealed class FrenzyState
{
    private double _left;

    public int Stacks { get; private set; }
    public bool Active => Stacks > 0;

    /// <summary>An attack lands; returns true when the stack count changed.</summary>
    public bool OnAttack(StatSheet s)
    {
        _left = SpellTuning.FrenzyStackSeconds;
        int max = (int)Math.Max(1, s.Get("spell_frenzy_max_stacks"));
        if (Stacks >= max) return false;
        Stacks++;
        return true;
    }

    /// <summary>Takes every stack (the cast): returns how many there were.</summary>
    public int Spend()
    {
        int n = Stacks;
        Stacks = 0;
        _left = 0;
        return n;
    }

    /// <summary>Returns true when the stacks just ran out.</summary>
    public bool Tick(double dt)
    {
        if (Stacks == 0) return false;
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

/// <summary>A bonus a spell stores for a later hit: Focus (the next shot), Parry (the next melee hit), Blood Infused Arrow (the next
/// arrows, one charge each), Seeking Arrow's armed mark. It runs out after its seconds.</summary>
public sealed class StoredBonus
{
    private double _left;

    public double Value { get; private set; }
    public int Charges { get; private set; }
    public bool Has => Charges > 0 && _left > 0;

    public void Store(double value, double seconds, int charges = 1)
    {
        Value = value;
        _left = seconds;
        Charges = charges;
    }

    /// <summary>Uses one charge; returns the stored value, or null when there is none.</summary>
    public double? Take()
    {
        if (!Has) return null;
        double v = Value;
        if (--Charges <= 0) Clear();
        return v;
    }

    public void Tick(double dt)
    {
        if (_left <= 0) return;
        _left -= dt;
        if (_left <= 0) Clear();
    }

    public void Clear()
    {
        Value = 0;
        Charges = 0;
        _left = 0;
    }
}

/// <summary>Repeat Offender: counts hits in a row on one target; a different target starts over.</summary>
public sealed class RepeatTracker
{
    private object? _target;
    private int _count;

    /// <summary>A hit on <paramref name="target"/>; returns how many hits in a row came before it.</summary>
    public int Hit(object target)
    {
        if (!ReferenceEquals(target, _target))
        {
            _target = target;
            _count = 0;
        }
        return _count++;
    }

    public void Reset()
    {
        _target = null;
        _count = 0;
    }
}
