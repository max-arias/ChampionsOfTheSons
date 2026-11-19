using ChampionsOfTheSons.Core.Rules;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>Portal: a linked pair of gates that carry the player between them while they last.</summary>
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

    /// <summary>Opens the entrance a few metres ahead of the player and the exit where the view ray lands (up to the range).</summary>
    public static (Vector3 Entrance, Vector3 Exit) Open(float seconds)
    {
        var eye = SpellWorld.Eye;
        var flat = new Vector3(eye.forward.x, 0, eye.forward.z).normalized;
        var a = SpellWorld.PlayerPos + flat * 3f;
        var b = SpellWorld.AimGround((float)SpellTuning.PortalMaxRange);
        if (Vector3.Distance(a, b) < 8f) b = SpellWorld.PlayerPos + flat * (float)SpellTuning.PortalMaxRange;
        if (SpellCaster.GroundY(b + Vector3.up * 3) is float gy) b.y = gy;
        var pair = new Pair { A = a, B = b, Left = seconds };
        pair.VisualA = Gate(a);
        pair.VisualB = Gate(b);
        Pairs.Add(pair);
        _lock = 0.5f;
        LastOpened = (a, b);
        return (a, b);
    }

    private static GameObject Gate(Vector3 at)
    {
        var go = SpellFx.Column(at, 0.9f, 2.4f, SpellFx.Arcane, 1e6f);
        return go;
    }

    public static void Step(float dt)
    {
        _lock = Mathf.Max(0, _lock - dt);
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
        _lock = 0;
    }
}
