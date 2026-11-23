using HarmonyLib;
using RedLoader;
using TheForest.Items.Inventory;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Game hooks of the real weapon items (COS-186): the game's inventory save never holds one of our item ids (a save loaded
/// without the mod must not meet unknown items; the weapon slot gives the item back after load). Hotkeys need nothing: the
/// game's <c>HotkeySaveData.SanitizeItemArray</c> clears unknown ids on load. Drops are refused by the item data
/// (<see cref="RealWeapons"/>). A run with patches on the drop methods (ref GameObject arguments) and on the hotkey save
/// (ref result) died natively right after the mod loaded; the same run without them passed (2026-11-23).
/// </summary>
[HarmonyPatch]
internal static class RealWeaponPatches
{
    /// <summary>Strips our items from the inventory save data the game is about to write (a fresh object: the live inventory keeps them).</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.Sons_Save_ISaveGameSerializer_TheForest_Items_Inventory_PlayerInventory_PlayerInventorySaveData__OnSerialize))]
    [HarmonyPostfix]
    private static void StripInventory(PlayerInventory.PlayerInventorySaveData __result)
    {
        if (__result == null) return;
        try
        {
            foreach (var w in RealWeapons.All) __result.RemoveItem(w.Id);
            SaveStrips++;
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: stripping weapons from the inventory save failed: {e}");
        }
    }

    /// <summary>Inventory saves stripped (self-test).</summary>
    public static int SaveStrips { get; private set; }
}
