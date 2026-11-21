using System.Numerics;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>How an active ability's damage reaches the player.</summary>
public enum AbilityDamageKind
{
    None,
    Magic,
}

/// <summary>
/// Timings, ranges and damage of one active (spell-like) enemy ability. COTF's exact numbers are not in the ticket notes,
/// so apart from Meteor and Laser damage (the difficulty table) these are this mod's own; see docs/mechanics.md.
/// </summary>
public sealed record ActiveAbilitySpec(
    EnemyAbility Ability,
    double Cooldown,
    double MinRange,
    double MaxRange,
    double Windup,
    double Duration,
    double Radius,
    double TickSeconds,
    double DamageFactor,
    AbilityDamageKind DamageKind);

public static class ActiveAbilityRules
{
    /// <summary>First cast happens this long after the enemy becomes active (plus a per-enemy spread), so a pack never opens in sync.</summary>
    public const double StartDelayMin = 2, StartDelaySpread = 4;

    /// <summary>Slow amounts (speed multiplier) and stun/root seconds applied to the player.</summary>
    public const double BlizzardSlow = 0.55, ArcaneSlow = 0.5;
    public const double ChainsRootSeconds = 2.5, TrapRootSeconds = 3;

    /// <summary>Player debuffs (COS-90; this mod's numbers): Blizzard's attack-speed multiplier and Radiance's damage-dealt multiplier while inside
    /// (refreshed every aura tick, lasting a little longer than it), Meteor's armor corrosion (share of the player's armor, per hit, accumulating)
    /// and the Laser's attack-speed and damage multipliers.</summary>
    public const double BlizzardAttackSlow = 0.8, RadianceDamageDown = 0.85, AuraDebuffSeconds = 1.5;
    public const double MeteorCorrosionShare = 0.1, MeteorCorrosionSeconds = 30;
    public const double LaserAttackSlow = 0.7, LaserDamageDown = 0.8, LaserDebuffSeconds = 5;

    /// <summary>Black Hole: pull speed (m/s) at the rim and at the centre; the core (this fraction of the radius) deals damage.</summary>
    public const double BlackHolePullRim = 2, BlackHolePullCentre = 9, BlackHoleCoreFraction = 0.25;

    /// <summary>Warp: the enemy lands this far from the player.</summary>
    public const double WarpLandingDistance = 5;

    /// <summary>Trapper: traps placed per cast, spread radius around the player, and how long a trap waits.</summary>
    public const int TrapsPerCast = 3;
    public const double TrapSpread = 6, TrapLifetime = 20;

    // Factors multiply the Meteor damage of the difficulty (the mean of its min..max) for damage per hit / per tick.
    public static readonly IReadOnlyList<ActiveAbilitySpec> All = new[]
    {
        // Meteor: a marked circle on the player's spot, impact after the windup. Damage = the difficulty's Meteor roll.
        new ActiveAbilitySpec(EnemyAbility.Meteor, 14, 0, 45, 2.0, 0, 4, 0, 1.0, AbilityDamageKind.Magic),
        // Laser: charge, then a 1 s beam to where the player stood; hits at most once per 0.5 s. Damage = the Laser roll.
        new ActiveAbilitySpec(EnemyAbility.Laser, 10, 3, 35, 1.5, 1.0, 0.8, 0.5, 1.0, AbilityDamageKind.Magic),
        // Black Hole: a well near the player that pulls and burns the core.
        new ActiveAbilitySpec(EnemyAbility.BlackHole, 28, 5, 35, 1.0, 6, 12, 1.0, 0.05, AbilityDamageKind.Magic),
        // Blizzard: aura around the enemy: slow and cold damage.
        new ActiveAbilitySpec(EnemyAbility.Blizzard, 0, 0, 0, 0, 0, 15, 1.0, 0.02, AbilityDamageKind.Magic),
        // Radiance: aura around the enemy: burning light.
        new ActiveAbilitySpec(EnemyAbility.Radiance, 0, 0, 0, 0, 0, 10, 1.0, 0.04, AbilityDamageKind.Magic),
        // Flare: a flare rises and bursts over the player: bright light, damage in the burst.
        new ActiveAbilitySpec(EnemyAbility.Flare, 20, 5, 40, 1.5, 3, 7, 0, 0.25, AbilityDamageKind.Magic),
        // Cataclysms: a vortex that follows the enemy for 10 s.
        new ActiveAbilitySpec(EnemyAbility.FireCataclysm, 40, 0, 40, 1.0, 10, 8, 1.0, 0.15, AbilityDamageKind.Magic),
        new ActiveAbilitySpec(EnemyAbility.ArcaneCataclysm, 40, 0, 40, 1.0, 10, 8, 1.0, 0.1, AbilityDamageKind.Magic),
        // Warp: blink next to a distant player.
        new ActiveAbilitySpec(EnemyAbility.Warp, 12, 15, 60, 0.75, 0, 0, 0, 0, AbilityDamageKind.None),
        // Chains: a tether that roots the player.
        new ActiveAbilitySpec(EnemyAbility.Chains, 15, 4, 25, 1.0, ChainsRootSeconds, 0, 0, 0, AbilityDamageKind.None),
        // Trapper: snares placed around the player, sprung when stepped on.
        new ActiveAbilitySpec(EnemyAbility.Trapper, 18, 4, 28, 1.5, TrapLifetime, 2.5, 0, 0.2, AbilityDamageKind.Magic),
    };

