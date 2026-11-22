using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Visuals;
using TheForest.Utils;
using UnityEngine;
using static ChampionsOfTheSons.Mod.Spells.SpellWorld;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// Flicker Strike (COS-141). The cast picks the route (<see cref="FlickerStrikeRules.Route"/>); this runs it frame by frame:
/// a hop beside each enemy (frozen until the payoff), back to the start, a short beat, then every visited enemy is hit in the
/// same frame. The caster takes no damage, stun or fall damage while it runs. Hits go through <see cref="SpellWorld.Strike"/>,
/// so damage numbers, perks and co-op forwarding work as for any spell.
/// </summary>
internal static class FlickerRun
{
    private enum Phase { Idle, Hopping, Beat }

    private static Phase _phase;
    private static readonly List<EnemyRuntime> Targets = new();
    private static int _next;
    private static float _timer, _hopPause;
    private static Vector3 _origin, _lastSpot;
    private static Quaternion _originLook;
    private static uint? _castId;

    /// <summary>True from the first hop until the caster stands back at the start: no damage, stun or fall damage.</summary>
    public static bool Untouchable => _phase == Phase.Hopping;

    public static bool Running => _phase != Phase.Idle;

    /// <summary>Self-test: the targets of the last run and the frame their payoff landed in.</summary>
    public static int LastTargets { get; private set; }
    public static int LastPayoffFrame { get; private set; } = -1;
    public static Vector3 LastOrigin { get; private set; }

    /// <summary>Living enemies the caster can reach from here: within range and in line of sight (nothing through terrain).</summary>
    public static List<EnemyRuntime> Reachable()
    {
        var eye = Eye.position;
        var inRange = EnemiesWithin(PlayerPos, FlickerStrikeRules.Range).Where(e => e.Actor != null).ToList();
        var near = inRange.Where(e => InSight(eye, e)).ToList();
        LastReach = (inRange.Count, near.Count);
        var route = FlickerStrikeRules.Route(ToNum(PlayerPos), near.Select(e => ToNum(Center(e))).ToList());
        return route.Select(i => near[i]).ToList();
    }

    private static System.Numerics.Vector3 ToNum(Vector3 v) => new(v.x, v.y, v.z);

    /// <summary>Self-test: whether the spell would see this enemy from where the player stands now.</summary>
    public static bool Sees(EnemyRuntime e) => e.Actor != null && InSight(Eye.position, e);

    /// <summary>Nothing of the world (terrain, rocks, buildings) between the eye and the enemy. The player's own colliders and
    /// enemies (this one or others in the way) do not block.</summary>
    private static bool InSight(Vector3 eye, EnemyRuntime e)
    {
        var to = Center(e);
        var dir = to - eye;
        float dist = dir.magnitude;
        if (dist < 0.5f) return true;
        foreach (var hit in Physics.RaycastAll(eye, dir / dist, dist).OrderBy(h => h.distance))
        {
            var t = hit.transform;
            if (t == null) continue;
            // Trigger volumes (zones, interaction areas) are not walls: standing in one hid every enemy (2026-11-22).
            if (hit.collider != null && hit.collider.isTrigger) continue;
            if (LocalPlayer.Transform != null && t.IsChildOf(LocalPlayer.Transform)) continue;
            if (t.GetComponentInParent<Sons.Ai.Vail.VailActor>() != null) continue; // an enemy's body (this one or another)
            if (hit.distance >= dist - 1f) return true; // something right at the enemy
            return false;
        }
        return true;
    }

    /// <summary>Self-test: enemies in range and in sight at the last check.</summary>
    public static (int InRange, int InSight) LastReach { get; private set; }

    /// <summary>Starts a run on the given route (already checked non-empty by the cast's precondition).</summary>
    public static bool Start(List<EnemyRuntime> route)
    {
        if (route.Count == 0 || LocalPlayer.Transform == null) return false;
        Targets.Clear();
        Targets.AddRange(route);
        _next = 0;
        _timer = 0;
        _hopPause = (float)FlickerStrikeRules.HopPause(route.Count);
        _origin = PlayerPos;
        _lastSpot = _origin;
        _originLook = LocalPlayer.Transform.rotation;
        _castId = SpellNet.Context;
        LastTargets = route.Count;
        LastOrigin = _origin;
        // Frozen from now until the payoff: they "haven't noticed yet".
        float frozen = (float)(route.Count * _hopPause + FlickerStrikeRules.Beat + 0.3);
        foreach (var e in route) e.Combat.ApplyStun(frozen);
        _phase = Phase.Hopping;
        return true;
    }

