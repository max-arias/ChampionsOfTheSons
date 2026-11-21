namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Numbers for the passive enemy abilities (COTF's where known; the rest are this mod's own and listed in mechanics.md).</summary>
public static class AbilityTuning
{
    public const double ShieldSeconds = 3;
    public const double BasherStunSeconds = 1;
    public const double RainDamage = 5, RainArmor = 5, RainSpeed = 2;
    public const double UndeadReviveFraction = 0.5;
    public const double PoisonSeconds = 6;
    /// <summary>Slow from "chance to slow": speed ×0.5 for 8 s.</summary>
    public const double SlowAmount = 0.5, SlowSeconds = 8;
    /// <summary>Weaken: enemy takes ×1.2 damage for 8 s.</summary>
    public const double WeakenMult = 1.2, WeakenSeconds = 8;
    /// <summary>Bleed: hit damage / 20 per second for 10 s.</summary>
    public const double BleedDivisor = 20, BleedSeconds = 10;

    // Avenger / Sacrifice are new rules (COTF's text is not available): an ally dying within range enrages Avenger.
    public const double AvengerRadius = 30, AvengerDamagePerStack = 0.25, AvengerSpeedPerStack = 0.1, AvengerSeconds = 20;
    public const int AvengerMaxStacks = 5;
    /// <summary>Sacrifice: on death, allies in range heal this fraction of their max health and deal more damage for a while.</summary>
    public const double SacrificeRadius = 25, SacrificeHeal = 0.3, SacrificeDamage = 1.5, SacrificeSeconds = 15;
}

/// <summary>Stat IDs used by the bridge's combat effects (kept apart from <see cref="StatIds"/> so parallel work never conflicts).</summary>
public static class CombatStatIds
{
    public const string HammerStun = "i_hammer_stun";
    public const string HammerStunChance = "i_hammer_stun_amount";
    public const string HammerStunDuration = "i_hammer_stun_duration";
    public const string ThornsPerStrength = "thorns_per_strenght";
    public const string ThornsPerVit = "thorns_per_vit";
    public const string ThornsDmgMult = "thorns_dmg_mult";
    public const string StaminaPerSecond = "stamina_recoveryper_second";
    public const string EnergyPerSecond = "energy_recoveryper_second";
    public const string ProjectileSpeed = "projectile_speed";
    public const string ProjectileSize = "projectile_size";
    public const string ExplosionDamage = "explosion_damage";
    public const string StunImmunity = "stun_immunity";
    public const string WeaponRange = "weapon_range";
    public const string AttackStaminaCost = "attack_stamina_cost";
    public const string ThrownSpearDamage = "perk_thrown_spear_damage_mult";
}

public enum DotKind
{
    Bleed,
    Fire,
    Poison,
}

/// <summary>One damage-over-time tick due on an enemy.</summary>
public readonly record struct DotTick(string Source, DotKind Kind, double Damage);

/// <summary>
/// Everything an enemy's abilities and debuffs change over time, as a pure state machine the bridge drives.
/// Abilities and debuffs only advance while the enemy is alive and active (the original's bug #43: abilities
/// kept acting on dead or despawned enemies); <see cref="Tick"/> does nothing otherwise.
/// </summary>
/// <summary>What a player did to an enemy's combat state, as data: lets a co-op client forward its debuffs to the host (COS-13).</summary>
public enum EnemyEffectKind : byte { Slow = 1, Weaken = 2, Stun = 3, FireMark = 4, Dot = 5 }

public readonly record struct EnemyEffect(EnemyEffectKind Kind, string Source, double Amount, double Seconds, DotKind Dot = default);

public sealed class EnemyCombatState
{
    private sealed class Timed
    {
        public double Amount;
        public double Left;
    }

    private sealed class Dot
    {
        public DotKind Kind;
        public double PerSecond;
        public int TicksLeft;
        public double Phase;
    }

    private readonly Dictionary<string, Timed> _slows = new();
    private readonly Dictionary<string, Timed> _weakens = new();
    private readonly Dictionary<string, Dot> _dots = new();
    private double _stunLeft;
    private double _shieldLeft, _shieldCooldownLeft;
    private double _buffLeft, _buffMult = 1;
    private double _fireMark, _fireMarkLeft;
    private double _furyLeft;
    private int _furyStacks;

