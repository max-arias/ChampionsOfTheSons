using ChampionsOfTheSons.Core.Content;
using RedLoader;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace ChampionsOfTheSons.Mod.Language;

/// <summary>
/// The mod's text follows the game's language option (COS-74): the game's selected locale is mapped onto a shipped table
/// (<see cref="TextDb.MatchLanguage"/>), checked once a second so a change in the options menu applies at once. The
/// "language" setting overrides it. On a change, text already on screen is re-labelled and open panels refresh.
/// </summary>
public sealed class LanguageFeature : IModFeature
{
    public const string Auto = "auto";

    public static ConfigEntry<string> Setting { get; private set; } = null!;

    /// <summary>The game's locale code as last read ("" until the game's localization is ready).</summary>
    public static string GameLocale { get; private set; } = "";

    /// <summary>Self-tests force a language here instead of writing the setting to the config file.</summary>
    internal static string? TestOverride { get; set; }

    private float _poll;

    public void Init()
    {
        Setting = Config.Category.CreateEntry("language", Auto, "Language",
            "Language of the mod's text: auto (follow the game) or one of " + string.Join(", ", TextDb.Languages) + ".");
        Apply();
    }

    public void Update(float dt, bool inWorld)
    {
        _poll -= dt;
        if (_poll > 0) return;
        _poll = 1f;
        Apply();
    }

    public void RegisterSelfTests() => LanguageSelfTest.Register();

    /// <summary>The language the mod should show now: the setting when it names a shipped table, else the game's.</summary>
    public static string Wanted()
    {
        var forced = TestOverride ?? Setting?.Value?.Trim() ?? Auto;
        if (!string.Equals(forced, Auto, StringComparison.OrdinalIgnoreCase) && forced.Length > 0) return TextDb.MatchLanguage(forced);
        GameLocale = ReadGameLocale() ?? GameLocale;
        return TextDb.MatchLanguage(GameLocale);
    }

    private static string? ReadGameLocale()
    {
        try
        {
            return LocalizationSettings.SelectedLocale?.Identifier.Code;
        }
        catch
        {
            return null; // localization not initialized yet
        }
    }

    /// <summary>Switches to <see cref="Wanted"/> when it differs from the current table. True if it changed.</summary>
    public static bool Apply()
    {
        var text = ModContent.Text;
        string wanted = Wanted();
        if (text == null || wanted == text.Language) return false;
        string old = text.Language;
        text.Language = wanted;
        RLog.Msg($"Champions of the Sons: language {old} -> {wanted} (game locale '{GameLocale}', setting '{Setting?.Value}')");
        Relabel(old, wanted);
        RefreshPanels();
        return true;
    }

    /// <summary>Labels set once when a panel was built (titles, buttons, item names in cached cells) are whole table strings:
    /// swap each for the same key in the new language. Composite text is rebuilt by the panels' own refresh.</summary>
    private static void Relabel(string from, string to)
    {
        int changed = 0;
        foreach (var root in Roots())
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var next = ModContent.Text.Retranslate(t.text, from, to);
                if (next == null || next == t.text) continue;
                t.text = next;
                changed++;
            }
        RLog.Msg($"Champions of the Sons: re-labelled {changed} texts");
    }

    private static IEnumerable<Transform> Roots()
    {
        var viewport = SUI.SUI.SUIViewport;
        if (viewport != null) yield return viewport.transform;
        var book = GameObject.Find("cots-book-rig");
        if (book != null) yield return book.transform;
    }

    private static void RefreshPanels()
    {
        try
        {
            if (UI.CotsUi.Inventory is { IsOpen: true } inv) inv.RefreshAll();
            if (Perks.PerkPanel.Instance is { IsOpen: true } perks) perks.Refresh();
            if (Crafting.CraftingPanel.Instance is { IsOpen: true } craft) craft.Refresh();
            Book.BookPages.MarkDirty();
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: panel refresh after a language change failed: {e.Message}");
        }
    }
}
