using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using Sons.Gameplay.GameSetup;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// COS-130: reads the game's own difficulty (Peaceful, Normal, Hard, Hard Survival, Creative, Custom and, for Custom, the enemy
/// health, damage, armour and aggression sliders) from <c>GameSetupManager</c>. Only the host's reading counts; clients get it
/// in <c>WorldSettings</c>.
/// </summary>
internal static class GameDifficultyReader
{
    private static bool _warned;

    /// <summary>The game's difficulty, or Normal when the game setup cannot be read (logged once).</summary>
    public static GameDifficulty Read()
    {
        try
        {
            var mode = (GameMode)(int)GameSetupManager.GetMode(Mode.Normal);
            if (!Enum.IsDefined(mode)) mode = GameMode.Normal;
            if (mode != GameMode.Custom) return GameDifficulty.For(mode);
            return GameDifficulty.For(mode,
                Slider(GameSetupManager.GetVailEnemyHealthSetting()),
                Slider(GameSetupManager.GetVailEnemyDamageSetting()),
                Slider(GameSetupManager.GetVailEnemyArmourSetting()),
                Slider(GameSetupManager.GetVailEnemyAggressionSetting()));
        }
        catch (Exception e)
        {
            if (!_warned) RLog.Warning($"Champions of the Sons: could not read the game's difficulty, using Normal: {e.Message}");
            _warned = true;
            return GameDifficulty.Normal;
        }
    }

    private static GameSlider Slider(LowNormalHighSetting setting)
    {
        int v = setting;
        if (v == LowNormalHighSetting.LowInt) return GameSlider.Low;
        if (v == LowNormalHighSetting.HighInt) return GameSlider.High;
        return GameSlider.Normal;
    }
}
