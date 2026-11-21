using RedLoader;
using Sons.Items.Core;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// Read-only view of the game's own item hotkeys (keys 1-9 and 0, <c>ItemHotkeyController</c>) for the action bar (COS-151),
/// plus a trigger that does what pressing the number does. Client-local; main thread only.
/// </summary>
internal static class HotkeyBridge
{
    /// <summary>The game's hotkey count: keys 1..9 then 0 (index 0 = key 1, index 9 = key 0).</summary>
    public const int Count = 10;

    public readonly struct Slot
    {
        public readonly int ItemId;
        public readonly int Amount;
        public readonly bool Held;
        public Slot(int itemId, int amount, bool held) { ItemId = itemId; Amount = amount; Held = held; }
        public bool Empty => ItemId <= 0;
    }

    private static ItemHotkeyController? _controller;
    private static readonly Dictionary<int, Texture?> Icons = new();
    private static readonly Dictionary<int, string> Names = new();

    /// <summary>The key printed for hotkey index <paramref name="index"/>: "1".."9", "0".</summary>
    public static string KeyLabel(int index) => index == 9 ? "0" : (index + 1).ToString();

    public static ItemHotkeyController? Controller
    {
        get
        {
            if (_controller != null) return _controller;
            var inv = LocalPlayer.Inventory;
            if (inv != null) _controller = inv.GetComponentInChildren<ItemHotkeyController>(true);
            if (_controller == null) _controller = UnityEngine.Object.FindObjectOfType<ItemHotkeyController>();
            return _controller;
        }
    }

    /// <summary>The right-hand item id on hotkey <paramref name="index"/> (0 = none).</summary>
    public static int ItemIdAt(int index)
    {
        var c = Controller;
        var data = c == null ? null : c._hotkeySaveData;
        var ids = data?.RightHandItemIds;
        if (ids == null || index < 0 || index >= ids.Count) return 0;
        return ids[index];
    }

    public static Slot At(int index)
    {
        int id = ItemIdAt(index);
        if (id <= 0) return default;
        var inv = LocalPlayer.Inventory;
        if (inv == null) return new Slot(id, 0, false);
        int amount = inv.AmountOf(id, true, false);
        var right = inv.RightHandItem;
        bool held = right != null && right._itemID == id;
        return new Slot(id, amount, held);
    }

    /// <summary>Self-tests: puts an item on a hotkey the way the game's save data holds it (the game's own setters need the inventory view).</summary>
    public static void SetForTest(int index, int itemId)
    {
        var ids = Controller?._hotkeySaveData?.RightHandItemIds;
        if (ids != null && index >= 0 && index < ids.Count) ids[index] = itemId;
    }

    /// <summary>Does what pressing the hotkey's number does (equip / toggle / eat).</summary>
    public static void Trigger(int index)
    {
        try
        {
            Controller?.TriggerItemHotkey(index);
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: hotkey {index} trigger failed: {e.Message}");
        }
    }

    /// <summary>The game's own inventory icon for an item, or null.</summary>
    public static Texture? Icon(int itemId)
    {
        if (Icons.TryGetValue(itemId, out var t)) return t;
        Texture? tex = null;
        try
        {
            tex = ItemDatabaseManager.ItemById(itemId)?.UiData?._icon;
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: icon for item {itemId}: {e.Message}");
        }
        Icons[itemId] = tex;
        return tex;
    }

    /// <summary>The game's item name (untranslated database name), for the no-icon fallback and tests.</summary>
    public static string Name(int itemId)
    {
        if (Names.TryGetValue(itemId, out var n)) return n;
        try
        {
            n = ItemDatabaseManager.ItemById(itemId)?.Name ?? "";
        }
        catch
        {
            n = "";
        }
        Names[itemId] = n;
        return n;
    }

    /// <summary>Spike/diagnostic line: every hotkey's ids, amounts and the held item.</summary>
    public static string Describe()
    {
        var c = Controller;
        if (c == null) return "no ItemHotkeyController";
        var data = c._hotkeySaveData;
        var r = data?.RightHandItemIds;
        var l = data?.LeftHandItemIds;
        var parts = new List<string>();
        for (int i = 0; i < Count; i++)
        {
            var s = At(i);
            int left = l != null && i < l.Count ? l[i] : 0;
            parts.Add($"{KeyLabel(i)}:{(s.Empty ? "-" : $"{s.ItemId} {Name(s.ItemId)} x{s.Amount}{(s.Held ? " HELD" : "")}{(Icon(s.ItemId) != null ? " icon" : " noicon")}")}{(left > 0 ? $" L{left}" : "")}");
        }
        return $"controller on '{c.gameObject.name}', right {r?.Count ?? -1} left {l?.Count ?? -1}: " + string.Join(", ", parts);
    }
}
