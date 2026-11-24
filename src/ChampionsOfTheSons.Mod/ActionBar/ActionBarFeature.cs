using RedLoader;

namespace ChampionsOfTheSons.Mod.ActionBar;

/// <summary>Which hotkeys (keys 1-9, 0) the weapon group (the weapon bar, COS-192) and the consumable group show; config text like
/// "1,2,3,4,5,6".</summary>
internal static class ActionBarConfig
{
    private const string WeaponDefault = "1,2,3,4,5,6", ConsumableDefault = "7,8,9,0";
    /// <summary>The defaults before COS-192: a config still on them moves to the new ones.</summary>
    private const string OldWeaponDefault = "1,2,3,4", OldConsumableDefault = "5,6,7,8";
    private static ConfigEntry<string>? _weapons, _consumables;
    private static string _weaponsText = "", _consumablesText = "";
    private static List<int> _weaponIdx = Parse(WeaponDefault, ActionBar.WeaponSlots), _consumableIdx = Parse(ConsumableDefault, ActionBar.ConsumableSlots);

    public static void Init()
    {
        _weapons = Config.Category.CreateEntry("actionbar_weapon_keys", WeaponDefault, "Action bar: weapon keys",
            $"The weapon bar's keys (1-9, 0) on the left of the action bar, up to {ActionBar.WeaponSlots}, e.g. 1,2,3,4,5,6.");
        _consumables = Config.Category.CreateEntry("actionbar_consumable_keys", ConsumableDefault, "Action bar: consumable keys",
            $"The game's item hotkeys (1-9, 0) shown on the right of the action bar, up to {ActionBar.ConsumableSlots}, e.g. 7,8,9,0.");
        try
        {
            if (_weapons.Value == OldWeaponDefault && _consumables.Value == OldConsumableDefault)
            {
                _weapons.Value = WeaponDefault;
                _consumables.Value = ConsumableDefault;
            }
        }
        catch { /* the defaults stand */ }
    }

    public static IReadOnlyList<int> WeaponHotkeys => Current(_weapons, WeaponDefault, ActionBar.WeaponSlots, ref _weaponsText, ref _weaponIdx);
    public static IReadOnlyList<int> ConsumableHotkeys => Current(_consumables, ConsumableDefault, ActionBar.ConsumableSlots, ref _consumablesText, ref _consumableIdx);

    private static List<int> Current(ConfigEntry<string>? entry, string fallback, int max, ref string text, ref List<int> parsed)
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
            parsed = Parse(now, max);
        }
        return parsed;
    }

    /// <summary>"1,2,3,4" to hotkey indices 0..3 (key 0 is index 9); unknown entries are skipped, at most <paramref name="max"/>.</summary>
    public static List<int> Parse(string text, int max)
    {
        var list = new List<int>();
        foreach (var part in text.Split(',', ' ', ';'))
        {
            if (!int.TryParse(part.Trim(), out int key) || key is < 0 or > 9) continue;
            int index = key == 0 ? 9 : key - 1;
            if (!list.Contains(index)) list.Add(index);
            if (list.Count == max) break;
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

    public void Update(float dt, bool inWorld) => Bar.Update(UnityEngine.Time.unscaledDeltaTime, inWorld && !UI.CotsUi.AnyPanelOpen && Config.ShowHud.Value);

    public void RegisterSelfTests() => ActionBarSelfTest.Register();
}
