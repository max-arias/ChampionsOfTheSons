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
    public bool Homing;
    /// <summary>Flies on through every enemy it touches (each at most once) instead of stopping at the first.</summary>
    public bool Pierces;
    public EnemyRuntime? Target;
    public Action<SpellBolt, EnemyRuntime>? OnHit;
    /// <summary>COS-213: the bolt's hit on everything that is not a tracked enemy (animals it touches, and the world where it ends).</summary>
    public Func<OutgoingHit>? WorldHit;
    /// <summary>Radius of the world damage where the bolt ends (COS-213).</summary>
    public float ImpactRadius = 1.5f;
    /// <summary>The bolt ended at a point: on an enemy it stopped at, an animal, or solid world (COS-213: Firebolt explodes here).</summary>
    public Action<SpellBolt, Vector3>? OnImpact;
    public readonly HashSet<IntPtr> HitActors = new();
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
    /// <summary>Where an aimed area spell lands: the aimed point brought down to the ground. COS-178: the terrain under it wins over
    /// anything higher (tree canopies caught spells in the treetops, where their visuals hung in the air).</summary>
    public static Vector3 AimGround(float range)
    {
        var p = AimPoint(range);
        if (SpellCaster.TerrainY(p + Vector3.up * 3) is float ty) p.y = ty;
        else if (SpellCaster.GroundY(p + Vector3.up * 3) is float gy && gy <= p.y + 4) p.y = gy;
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

    /// <summary>
    /// COS-213: a spell's damage on everything in a sphere. Tracked enemies take <paramref name="hit"/> through <see cref="Strike"/>
    /// (with <paramref name="onEnemy"/> after each, for slows and the like); animals, plants, trees and buildings through
    /// <see cref="WorldDamage.Area"/>. Returns the enemies struck.
    /// </summary>
    public static List<EnemyRuntime> StrikeAll(Vector3 center, float radius, Func<OutgoingHit> hit, Action<EnemyRuntime>? onEnemy = null, Vector3? pullTo = null)
    {
        var struck = EnemiesWithin(center, radius);
        foreach (var e in struck)
        {
            Strike(e, hit());
            onEnemy?.Invoke(e);
        }
        WorldDamage.Area(center, radius, hit(), pullTo);
        return struck;
    }

    /// <summary>Moves an enemy (pull, knockback) by shifting its transform.</summary>
    public static void Push(EnemyRuntime e, Vector3 delta)
    {
        if (!e.IsActive || delta == Vector3.zero) return;
        e.Actor!.transform.position += delta;
        if (Net.CoopHits.ForwardsToHost) Net.CoopHits.ForwardPush(e, delta); // the host's enemy moves too (COS-13)
    }

    /// <summary>
    /// Knockback (COS-90): the enemy slides the way a push goes, fast at first and slowing to a stop, instead of jumping.
    /// Juggernaut takes none. A co-op client's knockback goes to the host at once (its enemy moves there as a whole jump).
    /// </summary>
    public static void Knock(EnemyRuntime e, Vector3 delta)
    {
        if (!e.IsActive || delta == Vector3.zero || e.Combat.CcImmune) return;
        if (Net.CoopHits.ForwardsToHost)
        {
            Push(e, delta);
            return;
        }
        Slides.Add((e, delta));
    }

    /// <summary>Knockbacks still sliding: the enemy and the distance left to go.</summary>
    private static readonly List<(EnemyRuntime Enemy, Vector3 Left)> Slides = new();

    /// <summary>How fast a knockback slows: the share of the remaining distance covered per second is <c>1 - e^-rate</c>.</summary>
    private const float SlideRate = 7f;

    /// <summary>Self-tests: knockbacks still sliding.</summary>
    internal static int SlidingCount => Slides.Count;

    private static void StepSlides(float dt)
    {
        for (int i = Slides.Count - 1; i >= 0; i--)
        {
            var (e, left) = Slides[i];
            if (!e.IsActive || left.sqrMagnitude < 0.0025f)
            {
                Slides.RemoveAt(i);
                continue;
            }
            var step = left * (1 - Mathf.Exp(-SlideRate * dt));
            e.Actor!.transform.position += step;
            Slides[i] = (e, left - step);
        }
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
            SpellEffects.Sustained(dt);
        }
        SpellHits.Tick(dt);

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

        StepSlides(dt);
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

        // Solid world along this step (COS-213): the bolt ends there. Enemies and animals are caught by distance below.
        float wall = float.MaxValue;
        if (WorldBlock(from, to - from, step) is float w) wall = w;
        if (wall < float.MaxValue) to = from + b.Dir * wall;

        // Enemies along this step, nearest first.
        foreach (var e in ActiveEnemies().OrderBy(e => Vector3.Distance(Center(e), from)))
        {
            if (b.Hit.Contains(e)) continue;
            if (DistanceToSegment(Center(e), from, to) > b.Radius) continue;
            b.Hit.Add(e);
            b.OnHit?.Invoke(b, e);
            if (SelfTest.Enabled) LastBlock = $"enemy {e.Kind.Id}";
            if (!b.Pierces) { End(b, Center(e)); return; } // a bolt stops at the first enemy unless it pierces
        }

        // Animals and companions along the step (COS-213): hit like enemies, once each.
        if (b.WorldHit != null)
            foreach (var a in WorldDamage.ActorsWithin(Vector3.Lerp(from, to, 0.5f), step / 2 + b.Radius + 1))
            {
                var c = a.transform.position + Vector3.up * 0.6f;
                if (DistanceToSegment(c, from, to) > b.Radius || !b.HitActors.Add(a.Pointer)) continue;
                if (SelfTest.Enabled) LastBlock = $"actor {a.TypeId} ({a.name})";
                WorldDamage.Strike(a, b.WorldHit().Amount, from);
                if (!b.Pierces) { End(b, c); return; }
            }

        b.Pos = to;
        b.Travelled += step;
        if (b.Visual != null) b.Visual.transform.position = to;
        if (wall < float.MaxValue) { End(b, to); return; }
        if (b.Travelled >= b.Range) b.Done = true;
    }

    /// <summary>Self-tests: what the last world block was.</summary>
    internal static string LastBlock = "";

    /// <summary>A bolt ends at <paramref name="at"/>: its world damage there, then the spell's own impact.</summary>
    private static void End(SpellBolt b, Vector3 at)
    {
        if (SelfTest.Enabled) SelfTest.Log($"bolt {b.Id} ended after {b.Travelled:0.#} m at {at}, last block {LastBlock}");
        b.Done = true;
        b.Pos = at;
        if (b.Visual != null) b.Visual.transform.position = at;
        if (b.WorldHit != null) WorldDamage.Area(at, b.ImpactRadius, b.WorldHit());
        b.OnImpact?.Invoke(b, at);
    }

    /// <summary>Distance to the first solid, non-actor collider along a ray (the caster ignored), if any within <paramref name="length"/>.</summary>
    public static float? WorldBlock(Vector3 from, Vector3 dir, float length)
    {
        if (length <= 0 || dir.sqrMagnitude < 1e-8f) return null;
        var self = LocalPlayer.Transform != null ? LocalPlayer.Transform.root : null;
        float best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(from, dir.normalized, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (h.distance >= best || h.collider == null) continue;
            if (self != null && h.collider.transform.IsChildOf(self)) continue;
            if (h.collider.GetComponentInParent<Sons.Ai.Vail.VailActor>() != null) continue;
            best = h.distance;
            if (SelfTest.Enabled) LastBlock = $"{h.collider.name} (layer {LayerMask.LayerToName(h.collider.gameObject.layer)}, root {h.collider.transform.root.name}) at {h.distance:0.##} m";
        }
        return best < float.MaxValue ? best : null;
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
        SpellHits.Reset();
        SpellPortals.Reset();
        WorldDamage.Reset();
        Slides.Clear();
        SpellEffects.ResetState();
        SpellNet.Reset();
        SpellFx.Clear();
    }
}
