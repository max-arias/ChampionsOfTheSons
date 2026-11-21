using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>A ground effect that pulses every <see cref="Interval"/> seconds and goes after <see cref="Left"/> runs out (Black Hole, Snow Storm, domes...).</summary>
internal sealed class SpellZone
{
    public string Id = "";
    public Vector3 Center;
    public float Radius, Left, Interval = 1, Next = 1;
    public int Pulses;
    public Action<SpellZone>? Pulse;
    public Action<SpellZone, float>? Frame;
    public Action<SpellZone>? End;
    public GameObject? Visual;
    /// <summary>The cast that made it (COS-80): visuals drawn by its pulses belong to that cast.</summary>
    public uint? CastId;
}

/// <summary>A flying spell: moves in a line (or homes on a target), hits enemies it touches and ends on impact, range or time.</summary>
internal sealed class SpellBolt
{
    public string Id = "";
    public Vector3 Pos, Dir;
    public float Speed, Range, Radius = 1.2f, Life = 10;
    public bool Pierce, Homing;
    public EnemyRuntime? Target;
    public Action<SpellBolt, EnemyRuntime>? OnHit;
    public GameObject? Visual;
    /// <summary>The cast that made it (COS-80).</summary>
    public uint? CastId;
    public readonly HashSet<EnemyRuntime> Hit = new();
    internal float Travelled;
    internal bool Done;
}

/// <summary>
/// The live part of the spells: timed player buffs, shield, parry and frenzy, zones, bolts, portals and the upkeep of channeled/toggle spells.
/// Everything advances in <see cref="Step"/>, which the spells feature calls every frame and self-tests call themselves
/// (the headless game runs with time scale 0).
/// </summary>
internal static class SpellWorld
{
    public static readonly SpellShield Shield = new();
    public static readonly ParryState Parry = new();
    public static readonly FrenzyState Frenzy = new();

    private static readonly List<SpellZone> Zones = new();
    private static readonly List<SpellBolt> Bolts = new();
    private static SpellBuffs? _buffs;
    private static StatSheet? _buffSheet;
    private static readonly Rng Rng = Rng.FromTime();

    /// <summary>True while a spell is dealing its own damage, so hit reactions (Frenzy stacks...) ignore it.</summary>
    public static bool Striking { get; private set; }

    public static int ZoneCount => Zones.Count;
    public static int BoltCount => Bolts.Count;

    public static PlayerCharacter Pc => PlayerState.Character;
    public static StatSheet Stats => PlayerState.Character.Stats;

    public static SpellBuffs Buffs
    {
        get
        {
            if (_buffs == null || !ReferenceEquals(_buffSheet, Stats))
            {
                _buffSheet = Stats;
                _buffs = new SpellBuffs(Stats);
            }
            return _buffs;
        }
    }

    public static float S(string stat) => (float)Stats.Get(stat);

    // ---------------------------------------------------------------- helpers for spell effects

    public static Transform Eye => Camera.main != null ? Camera.main.transform : LocalPlayer.Transform;
    public static Vector3 PlayerPos => LocalPlayer.Transform.position;

