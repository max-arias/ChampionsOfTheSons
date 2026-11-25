using System.Text;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Inventory actions behind the guide book pages and card (COS-59). No UI code.</summary>
internal static class InventoryActions
{
    private static PlayerCharacter Pc => PlayerState.Character;

    /// <summary>Bag item -> equip it; equipped item -> back into the first free bag slot. A bag weapon goes on or off the
    /// quickbar instead (COS-212).</summary>
    public static bool ToggleEquip(string key)
    {
        var inv = Pc.Inventory;
        if (inv.Get(key) == null) return false;
        if (IsBagWeapon(key)) return ToggleQuickbar(key) != QuickbarResult.NotAWeapon;
        if (key.StartsWith("bag.", StringComparison.Ordinal)) return inv.Equip(key, ModContent.Db);
        var free = FirstFreeBagSlot(inv);
        return free != null && inv.Move(key, free, ModContent.Db);
    }

    public enum QuickbarResult { Bound, Unbound, Full, NotAWeapon }

    public static bool IsBagWeapon(string key) =>
        key.StartsWith("bag.", StringComparison.Ordinal) && WeaponTypes.IsWeapon(Pc.Inventory.Get(key), ModContent.Db);

    /// <summary>Right-click (or pad West) on a bag weapon: off the quickbar when bound, else onto the first free key. A full bar
    /// says how to replace one.</summary>
    public static QuickbarResult ToggleQuickbar(string key)
    {
        if (!IsBagWeapon(key)) return QuickbarResult.NotAWeapon;
        var bar = Pc.WeaponBar;
        bar.WeaponKeys = ActionBar.ActionBarConfig.WeaponHotkeys;
        bool was = bar.KeyOf(key) >= 0;
        int now = bar.Toggle(key, Pc.Inventory, ModContent.Db);
        if (was) return QuickbarResult.Unbound;
        if (now >= 0) return QuickbarResult.Bound;
        LootFeature.Message(UiText.T("ui.quickbar.full", "Quickbar full: drag onto a key to replace."));
        return QuickbarResult.Full;
    }

    /// <summary>Puts a bag weapon on a quickbar key (index into the game's hotkeys), replacing that key's weapon.</summary>
    public static bool BindToKey(string key, int index)
    {
        var bar = Pc.WeaponBar;
        bar.WeaponKeys = ActionBar.ActionBarConfig.WeaponHotkeys;
        return bar.Bind(index, key, Pc.Inventory, ModContent.Db);
    }

    /// <summary>Moves a bag item into a chosen equip slot (or swaps). False when it does not fit.</summary>
    public static bool EquipInto(string bagKey, string equipKey) => bagKey != equipKey && Pc.Inventory.Move(bagKey, equipKey, ModContent.Db);

    /// <summary>Moves (or swaps) any slot to any other: bag, equip slot, either way. False when an item does not fit.</summary>
    public static bool MoveTo(string from, string to) => from != to && Pc.Inventory.MoveOrMerge(from, to, ModContent.Db); // equal stacks merge (COS-96)

    public static string? FirstFreeBagSlot(Inventory inv)
    {
        for (int i = 0; i < LootRoller.BagSlots; i++)
            if (inv.Get(Inventory.Bag(i)) == null) return Inventory.Bag(i);
        return null;
    }

    /// <summary>A bag item the Use action can consume (hearts and other items with a use effect).</summary>
    public static bool IsUsable(string key) =>
        key.StartsWith("bag.", StringComparison.Ordinal) && Pc.Inventory.Get(key) is { } item && Consumables.CanUse(item);

    /// <summary>Uses one unit of a bag item and says what it was.</summary>
    public static bool Use(string key)
    {
        var item = Pc.Inventory.Get(key);
        if (item == null || !Consumables.Use(Pc, key)) return false;
        LootFeature.Message(string.Format(UiText.T("ui.catalog.used", "Used {0}."), UiText.ItemName(item)));
        return true;
    }

