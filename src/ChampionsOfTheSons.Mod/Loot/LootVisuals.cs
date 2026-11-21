using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Loot;

/// <summary>
/// World objects for ground drops: a grey-metal model of the item's type from the asset bundle (COS-63; a rarity-tinted
/// gem when the bundle or model is missing), rising sparks, a vertical beam and a light in the rarity's colour.
/// </summary>
internal static class LootVisuals
{
    internal sealed class Visual
    {
        public GameObject Root = null!;
        public Transform Gem = null!;
        public Color Color;
        public Rarity Rarity;
        public Vector3 Position;
        /// <summary>The root's scale when created (the fade shrinks from it).</summary>
        public float Scale = 1;
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
        // Size and beam grow with rarity on the old 0..7 scale, so a Legendary drop is as big as a Primal one was (COS-109).
        float rarity = (int)drop.Item.Rarity * 7f / Rarities.Max;
        const float DropScale = 0.9f; // Max (2026-11-20): drops 10% smaller

        var root = new GameObject("cots-drop-" + drop.Id);
        root.transform.position = pos;

        // The item type's model when the asset bundle has it (COS-63), else the bundled crystal marker (COS-5), else a primitive gem.
        GameObject? gem = null;
        if (DropPrefabFor(drop.Item.BaseId) is string model && Bundles.CotsBundle.Spawn(model, root.transform) is GameObject m)
        {
            gem = m;
            foreach (var r in gem.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gem.transform.localPosition = new Vector3(0, 0.6f, 0);
            gem.transform.localScale = Vector3.one * (DropScale * (1f + 0.05f * rarity));
            gem.transform.localRotation = Quaternion.Euler(0, (drop.Id * 47) % 360, 0);
            // Top-rarity and unique weapons glow on the ground as in hand (COS-108); the drop has its own light.
            Weapons.WeaponGlowFx.Apply(gem, Weapons.GlowLook.For(drop.Item), light: false);
        }
        if (gem == null) gem = Bundles.CotsBundle.Spawn(Bundles.CotsBundle.LootMarker, root.transform);
        if (gem != null && gem.name == Bundles.CotsBundle.LootMarker)
        {
            Tint(gem, color);
            gem.transform.localPosition = new Vector3(0, 0.4f, 0);
            gem.transform.localScale = Vector3.one * (DropScale * (1f + 0.1f * rarity));
        }
        else if (gem == null)
        {
            gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Prepare(gem, root.transform, color);
            gem.transform.localPosition = new Vector3(0, 0.4f, 0);
            gem.transform.localScale = Vector3.one * (DropScale * (0.22f + 0.03f * rarity));
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

    /// <summary>Every drop model prefab (self-tests).</summary>
    public static readonly string[] DropPrefabs =
    {
        "Drop_Sword", "Drop_GreatSword", "Drop_Axe", "Drop_Hammer", "Drop_Bow", "Drop_Polearm", "Drop_Shield",
        "Drop_Quiver", "Drop_Scroll", "Drop_Armor", "Drop_Jewelry", "Drop_Material", "Drop_Consumable",
    };

    /// <summary>
    /// The bundled model for an item: weapons by their weapon model, the rest by item type. No armor or jewelry models
    /// exist in the free sets we use, so armor drops show a chest, jewelry a coin, spell scrolls a key, materials a small
    /// chest and consumables (hearts) a flask.
    /// </summary>
    public static string? DropPrefabFor(string baseId)
    {
        if (!ModContent.Db.Items.TryGetValue(baseId, out var def)) return null;
        return def.Type switch
        {
            "weapon" => def.WeaponModel switch
            {
                "great_sword" => "Drop_GreatSword",
                "axe" => "Drop_Axe",
                "hammer" => "Drop_Hammer",
                "greatbow" => "Drop_Bow",
                "polearm" => "Drop_Polearm",
                _ => "Drop_Sword",
            },
            "shield" => "Drop_Shield",
            "quiver" => "Drop_Quiver",
            "spell_scroll" => "Drop_Scroll",
            "helmet" or "chest_armor" or "pants" or "boot" or "glove" or "shoulder_armor" or "bracer" => "Drop_Armor",
            "ring" or "amulet" => "Drop_Jewelry",
            "material" => "Drop_Material",
            "other" => "Drop_Consumable",
            _ => null,
        };
    }

    public static void Destroy(int id)
    {
        if (!ById.Remove(id, out var v)) return;
        if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
    }

    /// <summary>Seconds over which a drop shrinks away before it despawns (COS-96).</summary>
    public const float FadeSeconds = 2f;

    /// <summary>Shrinks drops in their last <see cref="FadeSeconds"/>, so they fade out instead of popping away.</summary>
    public static void Fade(GroundLoot ground)
    {
        foreach (var (id, v) in ById)
        {
            if (v.Root == null) continue;
            var drop = ground.Get(id);
            if (drop == null || drop.Remaining >= FadeSeconds) continue;
            v.Root.transform.localScale = Vector3.one * Mathf.Max(0.05f, (float)drop.Remaining / FadeSeconds) * v.Scale;
        }
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
