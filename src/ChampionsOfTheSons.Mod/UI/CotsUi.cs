using SonsSdk;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Wires the inventory key. <see cref="Init"/> is called once from <c>OnSdkInitialized</c> (SUI is ready by then).</summary>
internal static class CotsUi
{
    private static bool _initialized;

    /// <summary>True while any of the mod's full panels (perks, host settings, mod settings) is open.
    /// HUD bars hide meanwhile so they never draw over a panel (COS-58).</summary>
    public static bool AnyPanelOpen => ChampionsOfTheSons.Mod.Pad.GamepadFeature.OpenPanel != null;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        Config.InventoryKey.Notify(ToggleInventory);
    }

    /// <summary>
    /// The inventory key opens the guide book on its Gear page (COS-59). The book is the only inventory (Max 2026-11-21: no
    /// fallback panel): when it cannot open, the reason is logged and shown, so it can be fixed.
    /// </summary>
    public static void ToggleInventory()
    {
        if (Book.BookHost.ModPageActive) return; // the book handles its own close
        try
        {
            if (Book.BookHost.Open(Book.BookTab.Gear)) return;
        }
        catch (Exception e)
        {
            Book.BookHost.Fail("exception: " + e.Message);
        }
        Book.BookHost.ReportFailure();
    }
}
