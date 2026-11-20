using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Wide Reach (COS-85): an area pickup, not a damage cone. A cast collects the mod's drops within <see cref="Radius"/> m
/// (times the player's range factor) that pass the rarity rule, and the game's small loose resource pickups in the same radius.
/// </summary>
public static class WideReachRules
{
    /// <summary>Pickup radius in metres before the range factor (COTF).</summary>
    public const double Radius = 7.5;

    /// <summary>The game's small loose pickups Wide Reach also collects, by the game's item name (case-insensitive, whole name).
    /// COTF's list of about 28 The Forest pickups, mapped to Sons of the Forest items that exist as loose world pickups.</summary>
    public static readonly IReadOnlyList<string> GamePickups = new[]
    {
        "Stick", "Rock", "SmallRock", "Stone", "Rope", "Cloth", "Feather", "Leaf", "Leaves", "Arrow", "CraftedArrow",
        "Bone", "Skull", "Aloe", "AloeVera", "Yarrow", "Fireweed", "Horsetail", "Chicory", "Devil'sClub", "Arrowleaf", "Coneflower",
        "Pills", "Bandage", "Coins", "Battery", "DuctTape", "Wire", "CircuitBoard", "Turtle Egg", "BatteryCharged",
    };

    /// <summary>The lowest rarity among the player's equipped items, or 0 when any equipment slot is empty (COTF).</summary>
    public static int LowestEquippedRarity(Inventory inventory)
    {
        int lowest = int.MaxValue;
        foreach (var slot in LootRoller.EquipSlots)
        {
            var item = inventory.Get(slot);
            if (item == null) return 0;
            lowest = Math.Min(lowest, (int)item.Rarity);
        }
        return lowest == int.MaxValue ? 0 : lowest;
    }

    /// <summary>COTF's rule: a mod drop is taken if it is a material, or its rarity is at least the lowest equipped rarity.</summary>
    public static bool Takes(ItemInstance item, ContentDb content, int lowestEquippedRarity)
    {
        if (content.Items.TryGetValue(item.BaseId, out var def) && def.Type == "material") return true;
        return (int)item.Rarity >= lowestEquippedRarity;
    }

    /// <summary>Whether a game item name is on the loose-pickup list.</summary>
    public static bool IsGamePickup(string? itemName) =>
        !string.IsNullOrEmpty(itemName) && GamePickups.Any(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase));
}