    public EnemyProfile Profile { get; }
    public DifficultyTier Difficulty { get; }

    /// <summary>Fed by the bridge from the weather system.</summary>
    public bool Raining { get; set; }
    public bool UndeadUsed { get; private set; }

    /// <summary>State for an enemy whose level band follows its level (<see cref="Difficulties.ForLevel"/>).</summary>
    public EnemyCombatState(EnemyProfile profile) : this(profile, Difficulties.ForLevel(profile.Level)) { }

    public EnemyCombatState(EnemyProfile profile, DifficultyTier difficulty)
    {
        Profile = profile;
        Difficulty = difficulty;
    }

    // ---------------------------------------------------------------- queries

    public bool CcImmune => Profile.Has(EnemyAbility.Juggernaut);
    public bool Stunned => _stunLeft > 0;
    public int FuryStacks => _furyStacks;
    public bool RainActive => Raining && Profile.Has(EnemyAbility.RainEmpowerment);

    /// <summary>Product of every slow source, the rain bonus and fury; 0 while stunned.</summary>
    public double SpeedMultiplier
    {
        get
        {
            if (Stunned) return 0;
            double m = 1;
            foreach (var s in _slows.Values) m *= s.Amount;
            if (RainActive) m *= AbilityTuning.RainSpeed;
            m *= 1 + _furyStacks * AbilityTuning.AvengerSpeedPerStack;
            return m;
        }
    }

    /// <summary>Product of every damage-taken debuff (weaken ×1.2...).</summary>
    public double DamageTakenMultiplier
    {
        get
        {
            double m = 1;
            foreach (var w in _weakens.Values) m *= w.Amount;
            return m;
        }
    }

    /// <summary>Rain, Avenger fury and Sacrifice buffs on the enemy's own damage.</summary>
    public double DamageDealtMultiplier
    {
        get
        {
            double m = RainActive ? AbilityTuning.RainDamage : 1;
            m *= 1 + _furyStacks * AbilityTuning.AvengerDamagePerStack;
            if (_buffLeft > 0) m *= _buffMult;
            return m;
        }
    }

    public double ArmorMultiplier => RainActive ? AbilityTuning.RainArmor : 1;

    public bool HasSlow => _slows.Count > 0;
    public bool HasWeaken => _weakens.Count > 0;
    public int DotCount => _dots.Count;

    // ---------------------------------------------------------------- applying effects

    /// <summary>Raised after any slow, weaken, stun, fire mark or damage-over-time took hold on any enemy.</summary>
    public static event Action<EnemyCombatState, EnemyEffect>? Applied;

    /// <summary>Applies an effect given as data (a co-op client's forwarded debuff).</summary>
    public void Apply(EnemyEffect e)
    {
        switch (e.Kind)
        {
            case EnemyEffectKind.Slow: ApplySlow(e.Source, e.Amount, e.Seconds); break;
            case EnemyEffectKind.Weaken: ApplyWeaken(e.Source, e.Amount, e.Seconds); break;
            case EnemyEffectKind.Stun: ApplyStun(e.Seconds); break;
            case EnemyEffectKind.FireMark: ApplyFireMark(e.Amount, e.Seconds); break;
            case EnemyEffectKind.Dot: ApplyDot(e.Source, e.Dot, e.Amount, e.Seconds); break;
        }
    }

    private void Raise(EnemyEffect e) => Applied?.Invoke(this, e);

    /// <summary>Per source: keeps the longest duration and the strongest (lowest) speed multiplier. Ignored by Juggernaut.</summary>
    public bool ApplySlow(string source, double speedMult, double seconds)
    {
        if (CcImmune || seconds <= 0) return false;
        if (_slows.TryGetValue(source, out var s))
        {
            s.Amount = Math.Min(s.Amount, speedMult);
            s.Left = Math.Max(s.Left, seconds);
        }
        else
        {
            _slows[source] = new Timed { Amount = speedMult, Left = seconds };
        }
        Raise(new EnemyEffect(EnemyEffectKind.Slow, source, speedMult, seconds));
        return true;
    }

