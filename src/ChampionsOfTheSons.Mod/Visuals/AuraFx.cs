using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// A ring of small flames standing on the ground around a point (COS-179: Black Flame's area, Max 2026-11-23: no disc, no border,
/// flames along the edge that follow the terrain). Each flame is moved every frame to the ground under its spot, so the ring
/// climbs slopes and steps. The same ring is drawn for the local player and for teammates (everyone sees the same thing).
/// </summary>
internal sealed class FlameRing
{
    public const int Count = 24;
    private static readonly Color Violet = new(0.55f, 0.2f, 1f);

    private readonly GameObject _root;
    private readonly GameObject?[] _flames = new GameObject?[Count];

    public FlameRing(Color? color = null)
    {
        _root = new GameObject("cots-flame-ring");
        for (int i = 0; i < Count; i++)
        {
            var go = FxKit.Spawn(FxKit.Flame, Vector3.zero, 0.22f, color ?? Violet, essential: true);
            if (go == null) continue;
            go.transform.SetParent(_root.transform, true);
            _flames[i] = go;
        }
    }

    public bool Alive => _root != null;

    /// <summary>Puts every flame on the ground at its place around <paramref name="center"/>.</summary>
    public void Update(Vector3 center, float radius)
    {
        if (_root == null) return;
        for (int i = 0; i < Count; i++)
        {
            var f = _flames[i];
            if (f == null) continue;
            float a = i * Mathf.PI * 2 / Count;
            var spot = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
            f.transform.position = Ground(spot);
        }
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

    /// <summary>Flames that exist (self-tests).</summary>
    public int Flames => _flames.Count(f => f != null);
}

/// <summary>Self buffs (COS-181): no ground ring; a small aura of flames on the body, tinted per buff.</summary>
internal static class BodyAura
{
    public static GameObject? On(Transform body, Color color)
    {
        var go = FxKit.Spawn(FxKit.Flame, body.position + Vector3.up * 1.0f, new Vector3(0.6f, 1.2f, 0.6f), color, body);
        if (go != null) FxKit.AddLight(go, color, 2, 4);
        return go;
    }
}