    public static void Step(float dt)
    {
        if (_phase == Phase.Idle) return;
        if (!LocalPlayer.IsInWorld || LocalPlayer.Transform == null) { Cancel(); return; }
        var saved = SpellNet.Context;
        SpellNet.Context = _castId;
        try
        {
            _timer -= dt;
            if (_timer > 0) return;
            if (_phase == Phase.Hopping)
            {
                if (_next < Targets.Count) Hop(Targets[_next++]);
                else Return();
            }
            else Payoff();
        }
        finally
        {
            SpellNet.Context = saved;
        }
    }

    private static void Hop(EnemyRuntime e)
    {
        _timer = _hopPause;
        if (!e.IsActive || e.Actor == null) return; // died or despawned mid-run: skipped
        var center = Center(e);
        var from = _lastSpot;
        var away = from - center;
        away.y = 0;
        if (away.sqrMagnitude < 0.01f) away = -LocalPlayer.Transform.forward;
        var spot = Ground(center + away.normalized * 1.3f);
        SpellFx.Sphere(from + Vector3.up * 1f, 0.45f, SpellFx.Steel, 0.6f); // the afterimage left behind
        SpellFx.Column((from + spot) / 2, 0.06f, 0.1f, SpellFx.Steel, 0.25f); // the streak between hops
        SpellCaster.TeleportPlayer(spot);
        var look = center - spot;
        look.y = 0;
        if (look.sqrMagnitude > 0.01f) LocalPlayer.Transform.rotation = Quaternion.LookRotation(look);
        Audio.ModAudio.PlayAt("swing_blade", spot, 30f, 0.45f, 1.25f);
        _lastSpot = spot;
    }

    private static void Return()
    {
        SpellFx.Sphere(_lastSpot + Vector3.up * 1f, 0.45f, SpellFx.Steel, 0.6f);
        SpellCaster.TeleportPlayer(Ground(_origin));
        LocalPlayer.Transform.rotation = _originLook;
        _phase = Phase.Beat;
        _timer = (float)FlickerStrikeRules.Beat;
        ScreenFx.Beat(_timer + 0.05f); // the "already cut" moment: desaturate, vignette, game sound ducked (local only)
    }

    private static void Payoff()
    {
        int alive = 0;
        double combo = FlickerStrikeRules.Combo(Targets.Count);
        foreach (var e in Targets)
        {
            if (!e.IsActive || e.Actor == null) continue;
            var hit = SpellWorld.Hit(FlickerStrikeRules.BaseDamage, FlickerStrikeRules.Scaling, canCrit: true);
            Strike(e, hit with { Amount = hit.Amount * combo });
            var c = Center(e);
            FxKit.Spawn(FxKit.Burst, c, hit.Crit ? 1.8f : 1.1f, hit.Crit ? Color.white : SpellFx.Steel);
            SpellFx.Column(c, hit.Crit ? 0.09f : 0.05f, hit.Crit ? 2.4f : 1.6f, hit.Crit ? Color.white : SpellFx.Steel, 0.35f); // the slash
            alive++;
        }
        ScreenFx.Release();
        if (alive > 0) Audio.ModAudio.PlayAt("impact_metal", _origin, 40f);
        LastPayoffFrame = Time.frameCount;
        Cancel();
    }

    public static void Cancel()
    {
        if (_phase == Phase.Beat) ScreenFx.Release();
        _phase = Phase.Idle;
        Targets.Clear();
        _castId = null;
    }

    private static Vector3 Ground(Vector3 at)
    {
        foreach (var h in Physics.RaycastAll(at + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (h.transform != null && LocalPlayer.Transform != null && h.transform.IsChildOf(LocalPlayer.Transform)) continue;
            if (EnemyRegistry.Live.Any(e => e.Actor != null && h.transform.IsChildOf(e.Actor.transform))) continue;
            return h.point + Vector3.up * 0.05f;
        }
        return at;
    }
}
