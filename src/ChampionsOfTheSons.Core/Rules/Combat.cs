namespace ChampionsOfTheSons.Core.Rules;

public enum DamageKind
{
    Physical,
    Magic,
    Pure,
}

public enum WeaponClass
{
    Melee,
    Ranged,
    Thrown,
    Spell,
}

/// <summary>An outgoing hit, computed by the attacker from its own stat sheet.</summary>
public readonly record struct OutgoingHit(double Amount, DamageKind Kind, bool Crit, double ArmorPierce);

/// <summary>Armor reduction shared by players and enemies.</summary>
public static class ArmorMath
{
    /// <summary>DR(a) = (a / (a + 500))², a clamped at 0.</summary>
    public static double Reduction(double armor)
    {
        double a = Math.Max(armor, 0);
        double r = a / (a + 500);
        return r * r;
    }
}

/// <summary>Player → enemy damage.</summary>
public static class PlayerAttack
{
    /// <summary>A weapon hit (melee, ranged or thrown). <paramref name="baseDamage"/> is the game weapon's own damage.</summary>
    public static OutgoingHit Weapon(StatSheet s, WeaponClass weapon, double baseDamage, Rng rng,
        bool heavy = false, bool headshot = false, int multishotIndex = 0)
    {
        bool crit = rng.Chance(s.Get(StatIds.CritChance));
        double critMult = crit ? 1 + s.Get(StatIds.CritDamage) : 1;
        double d;
        double pierce;
        if (weapon == WeaponClass.Melee)
        {
            d = (baseDamage + s.Get(StatIds.MeleeFlatDmg)) * critMult * Derived.MeleeMultiplier(s);
            if (heavy) d *= 3 * s.Get(StatIds.HeavyAttackDmg);
            pierce = Derived.MeleeArmorPierce(s);
        }
        else
        {
            d = (baseDamage + s.Get(StatIds.RangedFlatDmg)) * critMult * Derived.RangedMultiplier(s);
            d *= Math.Pow(0.75, Math.Max(0, multishotIndex));
            if (headshot) d *= s.Get(StatIds.HeadShotDamage);
            pierce = Derived.RangedArmorPierce(s);
        }
        return new OutgoingHit(Math.Max(0, d), DamageKind.Physical, crit, pierce);
    }

    /// <summary>A spell hit: (base + spellFlat × scaling) × spell multiplier, magic damage. Crit only if the spell allows it.</summary>
    public static OutgoingHit Spell(StatSheet s, double baseDamage, double flatScaling, Rng rng, bool canCrit = false)
    {
        bool crit = canCrit && rng.Chance(s.Get(StatIds.CritChance));
        double d = (baseDamage + s.Get(StatIds.SpellFlatDmg) * flatScaling) * Derived.SpellMultiplier(s);
        if (crit) d *= 1 + s.Get(StatIds.CritDamage);
        return new OutgoingHit(Math.Max(0, d), DamageKind.Magic, crit, 0);
    }
}

/// <summary>Enemy defenses as a pure function, for when the game owns the health value.</summary>
public static class EnemyDefense
{
    /// <summary>Damage after armor (physical/magic), damage-taken multiplier and the steadfast cap,
    /// plus the enemy's new armor reduction after this hit's armor pierce.</summary>
    public static (double Damage, double ArmorReduction) Apply(EnemyProfile profile, double armorReduction, OutgoingHit hit, double damageTakenMult = 1, double armorMult = 1)
    {
        double effectiveArmor = Math.Max(0, profile.Armor * armorMult - armorReduction);
        double dmg = hit.Amount * damageTakenMult;
        dmg *= hit.Kind switch
        {
            DamageKind.Physical => 1 - ArmorMath.Reduction(effectiveArmor),
            DamageKind.Magic => 1 - ArmorMath.Reduction(effectiveArmor) / 1.5,
            _ => 1,
        };
        double cap = profile.HitCap < 1 ? Math.Max(profile.HitCap * profile.MaxHealth, 1) : double.MaxValue;
        dmg = Math.Max(0, Math.Min(dmg, cap));
        double reduction = Math.Min(profile.Armor, armorReduction + Math.Max(0, hit.ArmorPierce));
        return (dmg, reduction);
    }
}

/// <summary>Host-side enemy defense state for one enemy: health, armor pierce taken, damage-taken debuffs.</summary>
public sealed class EnemyHealthState
{
    public EnemyProfile Profile { get; }
    public double Health { get; private set; }
    /// <summary>Armor removed by pierce and fire; never more than the enemy's armor.</summary>
    public double ArmorReduction { get; private set; }
    public double DamageTakenMult { get; set; } = 1;
    public bool Immune { get; set; }
    public bool UndeadUsed { get; private set; }

    public EnemyHealthState(EnemyProfile profile)
    {
        Profile = profile;
        Health = profile.MaxHealth;
    }

    public bool Dead => Health <= 0;

    public double EffectiveArmor => Math.Max(0, Profile.Armor - ArmorReduction);

    /// <summary>Applies a hit and returns the damage dealt. Handles armor, magic, pure, steadfast cap and Undead.</summary>
    public double TakeHit(OutgoingHit hit)
    {
        if (Dead || Immune) return 0;
        var (dmg, reduction) = EnemyDefense.Apply(Profile, ArmorReduction, hit, DamageTakenMult);
        ArmorReduction = reduction;
        Health -= dmg;
        if (Health <= 0 && Profile.Has(EnemyAbility.Undead) && !UndeadUsed)
        {
            UndeadUsed = true;
            Health = Profile.MaxHealth / 2;
        }
        return dmg;
    }

    /// <summary>Sets health from the host (clients mirror the host's numbers).</summary>
    public void SetHealth(double health, double armorReduction)
    {
        Health = health;
        ArmorReduction = Math.Clamp(armorReduction, 0, Profile.Armor);
    }
}

/// <summary>Enemy → player damage after the player's defenses.</summary>
public static class PlayerDefense
{
    public readonly record struct Result(double Damage, bool Dodged);

    /// <summary><paramref name="lostArmor"/> is armor removed by debuffs (armor corruption).</summary>
    public static Result Incoming(StatSheet s, double damage, DamageKind kind, Rng rng, double lostArmor = 0)
    {
        if (kind == DamageKind.Physical && rng.NextDouble() > s.Get(StatIds.GetHitChance))
            return new Result(0, true);
        double f = damage * s.Get(StatIds.AllDamageTaken);
        switch (kind)
        {
            case DamageKind.Physical:
                f *= 1 - ArmorMath.Reduction(s.Get(StatIds.Armor) - lostArmor);
                f -= s.Get(StatIds.Block);
                break;
            case DamageKind.Magic:
                f *= s.Get(StatIds.MagicDamageTaken);
                break;
        }
        return new Result(Math.Max(0, f), false);
    }
}
