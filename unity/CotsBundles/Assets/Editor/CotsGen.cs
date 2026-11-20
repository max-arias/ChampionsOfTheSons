#nullable enable
// Shared helpers for the generated bundle content: the output folder, procedural textures, HDRP/Lit and Cots/Fx
// materials, meshes and prefab saving. Everything lands under Assets/Cots/Generated, which is wiped on every build.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;

public static class CotsGen
{
    public const string Root = "Assets/Cots/Generated";

    public static void Reset()
    {
        if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Root + "/Textures");
        AssetDatabase.Refresh();
    }

    // ---- textures ----

    /// <summary>A procedural texture saved as PNG and imported; <paramref name="f"/> gets u, v in 0..1.</summary>
    public static Texture2D Tex(string name, int size, Func<float, float, Color> f, bool linear = false, bool repeat = false)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
        t.SetPixels(px);
        return SavePng(t, name, linear, repeat);
    }

    public static Texture2D SavePng(Texture2D t, string name, bool linear, bool repeat)
    {
        var path = $"{Root}/Textures/{name}.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.sRGBTexture = !linear;
        imp.alphaIsTransparency = !linear;
        imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        imp.mipmapEnabled = true;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3 - 2 * t);
    }

    /// <summary>Cheap value noise (tileable over 1) for smoke, fire and swirl textures.</summary>
    public static float Noise(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        float H(int i, int j)
        {
            i = ((i % cells) + cells) % cells;
            j = ((j % cells) + cells) % cells;
            uint h = (uint)(i * 374761393 + j * 668265263 + seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 0xFFFF) / 65535f;
        }
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx), fy);
    }

    public static float Fbm(float u, float v, int cells, int seed) =>
        0.55f * Noise(u, v, cells, seed) + 0.3f * Noise(u, v, cells * 2, seed + 1) + 0.15f * Noise(u, v, cells * 4, seed + 2);

    // ---- materials ----

    public static Material Lit(string name, Color color, float metallic, float smoothness, Texture2D? baseMap = null, Texture2D? normal = null,
        Texture2D? mask = null, Color? emissive = null, float tileMetres = 0)
    {
        var m = new Material(Shader.Find("HDRP/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        if (baseMap != null) m.SetTexture("_BaseColorMap", baseMap);
        // HDRP/Lit tiles every main map with the base map's scale; meshes with UVs in metres pass the tile size.
        if (tileMetres > 0) m.SetTextureScale("_BaseColorMap", Vector2.one / tileMetres);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        if (normal != null)
        {
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_NormalScale", 1);
        }
        if (mask != null)
        {
            m.SetTexture("_MaskMap", mask);
            m.SetFloat("_MetallicRemapMax", metallic);
            m.SetFloat("_SmoothnessRemapMax", smoothness);
        }
        if (emissive != null)
        {
            m.SetColor("_EmissiveColor", emissive.Value);
            m.SetColor("_EmissiveColorLDR", emissive.Value);
            m.SetFloat("_UseEmissiveIntensity", 0);
        }
        HDShaderUtils.ResetMaterialKeywords(m);
        return SaveMat(m);
    }

    /// <summary>A Cots/Fx material: additive glow, or alpha blend when <paramref name="additive"/> is false.</summary>
    public static Material Fx(string name, Texture2D tex, Color tint, float intensity = 1, bool additive = true, float fresnel = 0,
        bool fresnelInvert = false, Vector2 scroll = default, float pulse = 0, bool cullBack = false, bool overlay = false, bool fadeEnds = false)
    {
        var m = new Material(Shader.Find("Cots/Fx")) { name = name };
        m.SetTexture("_MainTex", tex);
        m.SetColor("_TintColor", tint);
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_Fresnel", fresnel);
        m.SetFloat("_FresnelInvert", fresnelInvert ? 1 : 0);
        m.SetVector("_Scroll", scroll);
        m.SetFloat("_Pulse", pulse);
        m.SetFloat("_FadeEnds", fadeEnds ? 1 : 0);
        m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_Cull", cullBack ? (float)CullMode.Back : (float)CullMode.Off);
        m.SetFloat("_ZTest", overlay ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
        m.renderQueue = (int)RenderQueue.Transparent;
        return SaveMat(m);
    }

    private static Material SaveMat(Material m)
    {
        AssetDatabase.CreateAsset(m, $"{Root}/{m.name}.mat");
        return m;
    }

    public static Mesh SaveMesh(Mesh mesh)
    {
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Root}/{mesh.name}.asset");
        return mesh;
    }

    // ---- prefabs ----

    public static GameObject SavePrefab(GameObject go)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/{go.name}.prefab");
        UnityEngine.Object.DestroyImmediate(go);
        return prefab;
    }

    public static GameObject Child(GameObject parent, string name, Mesh mesh, Material mat, Vector3 pos = default, Vector3 euler = default,
        Vector3? scale = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale ?? Vector3.one;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }
}
