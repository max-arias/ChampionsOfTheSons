using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Loot;

/// <summary>World objects for ground drops, built from primitives: a gem, a vertical beam and a light in the rarity's color.</summary>
internal static class LootVisuals
{
    internal sealed class Visual
    {
        public GameObject Root = null!;
        public Transform Gem = null!;
        public Color Color;
        public Rarity Rarity;
        public Vector3 Position;
    }

    private static readonly Dictionary<int, Visual> ById = new();
    private static Shader? _unlit;
    private static bool _shaderLooked;

    public static IReadOnlyDictionary<int, Visual> All => ById;

    public static Color ColorOf(Rarity r) => ColorUtility.TryParseHtmlString(UiTheme.RarityHex(r), out var c) ? c : Color.white;

    public static void Create(GroundDrop drop)
    {
        Destroy(drop.Id);
        var pos = new Vector3(drop.X, drop.Y, drop.Z);
        var color = ColorOf(drop.Item.Rarity);
        int rarity = (int)drop.Item.Rarity;

        var root = new GameObject("cots-drop-" + drop.Id);
        root.transform.position = pos;

        // The bundled marker mesh and sparkle effect when the asset bundle is present (COS-5), else a primitive gem.
        var gem = Bundles.CotsBundle.Spawn(Bundles.CotsBundle.LootMarker, root.transform);
        if (gem != null)
        {
            Tint(gem, color);
            gem.transform.localPosition = new Vector3(0, 0.4f, 0);
            gem.transform.localScale = Vector3.one * (1f + 0.1f * rarity);
        }
        else
        {
            gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Prepare(gem, root.transform, color);
            gem.transform.localPosition = new Vector3(0, 0.4f, 0);
            gem.transform.localScale = Vector3.one * (0.22f + 0.03f * rarity);
            gem.transform.localRotation = Quaternion.Euler(35, 45, 0);
        }
        var sparkle = Bundles.CotsBundle.Spawn(Bundles.CotsBundle.LootSparkle, root.transform);
        if (sparkle != null)
        {
            sparkle.transform.localPosition = new Vector3(0, 0.4f, 0);
            foreach (var ps in sparkle.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(color);
            }
        }

        float length = 3f + 0.6f * rarity;
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Prepare(beam, root.transform, color);
        beam.transform.localScale = new Vector3(0.09f, length / 2f, 0.09f); // a cylinder is 2 m tall at scale 1
        beam.transform.localPosition = new Vector3(0, length / 2f, 0);

        var lightObj = new GameObject("light");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = new Vector3(0, 0.8f, 0);
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 5;
        light.intensity = 40;

        ById[drop.Id] = new Visual { Root = root, Gem = gem.transform, Color = color, Rarity = drop.Item.Rarity, Position = pos };
    }

    public static void Destroy(int id)
    {
        if (!ById.Remove(id, out var v)) return;
        if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
    }

    public static void Spin(float dt)
    {
        foreach (var v in ById.Values)
            if (v.Gem != null) v.Gem.Rotate(0, 90 * dt, 0, Space.World);
    }

    /// <summary>Rarity color on a bundled mesh (HDRP/Lit "_BaseColor", other shaders "_Color").</summary>
    private static void Tint(GameObject go, Color color)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var m = r.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            else m.color = color;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(c);
    }

    private static void Prepare(GameObject go, Transform parent, Color color)
    {
        go.transform.SetParent(parent, false);
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col); // never block the player or bullets
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
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

    private static Shader? FindUnlit()
    {
        if (_shaderLooked) return _unlit;
        _shaderLooked = true;
        _unlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
        return _unlit;
    }
}
