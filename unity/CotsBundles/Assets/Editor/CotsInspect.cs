// Dumps the third-party model sources (meshes, sub-mesh materials, bounds) to %TEMP%/cots-inspect.txt, used to pick
// orientations and colour slots for the generated drop and weapon prefabs.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CotsInspect
{
    public static void Dump()
    {
        var sb = new StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            sb.AppendLine("== " + path);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is Mesh m)
                {
                    var b = m.bounds;
                    sb.AppendLine($"  mesh {m.name} verts {m.vertexCount} tris {m.triangles.Length / 3} sub {m.subMeshCount} center {b.center:F3} size {b.size:F3}");
                }
                else if (o is Material mat)
                    sb.AppendLine($"  mat {mat.name} shader {mat.shader.name} color {(mat.HasProperty("_Color") ? mat.color.ToString() : "-")} tex {(mat.mainTexture ? mat.mainTexture.name : "-")}");
                else if (o is GameObject go && go.transform.parent == null)
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        var t = r.transform;
                        sb.AppendLine($"  node {t.name} pos {t.localPosition:F3} rot {t.localEulerAngles:F1} scale {t.lossyScale:F3} mats {string.Join("|", System.Array.ConvertAll(r.sharedMaterials, x => x ? x.name : "null"))} bounds {r.bounds.center:F3}/{r.bounds.size:F3}");
                    }
            }
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "cots-inspect.txt"), sb.ToString());
        EditorApplication.Exit(0);
    }
}
