using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using RedLoader;
using Sons.Gameplay;
using Sons.Items.Core;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.PerkHooks;

/// <summary>Wide Reach's area pickup (COS-85). Rules in <see cref="WideReachRules"/>.</summary>
internal static class WideReachPickup
{
    /// <summary>What the last cast collected (self-test).</summary>
    public static int LastDrops { get; private set; }
    public static int LastGamePickups { get; private set; }

    public static float Radius => (float)WideReachRules.Radius;

    public static void Cast()
    {
        LastDrops = LastGamePickups = 0;
        if (PushingSled()) return;
        var p = LocalPlayer.Transform.position;
        float radius = Radius;
        var pc = PlayerState.Character;
        int lowest = WideReachRules.LowestEquippedRarity(pc.Inventory);
        foreach (var id in LootFeature.Ground.Near(p.x, p.y, p.z, radius))
        {
            var drop = LootFeature.Ground.Get(id);
            if (drop == null || !WideReachRules.Takes(drop.Item, pc.Content, lowest)) continue;
            if (LootFeature.Pickup(id) == Core.Rules.PickupResult.Ok) LastDrops++;
        }
        CollectGamePickups(p, radius);
    }

    private static void CollectGamePickups(Vector3 center, float radius)
    {
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
        {
            try
            {
                var pickup = col.GetComponentInParent<PickUp>();
                if (pickup == null || !pickup.gameObject.activeInHierarchy || !seen.Add(pickup.Pointer)) continue;
                if (!ItemDatabaseManager.TryFindItemById(pickup._itemId, out var data) || data == null || !WideReachRules.IsGamePickup(data.Name)) continue;
                pickup.Collect();
                LastGamePickups++;
            }
            catch (Exception e)
            {
                RLog.Warning($"Champions of the Sons: Wide Reach pickup failed: {e.Message}");
            }
        }
    }

    private static bool PushingSled()
    {
        try
        {
            foreach (var sled in UnityEngine.Object.FindObjectsOfType<LogSledController>())
                if (sled != null && sled.IsLocalPlayerConnected()) return true;
        }
        catch
        {
            // no sled data: not pushing
        }
        return false;
    }

    /// <summary>Which listed loose-pickup names exist as game items (self-test: the list must name real items).</summary>
    public static (List<string> Found, List<string> Missing) CheckList()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = ItemDatabaseManager.Items;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) names.Add(list[i].Name);
        var found = WideReachRules.GamePickups.Where(names.Contains).ToList();
        var missing = WideReachRules.GamePickups.Where(n => !names.Contains(n)).ToList();
        return (found, missing);
    }
}
