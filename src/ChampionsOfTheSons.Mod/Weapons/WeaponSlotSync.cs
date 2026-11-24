using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.ActionBar;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Icons;
using RedLoader;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// The weapon slot drives the game inventory (COS-186): the player owns exactly the real weapon item of the slot item's
/// type, one of it, and none while the slot is empty. A wanted-state check a few times a second, so load, respawn and
/// joining a co-op game need nothing of their own. Equipping never draws (COS-192): the weapon bar key does
/// (<see cref="WeaponBarKeys"/>). A slot weapon on no key goes on the first weapon bar key (key 1), as equipping does
/// (<see cref="UI.InventoryActions"/>); the game's hotkey of that key holds the real item so the game's own hotkey state matches.
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
        var type = WeaponTypes.Equipped(PlayerState.Character.Inventory, ModContent.Db);
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
            var slotItem = pc.Inventory.Get(WeaponTypes.WeaponSlot);
            var want = Wanted();

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
            // The slot weapon is on a weapon bar key (the first one if it was on none); the game's hotkey there holds the
            // real item, and no other game hotkey does.
            int key = pc.WeaponBar.KeyOf(WeaponTypes.WeaponSlot);
            if (key < 0 && FirstWeaponKey is int first && pc.WeaponBar.Bind(first, WeaponTypes.WeaponSlot, pc.Inventory, ModContent.Db)) key = first;
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

    /// <summary>The weapon bar's first key (key 1 by default): where an equipped weapon goes. Null when the bar has no keys.</summary>
    public static int? FirstWeaponKey => ActionBarConfig.WeaponHotkeys.Count > 0 ? ActionBarConfig.WeaponHotkeys[0] : null;

    /// <summary>Puts the weapon slot's item on the weapon bar's first key (the weapon that was there loses its key).</summary>
    public static void BindSlotToFirstKey()
    {
        var pc = PlayerState.Character;
        if (FirstWeaponKey is int first) pc.WeaponBar.Bind(first, WeaponTypes.WeaponSlot, pc.Inventory, ModContent.Db);
    }
}
