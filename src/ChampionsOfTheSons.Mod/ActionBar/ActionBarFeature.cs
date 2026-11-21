using RedLoader;

namespace ChampionsOfTheSons.Mod.ActionBar;

/// <summary>Which of the game's hotkeys (keys 1-9, 0) the weapon and consumable groups show; config text like "1,2,3,4".</summary>
internal static class ActionBarConfig
{
    private const string WeaponDefault = "1,2,3,4", ConsumableDefault = "5,6,7,8";
    private static ConfigEntry<string>? _weapons, _consumables;
    private static string _weaponsText = "", _consumablesText = "";
    private static List<int> _weaponIdx = Parse(WeaponDefault), _consumableIdx = Parse(ConsumableDefault);

    public static void Init()
    {
        _weapons = Config.Category.CreateEntry("actionbar_weapon_keys", WeaponDefault, "Action bar: weapon keys",
            "The game's item hotkeys (1-9, 0) shown on the left of the action bar, up to 4, e.g. 1,2,3,4.");
        _consumables = Config.Category.CreateEntry("actionbar_consumable_keys", ConsumableDefault, "Action bar: consumable keys",
            "The game's item hotkeys (1-9, 0) shown on the right of the action bar, up to 4, e.g. 5,6,7,8.");
    }

    public static IReadOnlyList<int> WeaponHotkeys => Current(_weapons, WeaponDefault, ref _weaponsText, ref _weaponIdx);
    public static IReadOnlyList<int> ConsumableHotkeys => Current(_consumables, ConsumableDefault, ref _consumablesText, ref _consumableIdx);

    private static List<int> Current(ConfigEntry<string>? entry, string fallback, ref string text, ref List<int> parsed)
    {
        string now;
        try
        {
            now = entry?.Value ?? fallback;
        }
        catch
        {
            now = fallback;
        }
        if (now != text)
        {
            text = now;
            parsed = Parse(now);
        }
        return parsed;
    }

    /// <summary>"1,2,3,4" to hotkey indices 0..3 (key 0 is index 9); unknown entries are skipped, at most 4.</summary>
    public static List<int> Parse(string text)
    {
        var list = new List<int>();
        foreach (var part in text.Split(',', ' ', ';'))
        {
            if (!int.TryParse(part.Trim(), out int key) || key is < 0 or > 9) continue;
            int index = key == 0 ? 9 : key - 1;
            if (!list.Contains(index)) list.Add(index);
            if (list.Count == ActionBar.GroupSlots) break;
        }
        return list;
    }
}

/// <summary>COS-151: the action bar along the bottom edge — the game's weapon and consumable hotkeys around the spell slots.</summary>
internal sealed class ActionBarFeature : IModFeature
{
    public static ActionBar Bar { get; } = new();

    public void Init()
    {
        ActionBarConfig.Init();
        Bar.Build();
    }

    public void Update(float dt, bool inWorld) => Bar.Update(UnityEngine.Time.unscaledDeltaTime, inWorld && !UI.CotsUi.AnyPanelOpen);

    public void RegisterSelfTests() => ActionBarSelfTest.Register();
}