    private static readonly Dictionary<EnemyAbility, ActiveAbilitySpec> ByAbility = All.ToDictionary(s => s.Ability);

    public static bool IsActive(EnemyAbility ability) => ByAbility.ContainsKey(ability);

    public static ActiveAbilitySpec Spec(EnemyAbility ability) =>
        ByAbility.TryGetValue(ability, out var s) ? s : throw new ArgumentException($"{ability} is not an active ability");

    /// <summary>Damage of one hit/tick before the enemy's own multipliers: Meteor and Laser roll the difficulty table, the rest scale Meteor.</summary>
    public static double Damage(DifficultyTier d, EnemyAbility ability, Rng rng)
    {
        var spec = Spec(ability);
        if (spec.DamageKind == AbilityDamageKind.None) return 0;
        return ability switch
        {
            EnemyAbility.Meteor => rng.RangeInclusive(d.MeteorDamageMin, d.MeteorDamageMax) * spec.DamageFactor,
            EnemyAbility.Laser => rng.RangeInclusive(d.LaserDamageMin, d.LaserDamageMax) * spec.DamageFactor,
            _ => (d.MeteorDamageMin + d.MeteorDamageMax) / 2.0 * spec.DamageFactor,
        };
    }

    /// <summary>Whether a spell-like ability may start a cast: the target must be in range (MinRange..MaxRange).</summary>
    public static bool InRange(ActiveAbilitySpec spec, double distance) => distance >= spec.MinRange && distance <= spec.MaxRange;

    /// <summary>Black Hole pull speed in m/s at <paramref name="distance"/> from the centre: zero outside the radius, rising to the centre.</summary>
    public static double BlackHolePull(double distance, double radius)
    {
        if (radius <= 0 || distance >= radius) return 0;
        double closeness = 1 - Math.Max(0, distance) / radius;
        return BlackHolePullRim + (BlackHolePullCentre - BlackHolePullRim) * closeness;
    }

    public static bool InBlackHoleCore(double distance, double radius) => distance <= radius * BlackHoleCoreFraction;

    /// <summary>Distance from a point to the segment a..b (the Laser beam's axis).</summary>
    public static double DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-6f) return Vector3.Distance(p, a);
        float t = Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0, 1);
        return Vector3.Distance(p, a + ab * t);
    }

    /// <summary>The Laser hits when the player is within the beam's half-width of its axis.</summary>
    public static bool BeamHits(Vector3 player, Vector3 from, Vector3 to, double halfWidth) => DistanceToSegment(player, from, to) <= halfWidth;

    /// <summary>A point <paramref name="distance"/> from <paramref name="target"/> on the line towards <paramref name="from"/> (Warp landing).</summary>
    public static Vector3 LandingPoint(Vector3 from, Vector3 target, double distance)
    {
        var dir = from - target;
        dir.Y = 0;
        if (dir.LengthSquared() < 1e-6f) dir = Vector3.UnitX;
        return target + Vector3.Normalize(dir) * (float)distance;
    }
}

/// <summary>
/// One enemy's cast gate (COS-90): only one active ability casts at a time, and after a cast ends a shared lockout of 0 to
/// <see cref="MaxLockout"/> s must pass before any ability on that enemy may start another. Each ability keeps its own cooldown.
/// </summary>
public sealed class CastGate
{
    public const double MaxLockout = 3;

    private bool _busy;
    private double _lockout;

    public bool Busy => _busy;
    public double Lockout => _lockout;

    /// <summary>True when no cast is running and the lockout is over.</summary>
    public bool CanStart => !_busy && _lockout <= 0;

    /// <summary>Claims the gate for a cast; false (nothing claimed) if it is not free.</summary>
    public bool TryBegin()
    {
        if (!CanStart) return false;
        _busy = true;
        return true;
    }

    /// <summary>A cast ended: free the gate and start the shared lockout.</summary>
    public void End(Rng rng)
    {
        _busy = false;
        _lockout = rng.NextDouble() * MaxLockout;
    }

    /// <summary>A cast was cut short (the enemy went inactive): free the gate without a lockout.</summary>
    public void Release() => _busy = false;

    public void Tick(double dt)
    {
        if (_lockout > 0) _lockout = Math.Max(0, _lockout - dt);
    }
}

/// <summary>
/// Cooldown clock of one ability on one enemy. It only advances while the enemy is alive and active (the host loop calls it
/// from <c>OnTick</c>), so a dormant or dead enemy never casts. Idle until <see cref="Tick"/> reports ready.
/// </summary>
public sealed class AbilityClock
{
    private double _left;

    public AbilityClock(double firstDelay) => _left = firstDelay;

    public double Left => _left;

    public static AbilityClock Start(Rng rng) =>
        new(ActiveAbilityRules.StartDelayMin + rng.NextDouble() * ActiveAbilityRules.StartDelaySpread);

    /// <summary>Advances the clock; true when a cast may start (the caller then calls <see cref="Fire"/> if it casts).</summary>
    public bool Tick(double dt)
    {
        if (_left > 0) _left = Math.Max(0, _left - dt);
        return _left <= 0;
    }

    public void Fire(double cooldown) => _left = cooldown;
}
