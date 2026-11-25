using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using Sons.Gameplay.GameSetup;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>
/// COS-213 (Max, 2026-11-24: "we should honor the base game's pvp settings"): the game world's own PvP damage setting ("Off",
/// "Reduced", "Normal") decides whether players hurt each other, the mod's spells included. The mod only reads it; the host reads
/// it and sends it to the clients in <c>WorldSettings</c>. (Before, a mod "friendly fire" toggle overwrote it, COS-78.)
/// </summary>
internal static class PvpRule
{
    private static bool _warned;

    /// <summary>Host: reads the game's setting into <see cref="WorldState.Pvp"/>.</summary>
    public static void Read()
    {
        try
        {
            string setting = GameSetupManager.GetPvpDamageSetting();
            var mode = PvpRules.Parse(setting);
            if (mode != WorldState.Pvp) RLog.Msg($"Champions of the Sons: the game's PvP damage is {setting}");
            WorldState.SetPvp(mode);
        }
        catch (Exception e)
        {
            if (!_warned) RLog.Warning($"Champions of the Sons: could not read the game's PvP damage: {e.Message}");
            _warned = true;
        }
    }
}