    /// <summary>Per source: keeps the longest duration and the latest amount.</summary>
    public void ApplyWeaken(string source, double damageTakenMult, double seconds)
    {
        if (seconds <= 0) return;
        if (_weakens.TryGetValue(source, out var w))
        {
            w.Amount = damageTakenMult;
            w.Left = Math.Max(w.Left, seconds);
        }
        else
        {
            _weakens[source] = new Timed { Amount = damageTakenMult, Left = seconds };
        }
        Raise(new EnemyEffect(EnemyEffectKind.Weaken, source, damageTakenMult, seconds));
    }

    public bool ApplyStun(double seconds)
    {
        if (CcImmune || seconds <= 0) return false;
        _stunLeft = Math.Max(_stunLeft, seconds);
        Raise(new EnemyEffect(EnemyEffectKind.Stun, "", 0, seconds));
        return true;
    }

    /// <summary>Smolder Mark (COS-87): fire damage-over-time ticks on this enemy deal ×(1 + bonus) while the mark lasts. Re-marking refreshes it and keeps the larger bonus.</summary>
    public void ApplyFireMark(double bonus, double seconds)
    {
        if (bonus <= 0 || seconds <= 0) return;
        _fireMark = _fireMarkLeft > 0 ? Math.Max(_fireMark, bonus) : bonus;
        _fireMarkLeft = Math.Max(_fireMarkLeft, seconds);
        Raise(new EnemyEffect(EnemyEffectKind.FireMark, "", bonus, seconds));
    }

    /// <summary>Current Smolder Mark bonus on fire damage (0 when unmarked).</summary>
    public double FireMark => _fireMarkLeft > 0 ? _fireMark : 0;

    /// <summary>A damage-over-time: <paramref name="perSecond"/> every 1 s for ceil(duration) ticks. Re-applying a source refreshes it.</summary>
    public void ApplyDot(string source, DotKind kind, double perSecond, double seconds)
    {
        if (perSecond <= 0 || seconds <= 0) return;
        _dots[source] = new Dot { Kind = kind, PerSecond = perSecond, TicksLeft = (int)Math.Ceiling(seconds) };
        Raise(new EnemyEffect(EnemyEffectKind.Dot, source, perSecond, seconds, kind));
    }

    // ---------------------------------------------------------------- abilities

    /// <summary>Shielding: the first hit while ready raises the shield for 3 s, then it recharges for the difficulty's cooldown.
    /// Returns true when the hit is absorbed.</summary>
    public bool AbsorbsHit()
    {
        if (!Profile.Has(EnemyAbility.Shielding)) return false;
        if (_shieldLeft > 0) return true;
        if (_shieldCooldownLeft > 0) return false;
        _shieldLeft = AbilityTuning.ShieldSeconds;
        _shieldCooldownLeft = Difficulty.ShieldingCooldown;
        return true;
    }

    /// <summary>Undead: the first lethal hit leaves the enemy at half health instead.</summary>
    public bool TryUndeadRevive()
    {
        if (!Profile.Has(EnemyAbility.Undead) || UndeadUsed) return false;
        UndeadUsed = true;
        return true;
    }

    /// <summary>Another enemy died <paramref name="distance"/> m away: Avenger stacks up while it is close enough.</summary>
    public void OnAllyDied(double distance)
    {
        if (!Profile.Has(EnemyAbility.Avenger) || distance > AbilityTuning.AvengerRadius) return;
        _furyStacks = Math.Min(AbilityTuning.AvengerMaxStacks, _furyStacks + 1);
        _furyLeft = AbilityTuning.AvengerSeconds;
    }

    /// <summary>A Sacrifice enemy died <paramref name="distance"/> m away. Returns the fraction of max health this enemy heals (0 if out of range).</summary>
    public double OnSacrificeDied(double distance)
    {
        if (distance > AbilityTuning.SacrificeRadius) return 0;
        _buffMult = AbilityTuning.SacrificeDamage;
        _buffLeft = AbilityTuning.SacrificeSeconds;
        return AbilityTuning.SacrificeHeal;
    }

    // ---------------------------------------------------------------- time

