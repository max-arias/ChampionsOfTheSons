using System.Runtime.CompilerServices;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Abilities;

/// <summary>
/// One active ability on one enemy: its cooldown clock, cast state and every Unity object it made. The host loop ticks it only
/// while the enemy is alive and active, and <see cref="Dispose"/> removes every object and effect when that stops being true
/// (death, dormancy, despawn), so nothing keeps casting or lingers.
/// </summary>
internal abstract class AbilityInstance : IDisposable
{
    protected static readonly Rng Rng = Rng.FromTime();

    private readonly List<GameObject> _objects = new();
    private readonly List<(GameObject Go, float Left)> _flashes = new();

    protected AbilityInstance(EnemyRuntime enemy, ActiveAbilitySpec spec)
    {
        Enemy = enemy;
        Spec = spec;
    }

    protected EnemyRuntime Enemy { get; }
    protected ActiveAbilitySpec Spec { get; }
    protected string Key => $"{Spec.Ability}:{RuntimeHelpers.GetHashCode(Enemy)}";
    protected Vector3 EnemyPos => Enemy.Actor!.transform.position;
    protected Vector3 Chest => EnemyPos + Vector3.up * 1.4f;

    protected static System.Numerics.Vector3 Num(Vector3 v) => new(v.x, v.y, v.z);

    public int ObjectCount => _objects.Count(o => o != null) + _flashes.Count;

    public void Tick(float dt)
    {
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var (go, left) = _flashes[i];
            left -= dt;
            if (left <= 0)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                _flashes.RemoveAt(i);
            }
            else
            {
                _flashes[i] = (go, left);
            }
        }
        Step(dt);
    }

    protected abstract void Step(float dt);

    protected GameObject Track(GameObject go)
    {
        _objects.Add(go);
        return go;
    }

    protected void Remove(ref GameObject? go)
    {
        if (go == null) return;
        _objects.Remove(go);
        UnityEngine.Object.Destroy(go);
        go = null;
    }

    /// <summary>A short-lived puff (impact, burst, teleport flash) that removes itself after <paramref name="seconds"/>.</summary>
    protected void Flash(Color color, Vector3 position, float radius, float seconds)
    {
        _flashes.Add((AbilityVisuals.Make(PrimitiveType.Sphere, color, position, Vector3.one * radius * 2, "cots-ability-flash"), seconds));
    }

    public virtual void Dispose()
    {
        foreach (var go in _objects)
            if (go != null) UnityEngine.Object.Destroy(go);
        _objects.Clear();
        foreach (var (go, _) in _flashes)
            if (go != null) UnityEngine.Object.Destroy(go);
        _flashes.Clear();
        AbilityForces.RemoveSlow(Key);
        AbilityForces.SetPull(Key, Vector3.zero, 0);
    }
}

/// <summary>Always-on area around the enemy (Blizzard, Radiance): damage and effects every second to a player inside it.</summary>
internal abstract class AuraInstance : AbilityInstance
{
    private GameObject? _disc;
    private float _phase;
    private readonly Color _color;

    protected AuraInstance(EnemyRuntime enemy, ActiveAbilitySpec spec, Color color) : base(enemy, spec)
    {
        _color = color;
        AbilityDamage.NoteFired(enemy, spec.Ability);
    }

    protected virtual void Created(GameObject disc) { }

    private Audio.AudioLoop? _loop;

    protected virtual void InsideEachFrame(Vector3 player) { }

