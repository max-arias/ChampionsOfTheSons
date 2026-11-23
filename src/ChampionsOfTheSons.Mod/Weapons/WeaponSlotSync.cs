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
/// joining a co-op game need nothing of their own. A new weapon goes into the hand at once and onto a hotkey.
/// </summary>
internal static class WeaponSlotSync
{
    private static int _wanted;
    private static string _lookKey = "";
    private static bool _fresh = true;

    /// <summary>Times the sync added, removed or equipped an item (self-test).</summary>
    public static int Gives { get; private set; }
    public static int Takes { get; private set; }

    public static void Reset()
    {
        _wanted = 0;
        _lookKey = "";
        _fresh = true;
    }

    public static void Tick()
    {
        var inv = LocalPlayer.Inventory;
        if (inv == null || !RealWeapons.Registered) return;
        try
        {
            var slotItem = PlayerState.Character.Inventory.Get(WeaponTypes.WeaponSlot);
            var type = WeaponTypes.Equipped(PlayerState.Character.Inventory, ModContent.Db);
            var want = RealWeapons.ByType(type?.Id);
            if (want?.Item == null) want = null;

            // The hotkey the previous weapon sat on, so a swap keeps the key.
            int hotkey = -1;
            foreach (var w in RealWeapons.All)
            {
                int i = HotkeyIndexOf(w.Id);
                if (hotkey < 0 && i >= 0) hotkey = i;
                if (w == want) continue;
                int n = inv.AmountOf(w.Id, true, false);
                if (n > 0)
                {
                    inv.RemoveItem(w.Id, n, false, false, true, null, true);
                    Takes++;
                }
                if (i >= 0) HotkeyBridge.Set(i, 0);
            }

            if (want == null)
            {
                _wanted = 0;
                _fresh = false;
                return;
            }

            string look = slotItem != null ? $"{slotItem.BaseId}" : "";
            if (look != _lookKey || want.Id != _wanted) Label(want, slotItem);

            int have = inv.AmountOf(want.Id, true, false);
            if (have > 1) inv.RemoveItem(want.Id, have - 1, false, false, true, null, true);
            bool added = false;
            if (have == 0)
            {
                added = inv.AddItem(want.Id, 1, true, false, null);
                if (added) Gives++;
            }
            if (HotkeyIndexOf(want.Id) < 0) HotkeyBridge.Set(hotkey >= 0 ? hotkey : FreeWeaponHotkey(), want.Id);

            // A newly chosen weapon goes into the hand at once; the one given back on the first check after a load does not.
            if (want.Id != _wanted)
            {
                _wanted = want.Id;
                if (!_fresh) inv.TryEquip(want.Id, false, true, false);
            }
            _fresh = false;
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

    /// <summary>The first empty key of the action bar's weapon group, else its first key.</summary>
    private static int FreeWeaponHotkey()
    {
        var keys = ActionBarConfig.WeaponHotkeys;
        foreach (int i in keys)
            if (HotkeyBridge.ItemIdAt(i) <= 0) return i;
        return keys.Count > 0 ? keys[0] : 0;
    }
}