    /// <summary>Where the view ray meets the world within <paramref name="range"/> (the far end of the ray if nothing is there).</summary>
    public static Vector3 AimPoint(float range)
    {
        var eye = Eye;
        var self = LocalPlayer.Transform != null ? LocalPlayer.Transform.root : null;
        float best = range;
        foreach (var h in Physics.RaycastAll(eye.position, eye.forward, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (self != null && h.collider.transform.IsChildOf(self)) continue;
            if (h.distance < best) best = h.distance;
        }
        return eye.position + eye.forward * best;
    }

    /// <summary>The aim point put on the ground below it.</summary>
    public static Vector3 AimGround(float range)
    {
        var p = AimPoint(range);
        if (SpellCaster.GroundY(p + Vector3.up * 3) is float gy && gy <= p.y + 4) p.y = gy;
        return p;
    }

    public static Vector3 Center(EnemyRuntime e) => e.Actor!.transform.position + Vector3.up;

    public static IEnumerable<EnemyRuntime> ActiveEnemies() => EnemyRegistry.Live.Where(e => e.IsActive && e.Actor != null).ToList();

    public static List<EnemyRuntime> EnemiesWithin(Vector3 center, float radius) =>
        ActiveEnemies().Where(e => Vector3.Distance(Center(e), center) <= radius + 0.5f).ToList();

    public static EnemyRuntime? Nearest(Vector3 from, float maxDistance, Func<EnemyRuntime, bool>? filter = null) =>
        ActiveEnemies().Where(e => filter == null || filter(e)).Select(e => (e, d: Vector3.Distance(Center(e), from)))
            .Where(x => x.d <= maxDistance).OrderBy(x => x.d).Select(x => x.e).FirstOrDefault();

    /// <summary>Enemies in front of the player inside a cone (half angle <paramref name="halfAngle"/>) and range.</summary>
    public static List<EnemyRuntime> InCone(Vector3 from, Vector3 dir, float range, float halfAngle) =>
        ActiveEnemies().Where(e =>
        {
            var to = Center(e) - from;
            return to.magnitude <= range && Vector3.Angle(dir, to) <= halfAngle;
        }).ToList();

    /// <summary>A spell hit on an enemy through the same shield, armor and cap rules as any player hit (credited to the player).</summary>
    public static void Strike(EnemyRuntime e, OutgoingHit hit)
    {
        if (!e.IsActive) return;
        Striking = true;
        try
        {
            EnemyPatches.DealForced(e, hit, creditPlayer: true);
        }
        finally
        {
            Striking = false;
        }
    }

    public static OutgoingHit Hit(double baseDamage, double scaling, bool canCrit = false) => SpellTuning.Hit(Stats, baseDamage, scaling, Rng, canCrit);

    /// <summary>Moves an enemy (pull, knockback) by shifting its transform.</summary>
    public static void Push(EnemyRuntime e, Vector3 delta)
    {
        if (!e.IsActive || delta == Vector3.zero) return;
        e.Actor!.transform.position += delta;
        if (Net.CoopHits.ForwardsToHost) Net.CoopHits.ForwardPush(e, delta); // the host's enemy moves too (COS-13)
    }

    public static SpellZone AddZone(SpellZone z)
    {
        z.Next = z.Interval;
        z.CastId = SpellNet.Context;
        SpellNet.NoteMotion(z.Visual, z.Left, Vector3.zero); // the zone, not the visual's placeholder lifetime, decides how long it shows
        Zones.Add(z);
        return z;
    }

    /// <summary>How fast a homing bolt turns, radians per second (the same on every machine).</summary>
    public const float BoltTurnRate = 4f;

    public static SpellBolt AddBolt(SpellBolt b)
    {
        b.Dir.Normalize();
        b.CastId = SpellNet.Context;
        SpellNet.NoteMotion(b.Visual, Mathf.Min(b.Life, b.Speed > 0 ? b.Range / b.Speed : b.Life), b.Dir * b.Speed);
        if (b.Homing && b.Target?.Actor != null) SpellNet.NoteHoming(b.Visual, Net.EnemySyncFeature.NetIdOf(b.Target.Actor));
        Bolts.Add(b);
        return b;
    }

    // ---------------------------------------------------------------- frame

    public static void Step(float dt)
    {
        SpellFx.Step(dt);
        FlickerRun.Step(dt);
        var pc = Pc;
        Buffs.Tick(dt);
        Parry.Tick(dt);
        if (Frenzy.Tick(dt)) Buffs.Remove("frenzy");
        double maxHealth = LocalPlayer.Vitals?.Health != null ? LocalPlayer.Vitals.Health.GetMax() : Derived.MaxHealth(Stats);
        Shield.Tick(dt, maxHealth, Stats);

        if (LocalPlayer.IsInWorld && LocalPlayer.Vitals != null)
        {
            var sustain = pc.Spells.SustainTick(dt, SpellCaster.Energy, Stats);
            SpellCaster.Spend(sustain.Spent);
            foreach (int slot in sustain.Stopped) SpellEffects.Stopped(slot);
            for (int slot = 0; slot < SpellBook.Slots; slot++)
                if (pc.Spells.IsActive(slot) && pc.Spells.Loadout[slot] is string id)
                {
                    SpellNet.Context = SpellNet.ToggleCast(id);
                    SpellEffects.Sustained(id, dt);
                    SpellNet.Context = null;
                }
        }

        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            var z = Zones[i];
            SpellNet.Context = z.CastId;
            z.Frame?.Invoke(z, dt);
            z.Left -= dt;
            z.Next -= dt;
            while (z.Next <= 0 && z.Interval > 0)
            {
                z.Next += z.Interval;
                z.Pulses++;
                z.Pulse?.Invoke(z);
            }
            if (z.Left > 0) continue;
            z.End?.Invoke(z);
            SpellNet.Context = null;
            SpellNet.Expired(z.Visual); // ran its time: the others' copy ends on its own
            SpellFx.Remove(z.Visual);
            Zones.RemoveAt(i);
        }
        SpellNet.Context = null;

        for (int i = Bolts.Count - 1; i >= 0; i--)
        {
            SpellNet.Context = Bolts[i].CastId;
            StepBolt(Bolts[i], dt);
            SpellNet.Context = null;
            if (!Bolts[i].Done) continue;
            SpellFx.Remove(Bolts[i].Visual);
            Bolts.RemoveAt(i);
        }

        SpellPortals.Step(dt);
        SpellNet.Flush();
    }

