using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// Black Flame's area (Max, 2026-11-24): flames of mixed sizes all over the radius, each standing on the ground under its spot
/// (COS-179), and one large, faint flame engulfing the caster. No disc, no border. The same field is drawn for the local
/// player and for teammates. A third of the flames are re-grounded each frame, so the field climbs slopes and steps as it moves.
/// </summary>
internal sealed class FlameRing
{
    public const int Count = 44;
    private static readonly Color Violet = new(0.55f, 0.2f, 1f);
    private const float Golden = 2.39996323f;   // the golden angle: spots spread evenly over the disc

    private readonly GameObject _root;
    private readonly GameObject?[] _flames = new GameObject?[Count];
    private readonly GameObject? _engulf;
    private bool _placed;

    public FlameRing(Color? color = null)
    {
        _root = new GameObject("cots-flame-ring");
        for (int i = 0; i < Count; i++)
        {
            var go = FxKit.Spawn(FxKit.Flame, Vector3.zero, Size(i), color ?? Violet, essential: true);
            if (go == null) continue;
            go.transform.SetParent(_root.transform, true);
            _flames[i] = go;
        }
        _engulf = FxKit.Spawn(FxKit.Flame, Vector3.zero, 1.6f, color ?? Violet, essential: true);
        if (_engulf != null)
        {
            _engulf.transform.SetParent(_root.transform, true);
            FxKit.Fade(_engulf, 0.3f);
        }
    }

    /// <summary>A fixed size per flame, small to large (0.35 to 1.15), mixed across the field.</summary>
    private static float Size(int i) => 0.35f + 0.8f * ((i * 7 + 3) % 11) / 10f;

    public bool Alive => _root != null;

    /// <summary>Puts the flames on the ground at their places inside <paramref name="radius"/> around <paramref name="center"/>.</summary>
    public void Update(Vector3 center, float radius)
    {
        if (_root == null) return;
        int turn = Time.frameCount % 3;
        for (int i = 0; i < Count; i++)
        {
            var f = _flames[i];
            if (f == null || (_placed && i % 3 != turn)) continue;
            float r = radius * Mathf.Sqrt((i + 0.5f) / Count), a = i * Golden;
            f.transform.position = Ground(center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
        }
        if (_engulf != null) _engulf.transform.position = Ground(center);
        _placed = true;
    }

    /// <summary>The ground under a spot: a ray down from 3 m above the caster's height; the spot itself when nothing is hit.</summary>
    public static Vector3 Ground(Vector3 spot)
    {
        var from = spot + Vector3.up * 3f;
        return Physics.Raycast(from, Vector3.down, out var hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point : spot;
    }

    public void Destroy()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    /// <summary>Flames in the field (self-tests).</summary>
    public int Flames => _flames.Count(f => f != null);
}

/// <summary>Self buffs (COS-181): no ground ring; an aura rising around the body, tinted per buff (flames without the pack aura).</summary>
internal static class BodyAura
{
    public static GameObject? On(Transform body, Color color)
    {
        var go = FxKit.Spawn(FxKit.AuraBuff, body.position, Vector3.one, color, body)
            ?? FxKit.Spawn(FxKit.Flame, body.position + Vector3.up * 1.0f, new Vector3(0.6f, 1.2f, 0.6f), color, body);
        if (go != null) FxKit.AddLight(go, color, 2, 4);
        return go;
    }
}
