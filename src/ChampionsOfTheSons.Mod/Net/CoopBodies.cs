using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// The players' networked bodies (Bolt entities with a player state): where the other players stand, for visuals that ride on a
/// teammate (COS-80) and their name tags (COS-81), and the local player's own name. Scanned at most twice a second.
/// </summary>
public static class CoopBodies
{
    public readonly record struct Body(Transform Root, string Name, bool Local);

    private static readonly List<Body> Cache = new();
    private static float _scannedAt = -10;
    private static bool _warned;

    /// <summary>Every player body in the scene, the local one included.</summary>
    public static IReadOnlyList<Body> All
    {
        get
        {
            if (Time.realtimeSinceStartup - _scannedAt > 0.5f) Scan();
            return Cache;
        }
    }

    public static IEnumerable<Transform> Others() => All.Where(b => !b.Local && b.Root != null).Select(b => b.Root);

    /// <summary>The local player's name as the game shows it (empty when unknown).</summary>
    public static string LocalName => All.FirstOrDefault(b => b.Local).Name ?? "";

    private static void Scan()
    {
        _scannedAt = Time.realtimeSinceStartup;
        Cache.Clear();
        if (!BoltNetwork.isRunning) return;
        try
        {
            var me = LocalPlayer.Transform != null ? LocalPlayer.Transform.root : null;
            foreach (var e in UnityEngine.Object.FindObjectsOfType<BoltEntity>())
            {
                if (e == null || !e.isAttached || !e.StateIs<IPlayerState>()) continue;
                string name = "";
                try { name = e.GetState<IPlayerState>().name ?? ""; } catch { /* state not filled yet */ }
                var root = e.transform;
                Cache.Add(new Body(root, name, e.isOwner || (me != null && root.root == me)));
            }
        }
        catch (Exception ex)
        {
            if (!_warned) { _warned = true; RLog.Warning($"{CoopTestJoin.Tag} player body scan failed: {ex.Message}"); }
        }
    }
}