    protected override void Step(float dt)
    {
        if (_disc == null)
        {
            _disc = Track(AbilityVisuals.Disc(_color, EnemyPos, (float)Spec.Radius, "cots-ability-aura"));
            Created(_disc);
            _loop = Audio.ModAudio.StartLoop(Audio.SoundCues.AuraLoop(Spec.Ability), EnemyPos, 30f, 0.5f);
        }
        _disc.transform.position = EnemyPos + Vector3.up * 0.05f;
        _loop?.Move(EnemyPos);

        bool inside = false;
        foreach (var p in AbilityTargets.Positions())
        {
            if (AbilityTargets.Flat(p, EnemyPos) > Spec.Radius || Mathf.Abs(p.y - EnemyPos.y) > 6) continue;
            inside = true;
            InsideEachFrame(p);
        }

        _phase += dt;
        if (_phase < Spec.TickSeconds) return;
        _phase -= (float)Spec.TickSeconds;
        if (inside) AbilityDamage.HitPlayer(Enemy, Spec.Ability, ActiveAbilityRules.Damage(WorldState.Rules.Difficulty, Spec.Ability, Rng));
    }

    public override void Dispose()
    {
        _loop?.Stop();
        _loop = null;
        base.Dispose();
    }
}

internal sealed class BlizzardInstance : AuraInstance
{
    public BlizzardInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Blizzard), new Color(0.75f, 0.9f, 1f)) { }

    protected override void InsideEachFrame(Vector3 player) => AbilityForces.AddSlow(Key, ActiveAbilityRules.BlizzardSlow, 0.4f);
}

internal sealed class RadianceInstance : AuraInstance
{
    public RadianceInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Radiance), new Color(1f, 0.85f, 0.3f)) { }

    protected override void Created(GameObject disc) =>
        AbilityVisuals.AddLight(Track(AbilityVisuals.Make(PrimitiveType.Sphere, new Color(1f, 0.9f, 0.5f), Chest, Vector3.one * 0.4f, "cots-ability-radiance")),
            new Color(1f, 0.85f, 0.4f), 14, 60);
}

/// <summary>
/// A cast: wait out the cooldown, pick a target in range, then run <see cref="Begin"/> → <see cref="Advance"/> until it finishes.
/// The cooldown restarts when the cast ends, and does not run while casting.
/// </summary>
internal abstract class CastInstance : AbilityInstance
{
    private readonly AbilityClock _clock = AbilityClock.Start(Rng);
    private bool _casting;
    private bool _impactSounded;
    protected float T;

    /// <summary>Where the cast lands, for its impact sound (COS-101); the enemy's chest unless the cast picks a spot.</summary>
    protected virtual Vector3 ImpactPoint => Chest;

    protected CastInstance(EnemyRuntime enemy, ActiveAbilitySpec spec) : base(enemy, spec) { }

    public bool Casting => _casting;

    protected abstract void Begin(Vector3 target);

    /// <summary>Runs every frame of the cast with the seconds since it began; true when it is over.</summary>
    protected abstract bool Advance(float dt);

    /// <summary>The cast is over (finished or cut short): hide what it showed.</summary>
    protected virtual void Finish() { }

    protected override void Step(float dt)
    {
        if (!_casting)
        {
            if (!_clock.Tick(dt)) return;
            if (AbilityTargets.Nearest(EnemyPos) is not { } target) return;
            if (!ActiveAbilityRules.InRange(Spec, Vector3.Distance(target, EnemyPos))) return;
            _casting = true;
            T = 0;
            AbilityDamage.NoteFired(Enemy, Spec.Ability);
            Begin(target);
            _impactSounded = false;
            Audio.ModAudio.PlayAt(Audio.SoundCues.Telegraph(Spec.Ability), EnemyPos, 50f);
        }
        T += dt;
        if (!_impactSounded && T >= Spec.Windup)
        {
            _impactSounded = true;
            Audio.ModAudio.PlayAt(Audio.SoundCues.Impact(Spec.Ability), ImpactPoint, 60f);
        }
        if (!Advance(dt)) return;
        _casting = false;
        Finish();
        _clock.Fire(Spec.Cooldown);
    }
}

internal sealed class MeteorInstance : CastInstance
{
    private Vector3 _spot;
    private GameObject? _marker;

