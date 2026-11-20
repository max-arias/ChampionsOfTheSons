using RedLoader;
using SonsSdk;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Creates the mod's panels and drives them. <see cref="Init"/> is called once from <c>OnSdkInitialized</c> (SUI is ready by then).</summary>
internal static class CotsUi
{
    private static bool _initialized;

    public static InventoryPanel Inventory { get; private set; } = null!;

    /// <summary>True while any of the mod's full panels (inventory, perks, crafting, host settings, difficulty picker) is open.
    /// HUD bars hide meanwhile so they never draw over a panel (COS-58).</summary>
    public static bool AnyPanelOpen => (Inventory?.IsOpen ?? false) || ChampionsOfTheSons.Mod.Pad.GamepadFeature.OpenPanel != null;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        Inventory = new InventoryPanel();
        Inventory.Build();

        // The keybind only fires while the player is controllable, which is not the case with a panel open:
        // closing is handled in Update. Toggle ignores a second call in the same frame, so both paths can fire safely.
        Config.InventoryKey.Notify(ToggleInventory);
        GlobalEvents.OnUpdate.Subscribe(Update);
    }

    /// <summary>The inventory key: the guide book pages (COS-59), or the screen panel when that is off or the book is unavailable.</summary>
    public static void ToggleInventory()
    {
        if (Book.BookHost.ModPageActive) return; // the book handles its own close
        if (!Inventory.IsOpen && Config.InventoryInBook.Value)
        {
            try
            {
                if (Book.BookHost.Open(Book.BookTab.Gear)) return;
                RLog.Msg("Champions of the Sons: no guide book to open, using the inventory panel");
            }
            catch (Exception e)
            {
                RLog.Error($"Champions of the Sons: opening the book failed, using the inventory panel: {e.Message}");
            }
        }
        Inventory.Toggle();
    }

    private static void Update()
    {
        Inventory.Tick();
        if (!Inventory.IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { if (!Inventory.CloseMenu()) Inventory.Close(); }
        else if (Settings.ModKeys.PressedThisFrame(Config.InventoryKey)) Inventory.Toggle();
    }
}
