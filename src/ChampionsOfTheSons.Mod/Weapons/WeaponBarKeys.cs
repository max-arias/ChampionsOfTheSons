using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.ActionBar;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Book;
using RedLoader;
using SonsSdk;
using TheForest.Utils;
using UnityEngine.InputSystem;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// The weapon bar's keys (COS-192). A number key with a bound mod weapon is the mod's, never the game's hotkey: pressed in the
/// world it swaps that weapon into the weapon slot and draws it, or holsters it when it is in hand. While a mod page of the book
/// shows, every weapon bar key is the mod's (hovering a weapon and pressing one binds it, <see cref="BookFeature"/>), so the game
/// never swaps the book out of the hand for a hotkey.
/// </summary>
internal static class WeaponBarKeys
{
    /// <summary>What the last press did (self-test).</summary>
    public static string LastAction { get; private set; } = "";

    private static PlayerCharacter Pc => PlayerState.Character;

    /// <summary>The weapon bound to hotkey <paramref name="index"/>: its inventory slot, the item and whether it is in hand.</summary>
    public static (string Slot, ItemInstance Item, bool Held)? Bound(int index)
    {
        var slot = Pc.WeaponBar.SlotOf(index);
        var item = slot != null ? Pc.Inventory.Get(slot) : null;
        if (slot == null || item == null) return null;
        return (slot, item, slot == WeaponTypes.WeaponSlot && WeaponInHand());
    }

    /// <summary>Whether one of the mod's weapon items is in the right hand.</summary>
    public static bool WeaponInHand()
    {
        try
        {
            var right = LocalPlayer.Inventory?.RightHandItem;
            return right != null && RealWeapons.IsReal(right._itemID);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The keys ("1".."0") the game's hotkey actions must not see now (<see cref="Settings.ModKeys.Tick"/>).</summary>
    public static IEnumerable<string> HeldBackKeys()
    {
        var keys = new List<string>();
        try
        {
            if (!LocalPlayer.IsInWorld) return keys;
            foreach (int index in Pc.WeaponBar.Bindings.Keys) keys.Add(HotkeyBridge.KeyLabel(index));
            if (BookHost.ModPageActive)
                foreach (int index in ActionBarConfig.WeaponHotkeys) keys.Add(HotkeyBridge.KeyLabel(index));
        }
        catch { /* not in a world */ }
        return keys;
    }

    /// <summary>The hotkey index of a number key pressed this frame (-1 none). Index 9 is the 0 key.</summary>
    public static int PressedNumber()
    {
        var kb = Keyboard.current;
        // Alt+1..6 are the spell keys (and Ctrl/Shift combos other mod keys): only a plain number counts.
        if (kb == null || kb.altKey.isPressed || kb.ctrlKey.isPressed || kb.shiftKey.isPressed) return -1;
        for (int i = 0; i < WeaponBar.Keys; i++)
        {
            var key = kb[Key.Digit1 + i]; // Digit1..Digit9, then Digit0
            if (key != null && key.wasPressedThisFrame) return i;
        }
        return -1;
    }

    /// <summary>Every frame in the world: a bound key pressed while the player plays.</summary>
    public static void Tick()
    {
        if (BookHost.IsOpen) return;
        bool controllable;
        try { controllable = GameState.IsPlayerControllable; } catch { return; }
        if (!controllable) return;
        int index = PressedNumber();
        if (index >= 0) Press(index);
    }

    /// <summary>
    /// What pressing hotkey <paramref name="index"/> does when a weapon is bound to it: a weapon in the bag swaps into the weapon slot
    /// and is drawn; the weapon in the slot is drawn, or holstered when in hand. False when nothing is bound (the game's hotkey).
    /// </summary>
    public static bool Press(int index)
    {
        var bound = Bound(index);
        if (bound == null) return false;
        var (slot, item, held) = bound.Value;
        try
        {
            if (slot != WeaponTypes.WeaponSlot)
            {
                if (!Pc.Inventory.Move(slot, WeaponTypes.WeaponSlot, ModContent.Db))
                {
                    LastAction = $"key {index}: {item.BaseId} does not fit the weapon slot";
                    return true;
                }
                LastAction = $"key {index}: {item.BaseId} into the weapon slot, drawn: {WeaponSlotSync.Draw()}";
            }
            else if (held)
            {
                LocalPlayer.Inventory.StashRightHandItem(false, false, false);
                LastAction = $"key {index}: holstered";
            }
            else LastAction = $"key {index}: drawn: {WeaponSlotSync.Draw()}";
        }
        catch (Exception e)
        {
            LastAction = $"key {index}: failed: {e.Message}";
            RLog.Error($"Champions of the Sons: weapon bar key failed: {e}");
        }
        ActionBarFeature.Bar.Dirty();
        return true;
    }
}