    public MeteorInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Meteor)) { }

    protected override Vector3 ImpactPoint => _spot;

    protected override void Begin(Vector3 target)
    {
        _spot = target;
        _marker = Track(AbilityVisuals.Disc(new Color(1f, 0.25f, 0.1f), _spot, (float)Spec.Radius, "cots-ability-meteor-mark"));
    }

    protected override bool Advance(float dt)
    {
        if (_marker != null)
        {
            float k = Mathf.Clamp01(T / (float)Spec.Windup);
            _marker.transform.localScale = new Vector3((float)Spec.Radius * 2, 0.04f + 0.02f * Mathf.Sin(T * 14), (float)Spec.Radius * 2) * (0.6f + 0.4f * k);
        }
        if (T < Spec.Windup) return false;
        Flash(new Color(1f, 0.55f, 0.1f), _spot + Vector3.up, (float)Spec.Radius, 0.5f);
        foreach (var p in AbilityTargets.Positions())
            if (AbilityTargets.Flat(p, _spot) <= Spec.Radius && Mathf.Abs(p.y - _spot.y) < 5)
                AbilityDamage.HitPlayer(Enemy, EnemyAbility.Meteor, ActiveAbilityRules.Damage(WorldState.Rules.Difficulty, EnemyAbility.Meteor, Rng));
        return true;
    }

    protected override void Finish()
    {
        var m = _marker;
        Remove(ref m);
        _marker = null;
    }
}

internal sealed class LaserInstance : CastInstance
{
    private GameObject? _beam;
    private Vector3 _aim, _to;
    private float _tick;
    private bool _locked;

    public LaserInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Laser)) { }

    protected override void Begin(Vector3 target)
    {
        _aim = target + Vector3.up;
        _locked = false;
        _tick = 0;
        _beam = Track(AbilityVisuals.Make(PrimitiveType.Cylinder, new Color(1f, 0.1f, 0.1f), Chest, new Vector3(0.05f, 1, 0.05f), "cots-ability-laser"));
    }

    private Vector3 Extend(Vector3 aim)
    {
        var dir = (aim - Chest).normalized;
        return Chest + dir * (float)Spec.MaxRange;
    }

    protected override bool Advance(float dt)
    {
        if (T < Spec.Windup)
        {
            // Charging: a thin line follows the player.
            if (AbilityTargets.Nearest(EnemyPos) is { } p) _aim = p + Vector3.up;
            _to = Extend(_aim);
            if (_beam != null) AbilityVisuals.Stretch(_beam, Chest, _to, 0.05f);
            return false;
        }
        if (!_locked)
        {
            _locked = true;
            _to = Extend(_aim);
        }
        if (T >= Spec.Windup + Spec.Duration) return true;
        if (_beam != null) AbilityVisuals.Stretch(_beam, Chest, _to, 0.4f);
        _tick -= dt;
        if (_tick <= 0)
        {
            _tick = (float)Spec.TickSeconds;
            foreach (var p in AbilityTargets.Positions())
                if (ActiveAbilityRules.BeamHits(Num(p + Vector3.up), Num(Chest), Num(_to), Spec.Radius))
                    AbilityDamage.HitPlayer(Enemy, EnemyAbility.Laser, ActiveAbilityRules.Damage(WorldState.Rules.Difficulty, EnemyAbility.Laser, Rng));
        }
        return false;
    }

    protected override void Finish()
    {
        var b = _beam;
        Remove(ref b);
        _beam = null;
    }
}

internal sealed class BlackHoleInstance : CastInstance
{
    private Vector3 _center;
    private GameObject? _marker, _core;
    private float _tick;