    /// <summary>Advances every timer by <paramref name="dt"/> and returns the damage ticks that came due.
    /// Does nothing (and returns nothing) unless the enemy is alive and active.</summary>
    public IReadOnlyList<DotTick> Tick(double dt, bool aliveAndActive)
    {
        if (!aliveAndActive || dt <= 0) return Array.Empty<DotTick>();

        _stunLeft = Math.Max(0, _stunLeft - dt);
        _shieldLeft = Math.Max(0, _shieldLeft - dt);
        _shieldCooldownLeft = Math.Max(0, _shieldCooldownLeft - dt);
        _buffLeft = Math.Max(0, _buffLeft - dt);
        _fireMarkLeft = Math.Max(0, _fireMarkLeft - dt);
        if (_furyLeft > 0 && (_furyLeft -= dt) <= 0) _furyStacks = 0;
        Expire(_slows, dt);
        Expire(_weakens, dt);

        List<DotTick>? due = null;
        List<string>? finished = null;
        foreach (var (source, dot) in _dots)
        {
            dot.Phase += dt;
            while (dot.Phase >= 1 && dot.TicksLeft > 0)
            {
                dot.Phase -= 1;
                dot.TicksLeft--;
                double tick = dot.Kind == DotKind.Fire ? dot.PerSecond * (1 + FireMark) : dot.PerSecond;
                (due ??= new()).Add(new DotTick(source, dot.Kind, tick));
            }
            if (dot.TicksLeft <= 0) (finished ??= new()).Add(source);
        }
        if (finished != null) foreach (var s in finished) _dots.Remove(s);
        return due ?? (IReadOnlyList<DotTick>)Array.Empty<DotTick>();
    }

    private static void Expire(Dictionary<string, Timed> map, double dt)
    {
        List<string>? gone = null;
        foreach (var (k, v) in map)
        {
            v.Left -= dt;
            if (v.Left <= 0) (gone ??= new()).Add(k);
        }
        if (gone != null) foreach (var k in gone) map.Remove(k);
    }
}

/// <summary>What an enemy's ability does to the player when it hits.</summary>
public static class EnemyHitEffects
{
    /// <summary>Poisonous: damage per second = hit damage × √(damageMult/10)/10, for 6 s.</summary>
    public static double PoisonPerSecond(EnemyProfile enemy, double hitDamage) =>
        enemy.Has(EnemyAbility.Poisonous) ? hitDamage * Math.Sqrt(Math.Max(0, enemy.DamageMult) / 10) / 10 : 0;

    public static double StunSeconds(EnemyProfile enemy) => enemy.Has(EnemyAbility.Basher) ? AbilityTuning.BasherStunSeconds : 0;
}

/// <summary>What a player's hit procs from their stats (life/energy on hit, slow/bleed/weaken chances) plus thorns.</summary>
public static class PlayerHitEffects
{
    public readonly record struct Procs(double Life, double Energy, bool Slow, bool Bleed, double BleedPerSecond, bool Weaken, double StunSeconds);

    public static Procs Roll(StatSheet s, double damageDealt, Rng rng)
    {
        bool slow = rng.Chance(Math.Clamp(s.Get(StatIds.ChanceToSlow), 0, 1));
        bool bleed = rng.Chance(Math.Clamp(s.Get(StatIds.ChanceToBleed), 0, 1));
        bool weaken = rng.Chance(Math.Clamp(s.Get(StatIds.ChanceToWeaken), 0, 1));
        double stun = s.Get(CombatStatIds.HammerStun) > 0 && rng.Chance(Math.Clamp(s.Get(CombatStatIds.HammerStunChance), 0, 1))
            ? s.Get(CombatStatIds.HammerStunDuration) : 0;
        return new Procs(
            s.Get(StatIds.HealthOnHit) * s.Get(StatIds.AllRecoveryMult),
            (s.Get(StatIds.EnergyOnHit) + s.Get(StatIds.StaminaOnHit)) * s.Get(StatIds.AllRecoveryMult),
            slow, bleed, damageDealt / AbilityTuning.BleedDivisor, weaken, stun);
    }

    /// <summary>Damage reflected to a melee attacker: (thorns + per-STR + per-VIT) × thorns multiplier × melee and all-damage multipliers (COTF).</summary>
    public static double Thorns(StatSheet s) =>
        Math.Max(0, s.Get(StatIds.Thorns) + s.Get(CombatStatIds.ThornsPerStrength) * s.Get(StatIds.Strength) + s.Get(CombatStatIds.ThornsPerVit) * s.Get(StatIds.Vitality))
        * s.Get(CombatStatIds.ThornsDmgMult) * s.Get(StatIds.MeleeIncreasedDmg) * s.Get(StatIds.AllDamage);
}
