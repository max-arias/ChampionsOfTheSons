using RedLoader;
using Sons.Gameplay.GameSetup;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>
/// COS-78 (Max, Wave 7): friendly fire off blocks all player-versus-player damage; on leaves the game's own PvP damage (no mod
/// scaling). The game already has a PvP damage setting ("Off", "Reduced", "Normal"), so the mod drives that instead of filtering
/// hits itself. Mod spells and auras only ever strike enemies, so they need no rule. Applied on the host and on every client
/// (the host's value arrives in <c>WorldSettings</c>).
/// </summary>
internal static class FriendlyFireRule
{
    private static string? _applied;

    /// <summary>The game's PvP setting the mod last set (self-tests); null before the first apply.</summary>
    public static string? Applied => _applied;

    public static void Apply(bool on)
    {
        try
        {
            string current = GameSetupManager.GetPvpDamageSetting();
            string want = on ? (current == "Off" ? "Normal" : current) : "Off";
            if (current != want) GameSetupManager.SetPvpDamageSetting(want);
            if (_applied != want) RLog.Msg($"Champions of the Sons: friendly fire {(on ? "on" : "off")}: the game's PvP damage is {want} (was {current})");
            _applied = want;
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: friendly fire could not set the game's PvP damage: {e.Message}");
        }
    }
}
