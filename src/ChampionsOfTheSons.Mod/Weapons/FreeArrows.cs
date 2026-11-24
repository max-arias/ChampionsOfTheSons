using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using Sons.Items.Core;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// COS-189: a unique mod bow needs no arrows and leaves none behind. While one is drawn the player owns at least one of the
/// game's best arrow (granted when they have none, taken back when the bow leaves the hand); every arrow it spends comes
/// straight back (<see cref="PerkHooks.ShotTracker"/>'s ammo hook), and the arrow pickups its shots leave are removed.
/// Only AddItem/RemoveItem and a pickup scan: no new Harmony patches.
/// </summary>
internal static class FreeArrows
{
    /// <summary>The game's arrows, best first (item database names; the first one found is the free arrow).</summary>
    private static readonly string[] BestFirst = { "CarbonFiberArrow", "Carbon Fiber Arrow", "3DPrintedArrow", "3D Printed Arrow", "CraftedArrow", "Crafted Arrow", "Arrow" };

    /// <summary>A free shot's arrow pickups are looked for this long after the shot, within this range of the player.</summary>
    private const float PickupSeconds = 15, PickupRange = 80, ScanEvery = 1;

    private static int _best = -2;
    private static int _granted;
    private static float _lastFreeShot = -100, _nextScan;
    private static readonly HashSet<int> FreeAmmo = new();

    /// <summary>Free shots and removed pickups (self-test).</summary>
    public static int FreeShots { get; private set; }
    public static int PickupsRemoved { get; private set; }
    public static int Granted => _granted;

    /// <summary>The free arrow's game item id (-1 when the database has no arrow).</summary>
    public static int BestArrowId
    {
        get
        {
            if (_best != -2) return _best;
            foreach (var name in BestFirst)
            {
                int id = GameItems.Find(name, -1);
                if (id > 0) return _best = id;
            }
            var list = ItemDatabaseManager.Items;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Name.Contains("arrow", StringComparison.OrdinalIgnoreCase)) return _best = list[i].Id;
            return _best = -1;
        }
    }

    /// <summary>Whether a unique mod bow is in the right hand.</summary>
    public static bool Active
    {
        get
        {
            if (WeaponModels.Active?.Base != Core.Content.WeaponBase.Bow || !WeaponBarKeys.WeaponInHand()) return false;
            var item = PlayerState.Character.Inventory.Get(WeaponTypes.WeaponSlot);
            return item != null && ModContent.Db.Items.TryGetValue(item.BaseId, out var def) && WeaponGlow.IsUnique(def);
        }
    }

    public static void Reset()
    {
        _granted = 0;
        FreeAmmo.Clear();
        _lastFreeShot = -100;
    }

    /// <summary>A few times a second in the world: grants or takes back the free arrow, and clears free shots' pickups.</summary>
    public static void Tick()
    {
        var inv = LocalPlayer.Inventory;
        int best = BestArrowId;
        if (inv == null || best <= 0) return;
        try
        {
            if (Active)
            {
                if (inv.AmountOf(best, true, false) <= 0 && inv.AddItem(best, 1, true, false, null)) _granted++;
            }
            else if (_granted > 0)
            {
                int have = inv.AmountOf(best, true, false);
                int take = Math.Min(have, _granted);
                if (take > 0) inv.RemoveItem(best, take, false, false, false, null, true);
                _granted = 0;
            }
            ClearPickups();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: free arrows failed: {e}");
        }
    }

    /// <summary>The game spent an arrow of <paramref name="ammoItemId"/>: with a unique mod bow drawn it comes straight back. True when
    /// it did (no other refund then).</summary>
    public static bool OnAmmoSpent(int ammoItemId)
    {
        var inv = LocalPlayer.Inventory;
        if (ammoItemId <= 0 || inv == null || !Active) return false;
        if (!inv.AddItem(ammoItemId, 1, false, false, null)) return false;
        FreeShots++;
        FreeAmmo.Add(ammoItemId);
        _lastFreeShot = Time.time;
        return true;
    }

    /// <summary>Removes arrow pickups of the free shots' arrow types near the player, for a while after a free shot.</summary>
    private static void ClearPickups()
    {
        if (FreeAmmo.Count == 0 || Time.time - _lastFreeShot > PickupSeconds || Time.time < _nextScan || LocalPlayer.Transform == null) return;
        _nextScan = Time.time + ScanEvery;
        var at = LocalPlayer.Transform.position;
        foreach (var p in UnityEngine.Object.FindObjectsOfType<Sons.Gameplay.PickUp>())
        {
            if (p == null || !FreeAmmo.Contains(p._itemId) || (p.transform.position - at).sqrMagnitude > PickupRange * PickupRange) continue;
            UnityEngine.Object.Destroy(p._destroyTarget != null ? p._destroyTarget : p.gameObject);
            PickupsRemoved++;
        }
    }
}
