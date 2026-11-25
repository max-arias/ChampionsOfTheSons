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

    /// <summary>
    /// Dumps the Asset Store effect packs (COS-211) to %TEMP%/cots-inspect-fx.txt: every material's shader, blend and
    /// options, and every prefab's particle systems, other renderers and scripts. Used to fill CotsPackPrefabs' table.
    /// Run with -executeMethod CotsInspect.DumpFx.
    /// </summary>
    public static void DumpFx()
    {
        var sb = new StringBuilder();
        var root = new[] { CotsPackPrefabs.PackRoot };
        if (!AssetDatabase.IsValidFolder(CotsPackPrefabs.PackRoot)) sb.AppendLine("missing folder " + CotsPackPrefabs.PackRoot);
        else
        {
            sb.AppendLine("#### materials");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", root))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                string F(string p) => m.HasProperty(p) ? m.GetFloat(p).ToString("0.##") : "-";
                string C(string p) => m.HasProperty(p) ? m.GetColor(p).ToString("F2") : "-";
                var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                sb.AppendLine($"{path}\n  shader {m.shader.name} mode {F("_Mode")} blend {F("_SrcBlend")}/{F("_DstBlend")} color {C("_Color")} tint {C("_TintColor")} emission {F("_EmissionEnabled")} {C("_EmissionColor")}"
                    + $" soft {F("_SoftParticlesEnabled")} distortion {F("_DistortionEnabled")} flipbook {F("_FlipbookMode")} cull {F("_Cull")} tex {(tex ? $"{tex.name} {tex.width}x{tex.height} {AssetDatabase.GetAssetPath(tex)}" : "-")}"
                    + $" keywords {string.Join(",", m.shaderKeywords)}");
            }
            sb.AppendLine("#### prefabs");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", root))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                sb.AppendLine("== " + path);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    string node = AnimationUtility.CalculateTransformPath(t, go.transform);
                    if (node.Length == 0) node = ".";
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null) sb.AppendLine($"  {node}: MISSING SCRIPT");
                        else if (c is MonoBehaviour) sb.AppendLine($"  {node}: script {c.GetType().FullName}");
                        else if (c is ParticleSystem ps)
                        {
                            var main = ps.main;
                            var em = ps.emission;
                            var r = t.GetComponent<ParticleSystemRenderer>();
                            sb.AppendLine($"  {node}: ps loop {main.loop} dur {main.duration:0.##} life {main.startLifetime.constantMin:0.##}-{main.startLifetime.constantMax:0.##} size {main.startSize.constantMin:0.##}-{main.startSize.constantMax:0.##}"
                                + $" speed {main.startSpeed.constantMax:0.##} max {main.maxParticles} rate {em.rateOverTime.constantMax:0.#} dist {em.rateOverDistance.constantMax:0.#} bursts {em.burstCount} space {main.simulationSpace} scaling {main.scalingMode}"
                                + $" color {main.startColor.mode} {main.startColor.colorMax:F2} overLife {ps.colorOverLifetime.enabled} sheet {ps.textureSheetAnimation.enabled} trails {ps.trails.enabled} sub {ps.subEmitters.enabled} lights {ps.lights.enabled}"
                                + $" render {(r ? r.renderMode.ToString() : "-")} mat {(r && r.sharedMaterial ? r.sharedMaterial.name : "-")} trailMat {(r && r.trailMaterial ? r.trailMaterial.name : "-")} mesh {(r && r.mesh ? r.mesh.name : "-")}"
                                + $" pos {t.localPosition:F2} rot {t.localEulerAngles:F0} scale {t.localScale:F2}");
                        }
                        else if (c is Renderer rr && !(c is ParticleSystemRenderer))
                            sb.AppendLine($"  {node}: {c.GetType().Name} mats {string.Join("|", System.Array.ConvertAll(rr.sharedMaterials, x => x ? x.name : "null"))} bounds {rr.bounds.size:F2}");
                        else if (!(c is Transform) && !(c is ParticleSystemRenderer) && !(c is MeshFilter))
                            sb.AppendLine($"  {node}: {c.GetType().Name}");
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "cots-inspect-fx.txt"), sb.ToString());
        EditorApplication.Exit(0);
    }
}