    public static bool Drop(string key)
    {
        bool had = PlayerState.Character.Inventory.Get(key) != null;
        // A co-op client hands the drop to the host and gets no local drop back: the item leaving the bag is the success.
        return LootFeature.DropFromInventory(key) != null || (had && PlayerState.Character.Inventory.Get(key) == null);
    }

    public static bool Destroy(string key) => LootFeature.Destroy(key);

    /// <summary>A bag stack of two or more (Split and Drop 1 apply, COS-96).</summary>
    public static bool IsStack(string key) => key.StartsWith("bag.", StringComparison.Ordinal) && Pc.Inventory.Get(key) is { Amount: > 1 };

    /// <summary>Drops one of a stack; the rest stays in the slot.</summary>
    public static bool DropOne(string key)
    {
        int before = Pc.Inventory.Get(key)?.Amount ?? 0;
        if (before == 0) return false;
        return LootFeature.DropFromInventory(key, 1) != null || (Pc.Inventory.Get(key)?.Amount ?? 0) < before;
    }

    /// <summary>Split Stack: half goes to the first free bag slot.</summary>
    public static bool Split(string key) => Pc.Inventory.Split(key);

    /// <summary>"Drop all" for a stack, "Drop" otherwise.</summary>
    public static string DropLabel(string key) =>
        IsStack(key) ? UiText.T("ui.inventory.menu.drop_all", "Drop all") : UiText.T("ui.inventory.menu.drop", "Drop");

    public static int BagCount() => Pc.Inventory.Slots.Count(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal));

    /// <summary>Packs the bag to the front, ordered by equipment slot, then rarity (best first), then level (highest first).</summary>
    public static void SortBag()
    {
        var inv = Pc.Inventory;
        var db = ModContent.Db;
        int SlotOrder(ItemInstance it)
        {
            if (!db.Items.TryGetValue(it.BaseId, out var def)) return 999;
            if (def.Type == WeaponTypes.WeaponType) return -1; // weapons first (COS-212: they live in the bag)
            var slots = LootRoller.SlotsFor(def.Type);
            return slots.Count == 0 ? 998 : LootRoller.EquipSlots.ToList().IndexOf(slots[0]);
        }
        var bag = inv.Slots.Where(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal))
            .OrderBy(kv => SlotOrder(kv.Value)).ThenByDescending(kv => (int)kv.Value.Rarity).ThenByDescending(kv => kv.Value.Level)
            .ThenBy(kv => kv.Value.BaseId, StringComparer.Ordinal).ToList();
        var slotsOut = inv.Slots.Where(kv => !kv.Key.StartsWith("bag.", StringComparison.Ordinal)).ToList();
        var moved = new Dictionary<string, string>();
        for (int i = 0; i < bag.Count; i++)
        {
            slotsOut.Add(new KeyValuePair<string, ItemInstance>(Inventory.Bag(i), bag[i].Value));
            moved[bag[i].Key] = Inventory.Bag(i);
        }
        Pc.RearrangeBag(slotsOut, moved); // quickbar keys and the selected weapon follow their items
    }

    /// <summary>Equipped set pieces and their bonuses as rich text (active bonuses in <paramref name="activeHex"/>).</summary>
    public static string SetBonusText(string titleHex, string activeHex, string inactiveHex)
    {
        var pc = Pc;
        var counts = ItemEffects.SetCounts(pc.ActiveGear(), pc.Content);
        var sb = new StringBuilder();
        foreach (var (setId, have) in counts)
        {
            if (!pc.Content.Sets.TryGetValue(setId, out var set)) continue;
            int total = pc.Content.Items.Values.Count(i => i.Set == setId);
            sb.Append($"<color={titleHex}><b>{ModContent.Text.Get(setId + ".name", set.Name)}</b> ({have}/{total})</color>\n");
            foreach (var bonus in set.Bonuses.OrderBy(b => b.Pieces))
            {
                var text = ModContent.Text.Get($"{setId}.bonus.{bonus.Pieces}", "");
                if (text.Length == 0) continue;
                sb.Append($"<color={(have >= bonus.Pieces ? activeHex : inactiveHex)}>({bonus.Pieces}) {text}</color>\n");
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
}