    public BlackHoleInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.BlackHole)) { }

    protected override void Begin(Vector3 target)
    {
        _center = target + Vector3.up * 1.2f;
        _tick = 0;
        _marker = Track(AbilityVisuals.Disc(new Color(0.35f, 0.1f, 0.6f), target, (float)Spec.Radius, "cots-ability-blackhole-mark"));
    }

    protected override bool Advance(float dt)
    {
        if (T < Spec.Windup) return false;
        if (T >= Spec.Windup + Spec.Duration) return true;
        if (_core == null) _core = Track(AbilityVisuals.Make(PrimitiveType.Sphere, Color.black, _center, Vector3.one * 2.5f, "cots-ability-blackhole"));
        _core.transform.localScale = Vector3.one * (2.5f + 0.3f * Mathf.Sin(T * 6));

        _tick += dt;
        bool damage = _tick >= Spec.TickSeconds;
        if (damage) _tick -= (float)Spec.TickSeconds;
        foreach (var p in AbilityTargets.Positions())
        {
            float dist = Vector3.Distance(p + Vector3.up, _center);
            if (dist >= Spec.Radius) continue;
            // Only the local player can be pulled (co-op: the owner applies the pull to their own character).
            var dir = (_center - (p + Vector3.up)).normalized;
            AbilityForces.SetPull(Key, dir * (float)ActiveAbilityRules.BlackHolePull(dist, Spec.Radius), 0.5f);
            if (damage && ActiveAbilityRules.InBlackHoleCore(dist, Spec.Radius))
                AbilityDamage.HitPlayer(Enemy, EnemyAbility.BlackHole, ActiveAbilityRules.Damage(WorldState.Rules.Difficulty, EnemyAbility.BlackHole, Rng));
        }
        return false;
    }

    protected override void Finish()
    {
        AbilityForces.SetPull(Key, Vector3.zero, 0);
        var m = _marker;
        Remove(ref m);
        _marker = null;
        var c = _core;
        Remove(ref c);
        _core = null;
    }

    public override void Dispose()
    {
        AbilityForces.SetPull(Key, Vector3.zero, 0);
        base.Dispose();
    }
}

internal sealed class FlareInstance : CastInstance
{
    private Vector3 _burst;
    private GameObject? _orb;
    private bool _burstDone;

    public FlareInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Flare)) { }

    protected override Vector3 ImpactPoint => _burst;

    protected override void Begin(Vector3 target)
    {
        _burst = target + Vector3.up * 10;
        _burstDone = false;
        _orb = Track(AbilityVisuals.Make(PrimitiveType.Sphere, new Color(1f, 0.95f, 0.6f), Chest, Vector3.one * 0.3f, "cots-ability-flare"));
    }

    protected override bool Advance(float dt)
    {
        if (_orb == null) return true;
        if (T < Spec.Windup)
        {
            _orb.transform.position = Vector3.Lerp(Chest, _burst, T / (float)Spec.Windup);
            return false;
        }
        if (!_burstDone)
        {
            _burstDone = true;
            _orb.transform.position = _burst;
            _orb.transform.localScale = Vector3.one * 2.5f;
            AbilityVisuals.AddLight(_orb, new Color(1f, 0.95f, 0.75f), 45, 400);
            foreach (var p in AbilityTargets.Positions())
                if (AbilityTargets.Flat(p, _burst) <= Spec.Radius)
                    AbilityDamage.HitPlayer(Enemy, EnemyAbility.Flare, ActiveAbilityRules.Damage(WorldState.Rules.Difficulty, EnemyAbility.Flare, Rng));
        }
        return T >= Spec.Windup + Spec.Duration;
    }

    protected override void Finish()
    {
        var o = _orb;
        Remove(ref o);
        _orb = null;
    }
}

/// <summary>Fire and Arcane Cataclysm: a vortex that follows the enemy for 10 s, hurting (and, arcane, slowing) everything inside.</summary>
internal sealed class CataclysmInstance : CastInstance
{
    private readonly bool _arcane;
    private GameObject? _vortex;
    private float _tick;

    public CataclysmInstance(EnemyRuntime e, bool arcane)
        : base(e, ActiveAbilityRules.Spec(arcane ? EnemyAbility.ArcaneCataclysm : EnemyAbility.FireCataclysm)) => _arcane = arcane;

