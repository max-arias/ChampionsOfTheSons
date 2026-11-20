#nullable enable
// Ground-drop models (COS-63) and held-weapon models for the weapon-type work (COS-93), plus the loot marker and
// sparkle. Sources (Assets/ThirdParty, credited in CREDITS.md): Low Poly Weapon Pack (Kickin It Studios) for the
// weapons, KayKit Fantasy Weapons Bits (CC0) for the shield, Deepvein Mine Kit (CC0) for the chest, key and coin
// fallbacks. Every model gets one cold grey-metal look, like the original mod's single metallic drop material: the
// sources' colour slots and texture maps are remapped to greys here, nothing is painted by hand.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CotsLootPrefabs
{
    private const string Src = "Assets/ThirdParty";
    private static readonly Color SteelTint = new(0.93f, 0.96f, 1f);

    // Grey-metal set for the flat-coloured Kickin It models, keyed by their material names.
    private static Dictionary<string, Material> _slots = new();
    private static Material _kaykit = null!;

    public static void Generate()
    {
        MakeLootMarker();
        MakeLootSparkle();

        _slots = new Dictionary<string, Material>
        {
            ["Metal"] = CotsGen.Lit("GreyMetal_Bright", Grey(0.62f), 1f, 0.62f),
            ["Dark Metal"] = CotsGen.Lit("GreyMetal_Dark", Grey(0.22f), 1f, 0.5f),
            ["Wood"] = CotsGen.Lit("GreyMetal_Grip", Grey(0.16f), 0.55f, 0.32f),
            ["Chord"] = CotsGen.Lit("GreyMetal_Wrap", Grey(0.3f), 0.4f, 0.28f),
            ["Bone"] = CotsGen.Lit("GreyMetal_Pale", Grey(0.75f), 0.7f, 0.5f),
        };
        _kaykit = CotsGen.Lit("GreyMetal_KayKit", Color.white, 0.85f, 0.5f, GreyTexture($"{Src}/KayKit/weapons_bits_texture.png", "KayKitGrey", 256));

        // Held weapons (COS-93): real size, origin at the end of the grip, +Y toward the blade or head, +Z out of the flat.
        Weapon("Weapon_Sword", "Arming_Sword", 1.05f);
        Weapon("Weapon_GreatSword", "Great_Sword", 1.6f);
        Weapon("Weapon_Axe", "Bearded_Axe", 0.95f);
        Weapon("Weapon_Hammer", "War_Hammer", 1.1f);
        Weapon("Weapon_Spear", "Spear", 2.0f);
        Weapon("Weapon_Bow", "Bow", 1.4f, alongX: true);
        Weapon("Weapon_Polearm", "Halberd", 2.1f);
        Weapon("Weapon_GreatAxe", "Double_Axe", 1.2f);
        Weapon("Weapon_GreatHammer", "Double_Hammer", 1.4f);

        // Ground drops: centred on the origin (the mod spins them about it), longest side DropSize, tilted to read in profile.
        Drop("Drop_Sword", Kickin("Arming_Sword"), 0.95f, new Vector3(0, 0, 25));
        Drop("Drop_GreatSword", Kickin("Great_Sword"), 1.15f, new Vector3(0, 0, 25));
        Drop("Drop_Axe", Kickin("Bearded_Axe"), 0.85f, new Vector3(0, 0, 25));
        Drop("Drop_Hammer", Kickin("War_Hammer"), 0.9f, new Vector3(0, 0, 25));
        Drop("Drop_Polearm", Kickin("Halberd"), 1.25f, new Vector3(0, 0, 30));
        Drop("Drop_Bow", Kickin("Bow"), 1f, new Vector3(0, 0, 75));
        Drop("Drop_Shield", KayKit("shield_B"), 0.7f, Vector3.zero);
        Drop("Drop_Quiver", Quiver(), 0.8f, Vector3.zero);
        Drop("Drop_Armor", Deepvein("Chest", "chest", new[] { "m_chest", "m_chest_lid", "m_chest_lock", "m_chest_hinges" }), 0.6f, new Vector3(0, 20, 0));
        Drop("Drop_Material", Deepvein("Chest", "chest", new[] { "m_chest", "m_chest_lid", "m_chest_lock", "m_chest_hinges" }), 0.45f, new Vector3(0, 20, 0));
        Drop("Drop_Jewelry", Deepvein("Props", "coin_a", new[] { "m_coin_a" }), 0.3f, new Vector3(0, 0, 0));
        Drop("Drop_Scroll", Deepvein("Props", "key_a", new[] { "m_key_a" }), 0.45f, new Vector3(0, 0, 0));
        Drop("Drop_Consumable", Deepvein("Props", "potion_a", new[] { "m_potion_a", "m_potion_a_cork" }, extra: new() { ["m_potion_a_cork"] = "cork_a" }), 0.4f, Vector3.zero);
    }

    private static Color Grey(float v) => new(v * SteelTint.r, v * SteelTint.g, v * SteelTint.b, 1);

    // ---- sources ----

    /// <summary>A Kickin It model: one mesh with a sub-mesh per colour slot, stored in centimetres-ish units, +Y = head.</summary>
    private static GameObject Kickin(string file)
    {
        var path = $"{Src}/KickinIt/{file}.fbx";
        var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First();
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var srcMats = fbx.GetComponentInChildren<Renderer>().sharedMaterials;
        var root = new GameObject(file);
        var child = CotsGen.Child(root, "model", mesh, _slots["Metal"]);
        child.GetComponent<MeshRenderer>().sharedMaterials = srcMats.Select(m => _slots.TryGetValue(m.name, out var g) ? g : _slots["Metal"]).ToArray();
        return root;
    }

    private static GameObject KayKit(string file)
    {
        var mesh = AssetDatabase.LoadAllAssetsAtPath($"{Src}/KayKit/{file}.fbx").OfType<Mesh>().First();
        var root = new GameObject(file);
        CotsGen.Child(root, "model", mesh, _kaykit);
        return root;
    }

    /// <summary>Three arrows fanned out, standing on their fletchings: the quiver drop.</summary>
    private static GameObject Quiver()
    {
        var root = new GameObject("quiver");
        for (int i = 0; i < 3; i++)
        {
            var arrow = Kickin("Broadhead_Arrow");
            arrow.transform.SetParent(root.transform, false);
            arrow.transform.localPosition = new Vector3((i - 1) * 0.0012f, 0, 0);
            arrow.transform.localEulerAngles = new Vector3(0, 0, (i - 1) * -14f);
        }
        return root;
    }

    private static readonly Dictionary<string, Material> DeepveinMats = new();

    /// <summary>Deepvein nodes from one FBX, with grey-metal materials made from the matching t_&lt;key&gt; texture set.</summary>
    private static GameObject Deepvein(string file, string texKey, string[] nodes, Dictionary<string, string>? extra = null)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Src}/Deepvein/{file}.fbx");
        var root = new GameObject(file + "_" + texKey);
        foreach (var node in nodes)
        {
            // By mesh name: the FBX root node itself carries some meshes (the chest box sits on "Chest").
            var mf = fbx.GetComponentsInChildren<MeshFilter>().First(x => x.sharedMesh != null && x.sharedMesh.name == node);
            var t = mf.transform;
            var mesh = mf.sharedMesh;
            var key = extra != null && extra.TryGetValue(node, out var k) ? k : texKey;
            var go = CotsGen.Child(root, node, mesh, DeepveinMat(key));
            // Keep the parts' layout from the source hierarchy (lid on the box, cork in the bottle).
            go.transform.position = t.position;
            go.transform.rotation = t.rotation;
            go.transform.localScale = t.lossyScale;
        }
        return root;
    }

    private static Material DeepveinMat(string key)
    {
        if (DeepveinMats.TryGetValue(key, out var m)) return m;
        var dir = $"{Src}/Deepvein/Textures";
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/t_{key}_normal.png");
        if (normal != null)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{dir}/t_{key}_normal.png");
            if (imp.textureType != TextureImporterType.NormalMap)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.SaveAndReimport();
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/t_{key}_normal.png");
            }
        }
        m = CotsGen.Lit("GreyMetal_" + key, Color.white, 0.9f, 0.55f, GreyTexture($"{dir}/t_{key}_color.png", "Grey_" + key, 512), normal);
        DeepveinMats[key] = m;
        return m;
    }

    /// <summary>A desaturated, steel-tinted copy of a colour map, contrast stretched so parts stay distinguishable.</summary>
    private static Texture2D GreyTexture(string path, string name, int size)
    {
        var src = new Texture2D(2, 2);
        src.LoadImage(File.ReadAllBytes(path));
        var px = src.GetPixels();
        float lo = 1, hi = 0;
        foreach (var c in px)
        {
            float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            lo = Mathf.Min(lo, l);
            hi = Mathf.Max(hi, l);
        }
        int w = src.width, h = src.height;
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var o = new Color[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            float l = px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f;
            float v = Mathf.Lerp(0.12f, 0.8f, Mathf.InverseLerp(lo, hi, l));
            o[i] = new Color(v * SteelTint.r, v * SteelTint.g, v * SteelTint.b, 1);
        }
        outTex.SetPixels(o);
        if (w > size)
        {
            // Point-sample down: the KayKit atlas is flat colour blocks, the Deepvein maps are already 512.
            var small = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    small.SetPixel(x, y, outTex.GetPixel(x * w / size, y * h / size));
            Object.DestroyImmediate(outTex);
            outTex = small;
        }
        Object.DestroyImmediate(src);
        return CotsGen.SavePng(outTex, name, false, false);
    }

    // ---- prefab shaping ----

    private static Bounds RendererBounds(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Wraps <paramref name="model"/> so its bounds are centred on the origin, longest side <paramref name="size"/>.</summary>
    private static void Drop(string name, GameObject model, float size, Vector3 tilt)
    {
        var root = new GameObject(name);
        var pivot = new GameObject("tilt");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localEulerAngles = tilt;
        model.transform.SetParent(pivot.transform, false);
        var b = RendererBounds(model);
        float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
        float s = size / Mathf.Max(longest, 1e-5f);
        model.transform.localScale *= s;
        b = RendererBounds(root);
        model.transform.position -= b.center;
        CotsGen.SavePrefab(root);
    }

    /// <summary>A held weapon: real <paramref name="length"/>, origin at the grip end, +Y toward the head (bows: along +Y too).</summary>
    private static GameObject Weapon(string name, string kickin, float length, bool alongX = false)
    {
        var root = new GameObject(name);
        var model = Kickin(kickin);
        model.transform.SetParent(root.transform, false);
        if (alongX) model.transform.localEulerAngles = new Vector3(0, 0, 90);
        var b = RendererBounds(model);
        model.transform.localScale *= length / Mathf.Max(b.size.y, 1e-5f);
        b = RendererBounds(root);
        // Bows are held at the middle of the grip; everything else at the bottom end of the handle.
        model.transform.position -= alongX ? new Vector3(b.center.x, b.center.y, b.center.z) : new Vector3(b.center.x, b.min.y, b.center.z);
        return CotsGen.SavePrefab(root);
    }

    // ---- marker and sparkle (COS-5) ----

    /// <summary>A faceted crystal (two 6-sided pyramids) with an HDRP/Lit material; the mod tints "_BaseColor" by rarity.</summary>
    private static void MakeLootMarker()
    {
        var mesh = new Mesh { name = "LootMarkerMesh" };
        const int sides = 6;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        Vector3 top = new(0, 0.22f, 0), bottom = new(0, -0.22f, 0);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
            Vector3 p0 = new(Mathf.Cos(a0) * 0.12f, 0, Mathf.Sin(a0) * 0.12f), p1 = new(Mathf.Cos(a1) * 0.12f, 0, Mathf.Sin(a1) * 0.12f);
            Tri(verts, tris, top, p1, p0); // flat-shaded: own vertices per face
            Tri(verts, tris, bottom, p0, p1);
        }
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        CotsGen.SaveMesh(mesh);
        var mat = CotsGen.Lit("LootMarkerMat", Color.white, 0.2f, 0.85f, emissive: Color.white * 0.6f);
        var go = new GameObject("LootMarker");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        CotsGen.SavePrefab(go);
    }

    /// <summary>Slow rising sparks around a drop; the mod sets the start colour by rarity.</summary>
    private static void MakeLootSparkle()
    {
        var dot = CotsFxPrefabs.SoftDot();
        var go = new GameObject("LootSparkle");
        var ps = CotsFxPrefabs.Particles(go, CotsGen.Fx("LootSparkleMat", dot, Color.white, 2f));
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.6f;
        main.startSpeed = 0.35f;
        main.startSize = 0.06f;
        main.maxParticles = 40;
        var emission = ps.emission;
        emission.rateOverTime = 14;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.3f;
        shape.rotation = new Vector3(-90, 0, 0); // emit upwards
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        CotsGen.SavePrefab(go);
    }

    private static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        t.Add(v.Count); v.Add(a);
        t.Add(v.Count); v.Add(b);
        t.Add(v.Count); v.Add(c);
    }
}
