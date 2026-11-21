#nullable enable
// Ground-drop models (COS-63) and held-weapon models for the weapon-type work (COS-93), plus the loot marker and
// sparkle. Sources (Assets/ThirdParty, credited in CREDITS.md): Low Poly Weapon Pack (Kickin It Studios) for the
// weapons, KayKit Fantasy Weapons Bits (CC0) for the shield, Deepvein Mine Kit (CC0) for the chest, key and coin
// fallbacks. Weapons and the shield wear one worn-steel set (COS-111, look-dev variant B): Poly Haven CC0 textures
// (metal_plate_02, rusty_metal_04, rough_wood, brown_leather, wood_table_worn) on UVs box-projected in metres, since the
// sources are flat-coloured. The Deepvein props keep their own painted maps, half desaturated to sit with them.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CotsLootPrefabs
{
    private const string Src = "Assets/ThirdParty";

    // Worn-steel set for the flat-coloured models, keyed by the Kickin It material names.
    private static Dictionary<string, Material> _slots = new();

    /// <summary>Real length (m) of each Kickin It model's longest side: its UVs come out in metres whatever the prefab's size.</summary>
    private static readonly Dictionary<string, float> RealLength = new()
    {
        ["Arming_Sword"] = 1.05f, ["Great_Sword"] = 1.6f, ["Bearded_Axe"] = 0.95f, ["War_Hammer"] = 1.1f, ["Spear"] = 2.0f,
        ["Bow"] = 1.4f, ["Halberd"] = 2.1f, ["Double_Axe"] = 1.2f, ["Double_Hammer"] = 1.4f, ["Broadhead_Arrow"] = 0.75f,
    };

    public static void Generate()
    {
        MakeLootMarker();
        MakeLootSparkle();

        _slots = new Dictionary<string, Material>
        {
            // Metallic capped at 0.45: fully metallic surfaces read black wherever the game has no reflection probe.
            // Arguments: metallic and smoothness caps (the mask map scales them), tile size in metres, colour grade.
            ["Metal"] = Worn("Worn_Steel", "metal_plate_02", 0.45f, 0.75f, 0.6f, c => Desat(c, 0.45f) * 1.3f),
            ["Dark Metal"] = Worn("Worn_DarkMetal", "rusty_metal_04", 0.45f, 0.6f, 0.35f, c => c * 0.75f),
            ["Wood"] = Worn("Worn_Wood", "rough_wood", 0, 0.55f, 0.5f, c => c * 0.72f),
            ["Chord"] = Worn("Worn_Leather", "brown_leather", 0, 0.6f, 0.2f, c => Desat(c, 0.35f) * 0.8f),
            ["Bone"] = Worn("Worn_Bone", "wood_table_worn", 0, 0.5f, 0.3f, c => Desat(c, 0.7f) * 1.5f),
        };

        // Held weapons (COS-93): real size, origin at the end of the grip, +Y toward the blade or head, +Z out of the flat.
        Weapon("Weapon_Sword", "Arming_Sword", 1.05f);
        Weapon("Weapon_GreatSword", "Great_Sword", 1.6f);
        Weapon("Weapon_Axe", "Bearded_Axe", 0.95f);
        Weapon("Weapon_Hammer", "War_Hammer", 1.1f);
        Weapon("Weapon_Bow", "Bow", 1.4f, alongX: true);
        Weapon("Weapon_Polearm", "Halberd", 2.1f);

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

    // ---- worn-steel materials (COS-111) ----

    /// <summary>
    /// An HDRP/Lit material from one Poly Haven set in Assets/ThirdParty/PolyHaven: the colour map graded by
    /// <paramref name="grade"/>, its OpenGL normal map, and an HDRP mask map (R metallic, G AO, A smoothness) made from the
    /// ARM map (AO, roughness, metallic). The emissive colour stays free for the weapon glow (COS-108).
    /// </summary>
    private static Material Worn(string name, string asset, float metallic, float smoothness, float tileMetres, System.Func<Color, Color> grade)
    {
        var dir = $"{Src}/PolyHaven/{asset}";
        var c = Raw($"{dir}/{asset}_diff_512.jpg");
        var arm = Raw($"{dir}/{asset}_arm_512.jpg");
        var col = new Texture2D(c.width, c.height, TextureFormat.RGBA32, false);
        col.SetPixels(c.GetPixels().Select(p =>
        {
            var g = grade(p);
            return new Color(Mathf.Clamp01(g.r), Mathf.Clamp01(g.g), Mathf.Clamp01(g.b), 1);
        }).ToArray());
        var mask = new Texture2D(arm.width, arm.height, TextureFormat.RGBA32, false, true);
        mask.SetPixels(arm.GetPixels().Select(a => new Color(a.b, a.r, 1, 1 - a.g)).ToArray());
        Object.DestroyImmediate(c);
        Object.DestroyImmediate(arm);
        var normalPath = $"{dir}/{asset}_nor_gl_512.jpg";
        var imp = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (imp.textureType != TextureImporterType.NormalMap || imp.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            imp.textureType = TextureImporterType.NormalMap;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
        }
        return CotsGen.Lit(name, Color.white, metallic, smoothness, CotsGen.SavePng(col, name + "_c", false, true),
            AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath), CotsGen.SavePng(mask, name + "_m", true, true), tileMetres: tileMetres);
    }

    /// <summary>An image file's bytes as a readable texture, no colour conversion.</summary>
    private static Texture2D Raw(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    private static Color Desat(Color c, float amount)
    {
        float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
        return Color.Lerp(c, new Color(l, l, l, 1), amount);
    }

    /// <summary>
    /// <paramref name="src"/> with every triangle given its own vertices and a box-projected UV in metres (side faces:
    /// V runs along the model's length), one sub-mesh per list in <paramref name="subs"/>. <paramref name="metresPerUnit"/>
    /// converts the source's units, so tiling textures sit at real-world scale.
    /// </summary>
    private static Mesh BoxUv(Mesh src, string name, float metresPerUnit, List<int[]> subs)
    {
        var sv = src.vertices;
        var sn = src.normals;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var outSubs = new List<int[]>();
        foreach (var tris in subs)
        {
            var o = new int[tris.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = sv[tris[t]], b = sv[tris[t + 1]], c = sv[tris[t + 2]];
                var n = Vector3.Cross(b - a, c - a);
                var an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                for (int j = 0; j < 3; j++)
                {
                    var p = sv[tris[t + j]] * metresPerUnit;
                    o[t + j] = verts.Count;
                    verts.Add(sv[tris[t + j]]);
                    norms.Add(sn.Length > 0 ? sn[tris[t + j]] : n.normalized);
                    uvs.Add(an.x >= an.y && an.x >= an.z ? new Vector2(p.z, p.y) : an.y >= an.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y));
                }
            }
            outSubs.Add(o);
        }
        var m = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetUVs(0, uvs);
        m.subMeshCount = outSubs.Count;
        for (int s = 0; s < outSubs.Count; s++) m.SetTriangles(outSubs[s], s);
        m.RecalculateTangents();
        return CotsGen.SaveMesh(m);
    }

    private static float Longest(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

    // ---- sources ----

    /// <summary>A Kickin It model: one mesh with a sub-mesh per colour slot, stored in centimetres-ish units, +Y = head.</summary>
    private static GameObject Kickin(string file)
    {
        var path = $"{Src}/KickinIt/{file}.fbx";
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var srcMats = fbx.GetComponentInChildren<Renderer>().sharedMaterials;
        if (!KickinMeshes.TryGetValue(file, out var mesh))
        {
            var src = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First();
            var subs = Enumerable.Range(0, src.subMeshCount).Select(src.GetTriangles).ToList();
            KickinMeshes[file] = mesh = BoxUv(src, file + "_uv", RealLength[file] / Longest(src.bounds), subs);
        }
        var root = new GameObject(file);
        var child = CotsGen.Child(root, "model", mesh, _slots["Metal"]);
        child.GetComponent<MeshRenderer>().sharedMaterials = srcMats.Select(m => _slots.TryGetValue(m.name, out var g) ? g : _slots["Metal"]).ToArray();
        return root;
    }

    private static readonly Dictionary<string, Mesh> KickinMeshes = new();

    /// <summary>
    /// A KayKit model (the shield, about 0.6 m across): its atlas is flat colour blocks, so each triangle goes to the worn
    /// slot its block's colour suggests (grey: metal, brown: wood, other colours: leather) and gets box UVs.
    /// </summary>
    private static GameObject KayKit(string file)
    {
        var src = AssetDatabase.LoadAllAssetsAtPath($"{Src}/KayKit/{file}.fbx").OfType<Mesh>().First();
        var atlas = Raw($"{Src}/KayKit/weapons_bits_texture.png");
        string[] order = { "Metal", "Dark Metal", "Wood", "Chord" };
        var lists = order.Select(_ => new List<int>()).ToArray();
        var tris = src.triangles;
        var uv = src.uv;
        for (int t = 0; t < tris.Length; t += 3)
        {
            var mid = (uv[tris[t]] + uv[tris[t + 1]] + uv[tris[t + 2]]) / 3;
            var c = atlas.GetPixelBilinear(mid.x, mid.y);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            int slot = s < 0.2f ? (v > 0.45f ? 0 : 1) : h is > 0.02f and < 0.15f ? 2 : 3;
            lists[slot].AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
        }
        Object.DestroyImmediate(atlas);
        Debug.Log($"cots: {file} worn slots: " + string.Join(", ", order.Select((o, i) => $"{o} {lists[i].Count / 3}")));
        var used = Enumerable.Range(0, order.Length).Where(i => lists[i].Count > 0).ToArray();
        var mesh = BoxUv(src, file + "_uv", 0.6f / Longest(src.bounds), used.Select(i => lists[i].ToArray()).ToList());
        var root = new GameObject(file);
        var child = CotsGen.Child(root, "model", mesh, _slots["Wood"]);
        child.GetComponent<MeshRenderer>().sharedMaterials = used.Select(i => _slots[order[i]]).ToArray();
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
        m = CotsGen.Lit("Prop_" + key, Color.white, 0.45f, 0.55f, WornTexture($"{dir}/t_{key}_color.png", "Worn_" + key), normal);
        DeepveinMats[key] = m;
        return m;
    }

    /// <summary>A Deepvein colour map half desaturated, to sit with the worn-steel weapons (no resizing: the maps are 512).</summary>
    private static Texture2D WornTexture(string path, string name)
    {
        var src = Raw(path);
        var outTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        outTex.SetPixels(src.GetPixels().Select(c =>
        {
            var d = Desat(c, 0.5f);
            return new Color(d.r, d.g, d.b, 1);
        }).ToArray());
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