    private static void StepBolt(SpellBolt b, float dt)
    {
        b.Life -= dt;
        if (b.Life <= 0) { b.Done = true; return; }
        if (b.Homing)
        {
            if (b.Target is not { IsActive: true }) b.Target = Nearest(b.Pos, (float)SpellTuning.SeekingArrowRange);
            if (b.Target != null)
            {
                var want = (Center(b.Target) - b.Pos).normalized;
                b.Dir = Vector3.RotateTowards(b.Dir, want, BoltTurnRate * dt, 0).normalized;
            }
        }
        float step = b.Speed * dt;
        var from = b.Pos;
        var to = from + b.Dir * step;

        // Enemies along this step, nearest first.
        foreach (var e in ActiveEnemies().OrderBy(e => Vector3.Distance(Center(e), from)))
        {
            if (b.Hit.Contains(e)) continue;
            if (DistanceToSegment(Center(e), from, to) > b.Radius) continue;
            b.Hit.Add(e);
            b.OnHit?.Invoke(b, e);
            if (!b.Pierce) { b.Done = true; break; }
        }

        b.Pos = to;
        b.Travelled += step;
        if (b.Visual != null) b.Visual.transform.position = to;
        if (b.Travelled >= b.Range) b.Done = true;
    }

    public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float len = ab.sqrMagnitude;
        float t = len <= 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
        return Vector3.Distance(p, a + ab * t);
    }

    // ---------------------------------------------------------------- reset

    /// <summary>Drops every running spell effect (new world, new character, between self-test phases).</summary>
    public static void Reset()
    {
        foreach (var z in Zones) SpellFx.Remove(z.Visual);
        Zones.Clear();
        foreach (var b in Bolts) SpellFx.Remove(b.Visual);
        Bolts.Clear();
        _buffs?.Clear();
        _buffs = null;
        _buffSheet = null;
        Shield.Reset();
        Parry.Reset();
        Frenzy.Reset();
        SpellPortals.Reset();
        SpellEffects.ResetState();
        SpellNet.Reset();
        SpellFx.Clear();
    }
}
