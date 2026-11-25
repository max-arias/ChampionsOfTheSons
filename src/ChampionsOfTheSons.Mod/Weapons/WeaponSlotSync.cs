using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.ActionBar;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Icons;
using RedLoader;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// The selected weapon drives the game inventory (COS-186, COS-212): the player owns exactly the real weapon item of the
/// selected bag weapon's type, one of it, and none while nothing is selected. A wanted-state check a few times a second, so
/// load, respawn and joining a co-op game need nothing of their own. A quickbar key selects and draws (<see cref="WeaponBarKeys"/>);
/// the game's hotkey of that key holds the real item so the game's own hotkey state matches. It also tells the character whether
/// the weapon is in hand, which is when its stats count.
/// </summary>
internal static class WeaponSlotSync
{
    private static string _lookKey = "";

    /// <summary>Times the sync added, removed or equipped an item (self-test).</summary>
    public static int Gives { get; private set; }
    public static int Takes { get; private set; }

    public static void Reset() => _lookKey = "";

    /// <summary>Syncs now and puts the slot's weapon in the hand (the weapon bar key). False when there is none to draw.</summary>
    public static bool Draw()
    {
        Tick();
        var want = Wanted();
        var inv = LocalPlayer.Inventory;
        if (want == null || inv == null || inv.AmountOf(want.Id, true, false) <= 0) return false;
        return inv.TryEquip(want.Id, false, true, false);
    }

    private static RealWeapons.Weapon? Wanted()
    {
        var type = WeaponTypes.Selected(PlayerState.Character);
        var want = RealWeapons.ByType(type?.Id);
        return want?.Item == null ? null : want;
    }

    public static void Tick()
    {
        var inv = LocalPlayer.Inventory;
        if (inv == null || !RealWeapons.Registered) return;
        try
        {
            var pc = PlayerState.Character;
            var slotItem = pc.SelectedWeapon;
            var want = Wanted();
            pc.SetDrawn(want != null && WeaponBarKeys.WeaponInHand());

            foreach (var w in RealWeapons.All)
            {
                int i = HotkeyIndexOf(w.Id);
                if (w == want) continue;
                int n = inv.AmountOf(w.Id, true, false);
                if (n > 0)
                {
                    inv.RemoveItem(w.Id, n, false, false, true, null, true);
                    Takes++;
                }
                if (i >= 0) HotkeyBridge.Set(i, 0);
            }

            if (want == null) return;

            string look = slotItem != null ? $"{slotItem.BaseId}" : "";
            if (look != _lookKey) Label(want, slotItem);

            int have = inv.AmountOf(want.Id, true, false);
            if (have > 1) inv.RemoveItem(want.Id, have - 1, false, false, true, null, true);
            if (have == 0 && inv.AddItem(want.Id, 1, true, false, null)) Gives++;
            // The game's hotkey of the selected weapon's key holds the real item, and no other game hotkey does.
            int key = pc.SelectedSlot != null ? pc.WeaponBar.KeyOf(pc.SelectedSlot) : -1;
            for (int i = 0; i < HotkeyBridge.Count; i++)
                if (i != key && HotkeyBridge.ItemIdAt(i) == want.Id) HotkeyBridge.Set(i, 0);
            if (key >= 0 && HotkeyBridge.ItemIdAt(key) != want.Id) HotkeyBridge.Set(key, want.Id);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: weapon slot sync failed: {e}");
        }
    }

    /// <summary>The item's name and icon follow the rolled item in the slot (one game item per type).</summary>
    private static void Label(RealWeapons.Weapon w, Core.ItemInstance? slotItem)
    {
        _lookKey = slotItem != null ? $"{slotItem.BaseId}" : "";
        var ui = w.Item?._uiData;
        if (ui == null) return;
        if (slotItem != null)
        {
            ui._title = UI.UiText.ItemName(slotItem);
            var icon = IconCache.GetTexture(slotItem.BaseId);
            if (icon != null) ui._icon = icon;
        }
        HotkeyBridge.Forget(w.Id);
    }

    public static int HotkeyIndexOf(int itemId)
    {
        for (int i = 0; i < HotkeyBridge.Count; i++)
            if (HotkeyBridge.ItemIdAt(i) == itemId) return i;
        return -1;
    }

    /// <summary>Puts a picked-up bag weapon on the first free quickbar key (COS-212). The key, or -1 (not a weapon, or the bar is full).</summary>
    public static int BindPickup(string bagSlot)
    {
        var pc = PlayerState.Character;
        pc.WeaponBar.WeaponKeys = ActionBarConfig.WeaponHotkeys;
        return pc.WeaponBar.BindFirstFree(bagSlot, pc.Inventory, ModContent.Db);
    }
}
