using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// Placeholder visuals for spells: coloured Unity primitives (spheres, flat discs, columns) that live for a time and then go.
/// Real effects come with the asset pipeline (COS-5). Timers run on the dt the spell world is stepped with, so they work
/// while the game itself is paused (headless self-tests).
/// </summary>
internal static class SpellFx
{
    private sealed class Item
    {
        public GameObject Go = null!;
        public float Left;
        public Transform? Follow;
        public Vector3 Offset;
    }

    private static readonly List<Item> Items = new();
    private static Shader? _unlit;
    private static bool _looked;

    public static int Count => Items.Count;

    public static readonly Color Fire = new(1f, 0.45f, 0.1f), Ice = new(0.55f, 0.85f, 1f), Arcane = new(0.7f, 0.35f, 1f),
        Holy = new(1f, 0.9f, 0.4f), Heal = new(0.4f, 1f, 0.5f), Blood = new(0.75f, 0.05f, 0.1f), Dark = new(0.12f, 0.05f, 0.2f),
        Storm = new(0.6f, 0.8f, 1f), Steel = new(0.8f, 0.85f, 0.95f);

    public static GameObject Sphere(Vector3 pos, float radius, Color color, float seconds, Transform? follow = null) =>
        Make(PrimitiveType.Sphere, pos, Vector3.one * radius * 2, color, seconds, follow);

    /// <summary>A flat disc lying on the ground, <paramref name="radius"/> across to its edge.</summary>
    public static GameObject Disc(Vector3 pos, float radius, Color color, float seconds, Transform? follow = null) =>
        Make(PrimitiveType.Cylinder, pos, new Vector3(radius * 2, 0.04f, radius * 2), color, seconds, follow);

    public static GameObject Column(Vector3 pos, float radius, float height, Color color, float seconds) =>
        Make(PrimitiveType.Cylinder, pos + Vector3.up * height / 2, new Vector3(radius * 2, height / 2, radius * 2), color, seconds, null);

    private static GameObject Make(PrimitiveType type, Vector3 pos, Vector3 scale, Color color, float seconds, Transform? follow)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "cots-spell-fx";
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col); // never block the player, bullets or enemies
        go.transform.position = pos;
        go.transform.localScale = scale;
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var unlit = FindUnlit();
            if (unlit != null)
            {
                var mat = new Material(unlit);
                mat.SetColor("_UnlitColor", color);
                mat.color = color;
                rend.material = mat;
            }
            else
            {
                rend.material.color = color;
            }
        }
        Items.Add(new Item { Go = go, Left = seconds, Follow = follow, Offset = follow != null ? pos - follow.position : Vector3.zero });
        return go;
    }

    /// <summary>Removes one effect early.</summary>
    public static void Remove(GameObject? go)
    {
        if (go == null) return;
        int i = Items.FindIndex(x => x.Go == go);
        if (i >= 0) Items.RemoveAt(i);
        UnityEngine.Object.Destroy(go);
    }

    public static void Step(float dt)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var it = Items[i];
            if (it.Go == null)
            {
                Items.RemoveAt(i);
                continue;
            }
            if (it.Follow != null) it.Go.transform.position = it.Follow.position + it.Offset;
            it.Left -= dt;
            if (it.Left > 0) continue;
            UnityEngine.Object.Destroy(it.Go);
            Items.RemoveAt(i);
        }
    }

    public static void Clear()
    {
        foreach (var it in Items)
            if (it.Go != null) UnityEngine.Object.Destroy(it.Go);
        Items.Clear();
    }

    private static Shader? FindUnlit()
    {
        if (_looked) return _unlit;
        _looked = true;
        _unlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
        return _unlit;
    }
}
