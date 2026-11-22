namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Shared panel state. <see cref="Init"/> is called once from <c>OnSdkInitialized</c> (SUI is ready by then).</summary>
internal static class CotsUi
{
    /// <summary>True while any of the mod's full panels (perks, host settings, mod settings) is open.
    /// HUD bars hide meanwhile so they never draw over a panel (COS-58).</summary>
    public static bool AnyPanelOpen => ChampionsOfTheSons.Mod.Pad.GamepadFeature.OpenPanel != null;

    /// <summary>Nothing to wire: the mod has no inventory key; the game's book key opens the guide book and its tab holds the
    /// mod's pages (2026-11-21).</summary>
    public static void Init() { }
}
