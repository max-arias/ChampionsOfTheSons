using ChampionsOfTheSons.Core.Rules;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>Portal: each cast places one gate; two casts make a linked pair that carries the player between them while it lasts.</summary>
internal static class SpellPortals
{
    private sealed class Pair
    {
        public Vector3 A, B;
        public float Left;
        public GameObject? VisualA, VisualB;
    }

    private static readonly List<Pair> Pairs = new();
    private static float _lock;

    public static int Count => Pairs.Count;
    public static (Vector3 Entrance, Vector3 Exit) LastOpened { get; private set; }

    private static Vector3? _pending;
    private static GameObject? _pendingVisual;
    private static float _pendingLeft;

    /// <summary>A gate is waiting for the second cast.</summary>
    public static bool HasPending => _pending != null;

    /// <summary>One cast places one gate at the aimed point (up to the range). The second cast places the other and links them; the
    /// <paramref name="seconds"/> timer starts then. Returns true when this cast completed a pair.</summary>
    public static bool Place(float seconds)
    {
        var at = SpellWorld.AimGround((float)SpellTuning.PortalMaxRange);
        if (_pending is not Vector3 first)
        {
            _pending = at;
            _pendingLeft = (float)SpellTuning.PortalPendingSeconds;
            _pendingVisual = Gate(at);
            SpellNet.NoteMotion(_pendingVisual, _pendingLeft, Vector3.zero);
            return false;
        }
        // The waiting gate is redrawn with the second, so teammates get the pair from this one cast.
        SpellFx.Remove(_pendingVisual);
        _pending = null;
        _pendingVisual = null;
        var pair = new Pair { A = first, B = at, Left = seconds };
        pair.VisualA = Gate(first);
        pair.VisualB = Gate(at, second: true);
        SpellNet.NoteMotion(pair.VisualA, seconds, Vector3.zero);
        SpellNet.NoteMotion(pair.VisualB, seconds, Vector3.zero);
        Pairs.Add(pair);
        _lock = 0.5f;
        LastOpened = (first, at);
        return true;
    }

    /// <summary>A teammate's Portal (COS-80): the same pair, drawn from their cast, carries the local player too.</summary>
    public static void OpenShared(Vector3 a, Vector3 b, float seconds, GameObject? visualA, GameObject? visualB)
    {
        Pairs.Add(new Pair { A = a, B = b, Left = seconds, VisualA = visualA, VisualB = visualB });
        SharedOpened++;
    }

    /// <summary>Teammates' portal pairs opened here (co-op test).</summary>
    public static int SharedOpened { get; private set; }

    /// <summary>Gate colours (Max, 2026-11-24): the first gate of a pair is blue, the second orange.</summary>
    public static readonly Color FirstGate = new(0.2f, 0.55f, 1f), SecondGate = new(1f, 0.5f, 0.08f);

    private static GameObject Gate(Vector3 at, bool second = false)
    {
        // Face the caster, so the swirl is seen front-on when the pair opens.
        var go = SpellFx.Portal(at, SpellWorld.PlayerPos - at, second ? SecondGate : FirstGate, 1e6f);
        return go;
    }

    public static void Step(float dt)
    {
        _lock = Mathf.Max(0, _lock - dt);
        if (_pending != null && (_pendingLeft -= dt) <= 0)
        {
            SpellFx.Remove(_pendingVisual);
            _pending = null;
            _pendingVisual = null;
        }
        if (Pairs.Count == 0) return;
        for (int i = Pairs.Count - 1; i >= 0; i--)
        {
            var p = Pairs[i];
            p.Left -= dt;
            if (p.Left <= 0)
            {
                SpellFx.Remove(p.VisualA);
                SpellFx.Remove(p.VisualB);
                Pairs.RemoveAt(i);
                continue;
            }
            if (_lock > 0 || !LocalPlayer.IsInWorld) continue;
            var me = SpellWorld.PlayerPos;
            if (Near(me, p.A)) Take(p.B);
            else if (Near(me, p.B)) Take(p.A);
        }
    }

    private static bool Near(Vector3 me, Vector3 gate) =>
        Vector2.Distance(new Vector2(me.x, me.z), new Vector2(gate.x, gate.z)) <= SpellTuning.PortalRadius && Mathf.Abs(me.y - gate.y) < 2.5f;

    private static void Take(Vector3 to)
    {
        _lock = (float)SpellTuning.PortalReenterLock;
        SpellCaster.TeleportPlayer(to + Vector3.up * 0.1f);
    }

    public static void Reset()
    {
        foreach (var p in Pairs)
        {
            SpellFx.Remove(p.VisualA);
            SpellFx.Remove(p.VisualB);
        }
        Pairs.Clear();
        SpellFx.Remove(_pendingVisual);
        _pending = null;
        _pendingVisual = null;
        _lock = 0;
    }
}
