using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bundles;

/// <summary>Loads the HDRP asset bundle at start (COS-5) and checks it in the self-test. The bundle is optional.</summary>
public sealed class BundlesFeature : IModFeature
{
    public void Init() => CotsBundle.Load();

    public void RegisterSelfTests()
    {
        SelfTest.Add(1f, "bundle: load", () =>
        {
            if (!CotsBundle.Loaded && CotsBundle.Problem.StartsWith("not embedded", StringComparison.Ordinal))
            {
                SelfTest.Log("bundle: skipped, " + CotsBundle.Problem);
                return;
            }
            SelfTest.Check("bundle: loaded", CotsBundle.Loaded, CotsBundle.Problem);
            foreach (var name in new[] { CotsBundle.LootMarker, CotsBundle.LootSparkle })
            {
                var holder = new GameObject("cots-bundle-test");
                holder.transform.position = LocalPlayerPosition() + Vector3.up;
                var go = CotsBundle.Spawn(name, holder.transform);
                string detail = go == null ? "missing" : name == CotsBundle.LootMarker
                    ? $"renderer {go.GetComponentInChildren<Renderer>()?.sharedMaterial?.shader?.name ?? "none"}"
                    : $"particles {go.GetComponentInChildren<ParticleSystem>() != null}";
                SelfTest.Check($"bundle: {name} instantiates", go != null, detail);
                UnityEngine.Object.Destroy(holder);
            }
        });
    }

    private static Vector3 LocalPlayerPosition() => TheForest.Utils.LocalPlayer.Transform != null ? TheForest.Utils.LocalPlayer.Transform.position : Vector3.zero;
}
