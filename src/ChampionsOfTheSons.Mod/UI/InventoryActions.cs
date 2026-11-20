using System.Text;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Inventory actions shared by the screen panel and the guide book pages (COS-59). No UI code.</summary>
internal static class InventoryActions
{
    private static PlayerCharacter Pc => PlayerState.Character;

    /// <summary>Bag item -> equip it; equipped item -> back into the first free bag slot.</summary>
    public static bool ToggleEquip(string key)
    {
        var inv = Pc.Inventory;
        if (inv.Get(key) == null) return false;
        if (key.StartsWith("bag.", StringComparison.Ordinal)) return inv.Equip(key, ModContent.Db);
        var free = FirstFreeBagSlot(inv);
        return free != null && inv.Move(key, free, ModContent.Db);
    }

    /// <summary>Moves a bag item into a chosen equip slot (or swaps). False when it does not fit.</summary>
    public static bool EquipInto(string bagKey, string equipKey) => bagKey != equipKey && Pc.Inventory.Move(bagKey, equipKey, ModContent.Db);

    public static string? FirstFreeBagSlot(Inventory inv)
    {
        for (int i = 0; i < LootRoller.BagSlots; i++)
            if (inv.Get(Inventory.Bag(i)) == null) return Inventory.Bag(i);
        return null;
    }

    public static bool Drop(string key) => LootFeature.DropFromInventory(key) != null;

    public static bool Destroy(string key) => LootFeature.Destroy(key);

    public static int BagCount() => Pc.Inventory.Slots.Count(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal));

    /// <summary>Packs the bag to the front, ordered by equipment slot, then rarity (best first), then level (highest first).</summary>
    public static void SortBag()
    {
        var inv = Pc.Inventory;
        var db = ModContent.Db;
        int SlotOrder(ItemInstance it)
        {
            if (!db.Items.TryGetValue(it.BaseId, out var def)) return 999;
            var slots = LootRoller.SlotsFor(def.Type);
            return slots.Count == 0 ? 998 : LootRoller.EquipSlots.ToList().IndexOf(slots[0]);
        }
        var bag = inv.Slots.Where(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal)).Select(kv => kv.Value)
            .OrderBy(SlotOrder).ThenByDescending(i => (int)i.Rarity).ThenByDescending(i => i.Level).ThenBy(i => i.BaseId, StringComparer.Ordinal).ToList();
        var slotsOut = inv.Slots.Where(kv => !kv.Key.StartsWith("bag.", StringComparison.Ordinal)).ToList();
        for (int i = 0; i < bag.Count; i++) slotsOut.Add(new KeyValuePair<string, ItemInstance>(Inventory.Bag(i), bag[i]));
        inv.Restore(slotsOut);
    }

    /// <summary>Equipped set pieces and their bonuses as rich text (active bonuses in <paramref name="activeHex"/>).</summary>
    public static string SetBonusText(string titleHex, string activeHex, string inactiveHex)
    {
        var pc = Pc;
        var counts = ItemEffects.SetCounts(pc.Inventory.Equipped.Select(kv => kv.Value), pc.Content);
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
