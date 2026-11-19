// Builds the mod's HDRP asset bundle (COS-5). Run from the command line through tools/build-bundles.ps1:
//   Unity.exe -batchmode -quit -projectPath unity/CotsBundles -executeMethod CotsBundleBuilder.BuildFromCommandLine
// The prefabs are generated here from code (mesh, materials, particle system), so the bundle has no third-party
// assets and rebuilds the same way every time. Bought or downloaded art (COS-6) goes under Assets/Cots by hand
// and is picked up by the same build. No custom MonoBehaviours in bundled prefabs: the game has no such scripts;
// motion (the marker spin) is done by the mod.
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CotsBundleBuilder
{
    private const string Root = "Assets/Cots";
    private const string BundleName = "cots";

    [MenuItem("Cots/Generate placeholder prefabs")]
    public static void GeneratePrefabs()
    {
        Directory.CreateDirectory(Root);
        MakeLootMarker();
        MakeLootSparkle();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Cots/Build bundle")]
    public static void Build()
    {
        GeneratePrefabs();
        foreach (var guid in AssetDatabase.FindAssets("", new[] { Root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) continue;
            AssetImporter.GetAtPath(path).SetAssetBundleNameAndVariant(BundleName, "");
        }
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "bundles"));
        Directory.CreateDirectory(output);
        var manifest = BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle,
            BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new System.Exception("BuildAssetBundles failed");
        // Keep only the bundle itself; Unity also writes a folder-named manifest bundle and .manifest text files.
        foreach (var f in Directory.GetFiles(output))
            if (Path.GetFileName(f) != BundleName) File.Delete(f);
        Debug.Log($"Cots bundle written to {output}");
    }

    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    // ---- placeholder content (replaced by real art later) ----

    /// <summary>A faceted crystal (two 6-sided pyramids) with an HDRP/Lit material; the mod tints "_BaseColor" by rarity.</summary>
    private static void MakeLootMarker()
    {
        var mesh = new Mesh { name = "LootMarkerMesh" };
        const int sides = 6;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        Vector3 top = new(0, 0.22f, 0), bottom = new(0, -0.22f, 0);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
            Vector3 p0 = new(Mathf.Cos(a0) * 0.12f, 0, Mathf.Sin(a0) * 0.12f), p1 = new(Mathf.Cos(a1) * 0.12f, 0, Mathf.Sin(a1) * 0.12f);
            AddTri(verts, tris, top, p1, p0);    // flat-shaded: own vertices per face
            AddTri(verts, tris, bottom, p0, p1);
        }
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Root}/LootMarkerMesh.asset");

        var mat = new Material(Shader.Find("HDRP/Lit")) { name = "LootMarkerMat" };
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Metallic", 0.2f);
        mat.SetFloat("_Smoothness", 0.85f);
        mat.EnableKeyword("_EMISSIVE_COLOR_MAP");
        mat.SetColor("_EmissiveColor", Color.white * 0.6f);
        AssetDatabase.CreateAsset(mat, $"{Root}/LootMarkerMat.mat");

        var go = new GameObject("LootMarker");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/LootMarker.prefab");
        Object.DestroyImmediate(go);
    }

    /// <summary>Slow rising sparks around the marker; the mod sets the start color by rarity.</summary>
    private static void MakeLootSparkle()
    {
        var mat = new Material(Shader.Find("HDRP/Unlit")) { name = "LootSparkleMat" };
        mat.SetFloat("_SurfaceType", 1);   // transparent
        mat.SetFloat("_BlendMode", 1);     // additive
        mat.SetColor("_UnlitColor", Color.white);
        mat.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(mat, $"{Root}/LootSparkleMat.mat");

        var go = new GameObject("LootSparkle");
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.6f;
        main.startSpeed = 0.35f;
        main.startSize = 0.05f;
        main.maxParticles = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 14;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.25f;
        shape.rotation = new Vector3(-90, 0, 0); // emit upwards
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/LootSparkle.prefab");
        Object.DestroyImmediate(go);
    }

    private static void AddTri(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        t.Add(v.Count); v.Add(a);
        t.Add(v.Count); v.Add(b);
        t.Add(v.Count); v.Add(c);
    }
}
