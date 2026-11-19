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
        Config.InventoryKey.Notify(Inventory.Toggle);
        GlobalEvents.OnUpdate.Subscribe(Update);
    }

    private static void Update()
    {
        Inventory.Tick();
        if (!Inventory.IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { if (!Inventory.CloseMenu()) Inventory.Close(); }
        else if (Config.InventoryKey.GetAction().WasPressedThisFrame()) Inventory.Toggle();
    }
}
