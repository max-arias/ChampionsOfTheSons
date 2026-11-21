
namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Wide Reach (COS-85, Max 2026-11-20: "pick up things or pull them towards you"): an area pickup, not a damage cone. A cast collects
/// the mod's drops within <see cref="Radius"/> m that pass the rarity rule, and the game's small loose resource pickups in the same radius.
/// </summary>
public static class WideReachRules
{
    /// <summary>Pickup radius in metres: this mod's Wide Reach radius (COTF used 7.5 m).</summary>
    public const double Radius = 5.5;

    /// <summary>The game's small loose pickups Wide Reach also collects, by the game's item name (case-insensitive, whole name).
    /// COTF's list of about 28 The Forest pickups, mapped to Sons of the Forest items that exist as loose world pickups.</summary>
    public static readonly IReadOnlyList<string> GamePickups = new[]
    {
        // Checked against the game's item list in the self-test (2026-11-20).
        "Stick", "Rock", "SmallRock", "Stone", "Rope", "Cloth", "Feather", "Leaf", "CraftedArrow", "Bone", "Skull",
        "AloeVera", "Fireweed", "Horsetail", "Chicory", "Pills", "Coins", "DuctTape", "Wire", "CircuitBoard",
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

    /// <summary>COTF's rule: a mod drop is taken if its rarity is at least the lowest equipped rarity (COTF also always took
    /// materials, which COTS no longer has, COS-145).</summary>
    public static bool Takes(ItemInstance item, int lowestEquippedRarity) => (int)item.Rarity >= lowestEquippedRarity;

    /// <summary>Whether a game item name is on the loose-pickup list.</summary>
    public static bool IsGamePickup(string? itemName) =>
        !string.IsNullOrEmpty(itemName) && GamePickups.Any(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase));
}