    protected override void Begin(Vector3 target)
    {
        _tick = 0;
        var color = _arcane ? new Color(0.4f, 0.4f, 1f) : new Color(1f, 0.4f, 0.05f);
        _vortex = Track(AbilityVisuals.Make(PrimitiveType.Cylinder, color, EnemyPos + Vector3.up * 5, new Vector3(0.5f, 5, 0.5f), "cots-ability-cataclysm"));
    }

    protected override bool Advance(float dt)
    {
        if (_vortex == null) return true;
        _vortex.transform.position = EnemyPos + Vector3.up * 5;
        _vortex.transform.Rotate(0, 240 * dt, 0);
        float grow = Mathf.Clamp01(T / (float)Spec.Windup);
        float r = (float)Spec.Radius * grow;
        _vortex.transform.localScale = new Vector3(r * 2, 5, r * 2);
        if (T < Spec.Windup) return false;
        if (T >= Spec.Windup + Spec.Duration) return true;

        _tick += dt;
        bool damage = _tick >= Spec.TickSeconds;
        if (damage) _tick -= (float)Spec.TickSeconds;
        foreach (var p in AbilityTargets.Positions())
        {
            if (AbilityTargets.Flat(p, EnemyPos) > Spec.Radius || Mathf.Abs(p.y - EnemyPos.y) > 8) continue;
            if (_arcane) AbilityForces.AddSlow(Key, ActiveAbilityRules.ArcaneSlow, 0.4f);
            if (damage) AbilityDamage.HitPlayer(Enemy, Spec.Ability, ActiveAbilityRules.Damage(WorldState.Rules.Difficulty, Spec.Ability, Rng));
        }
        return false;
    }

    protected override void Finish()
    {
        var v = _vortex;
        Remove(ref v);
        _vortex = null;
    }
}

internal sealed class WarpInstance : CastInstance
{
    private Vector3 _target;
    private GameObject? _orb;

    /// <summary>Times it blinked (for self-tests).</summary>
    public int Warps { get; private set; }

    public WarpInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Warp)) { }

    protected override void Begin(Vector3 target)
    {
        _target = target;
        _orb = Track(AbilityVisuals.Make(PrimitiveType.Sphere, new Color(0.7f, 0.3f, 1f), EnemyPos + Vector3.up, Vector3.one * 0.5f, "cots-ability-warp"));
    }

    protected override bool Advance(float dt)
    {
        if (_orb != null) _orb.transform.localScale = Vector3.one * (0.5f + 2f * T / (float)Spec.Windup);
        if (T < Spec.Windup) return false;
        var from = EnemyPos;
        if (AbilityTargets.Nearest(from) is { } now) _target = now;
        var land = ActiveAbilityRules.LandingPoint(new System.Numerics.Vector3(from.x, from.y, from.z),
            new System.Numerics.Vector3(_target.x, _target.y, _target.z), ActiveAbilityRules.WarpLandingDistance);
        var dest = new Vector3(land.X, _target.y, land.Z);
        Flash(new Color(0.7f, 0.3f, 1f), from + Vector3.up, 1.5f, 0.4f);
        Enemy.Actor!.transform.position = dest;
        Enemy.LastPosition = dest;
        Flash(new Color(0.7f, 0.3f, 1f), dest + Vector3.up, 1.5f, 0.4f);
        Warps++;
        AbilityDamage.NoteEffect(Enemy, EnemyAbility.Warp);
        return true;
    }

    protected override void Finish()
    {
        var o = _orb;
        Remove(ref o);
        _orb = null;
    }
}

internal sealed class ChainsInstance : CastInstance
{
    private GameObject? _chain;
    private Vector3 _end;
    private bool _bound;

    public ChainsInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Chains)) { }

    protected override void Begin(Vector3 target)
    {
        _end = target + Vector3.up;
        _bound = false;
        _chain = Track(AbilityVisuals.Make(PrimitiveType.Cylinder, new Color(0.55f, 0.55f, 0.6f), Chest, new Vector3(0.05f, 1, 0.05f), "cots-ability-chains"));
    }

    protected override bool Advance(float dt)
    {
        if (AbilityTargets.Nearest(EnemyPos) is { } p) _end = p + Vector3.up;
        if (_chain != null) AbilityVisuals.Stretch(_chain, Chest, _end, _bound ? 0.18f : 0.06f);
        if (T < Spec.Windup) return false;
        if (!_bound)
        {
            _bound = true;
            // Still in reach when the chain closes: the player is rooted (stun immunity refuses it).
            if (Vector3.Distance(_end, Chest) <= Spec.MaxRange + 5 && PlayerEffects.Stun(ActiveAbilityRules.ChainsRootSeconds))
                AbilityDamage.NoteEffect(Enemy, EnemyAbility.Chains);
        }
        return T >= Spec.Windup + Spec.Duration;
    }

    protected override void Finish()
    {
        var c = _chain;
        Remove(ref c);
        _chain = null;
    }
}

/// <summary>Snares placed around the player. The cast is the telegraph; the traps then wait up to 20 s and root and hurt whoever steps on one.</summary>
internal sealed class TrapperInstance : CastInstance
{
    private const int MaxTraps = 9;

    private sealed class Trap
    {
        public GameObject Go = null!;
        public Vector3 Pos;
        public float Age;
        public bool Armed;
    }

    private readonly List<Trap> _traps = new();

    public int TrapCount => _traps.Count;

    public TrapperInstance(EnemyRuntime e) : base(e, ActiveAbilityRules.Spec(EnemyAbility.Trapper)) { }

    protected override void Begin(Vector3 target)
    {
        for (int i = 0; i < ActiveAbilityRules.TrapsPerCast; i++)
        {
            // The first lies where the player stands (the telegraph is the dodge); the rest around them.
            var pos = target;
            if (i > 0)
            {
                float a = Rng.Range(0f, 6.283f);
                float r = Rng.Range(2f, (float)ActiveAbilityRules.TrapSpread);
                pos += new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            }
            _traps.Add(new Trap { Pos = pos, Go = Track(AbilityVisuals.Disc(new Color(0.5f, 0.45f, 0.1f), pos, (float)Spec.Radius, "cots-ability-trap")) });
        }
        while (_traps.Count > MaxTraps) DropTrap(0);
    }

    private void DropTrap(int index)
    {
        var go = _traps[index].Go;
        _traps.RemoveAt(index);
        GameObject? g = go;
        Remove(ref g);
    }

    protected override bool Advance(float dt) => T >= Spec.Windup;

    protected override void Step(float dt)
    {
        base.Step(dt);
        for (int i = _traps.Count - 1; i >= 0; i--)
        {
            var trap = _traps[i];
            trap.Age += dt;
            if (!trap.Armed && trap.Age >= Spec.Windup)
            {
                trap.Armed = true;
                if (trap.Go != null) AbilityVisuals.Recolor(trap.Go, new Color(1f, 0.7f, 0.1f));
            }
            if (trap.Age >= Spec.Windup + Spec.Duration)
            {
                DropTrap(i);
                continue;
            }
            if (!trap.Armed) continue;
            foreach (var p in AbilityTargets.Positions())
            {
                if (AbilityTargets.Flat(p, trap.Pos) > Spec.Radius || Mathf.Abs(p.y - trap.Pos.y) > 2) continue;
                PlayerEffects.Stun(ActiveAbilityRules.TrapRootSeconds);
                AbilityDamage.HitPlayer(Enemy, EnemyAbility.Trapper, ActiveAbilityRules.Damage(WorldState.Rules.Difficulty, EnemyAbility.Trapper, Rng));
                DropTrap(i);
                break;
            }
        }
    }
}
