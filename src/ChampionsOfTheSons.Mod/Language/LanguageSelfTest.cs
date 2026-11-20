using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Mod.UI;
using TMPro;

namespace ChampionsOfTheSons.Mod.Language;

/// <summary>COS-74: switching to a non-English table changes UI strings, item names and labels already on screen.</summary>
internal static class LanguageSelfTest
{
    private static string Lang = "de";

    public static void Register()
    {
        SelfTest.Add(6f, "language: follows the game", FollowsGame);
        SelfTest.Add(6.5f, "language: switch to another table", SwitchAndCheck);
        SelfTest.Add(7f, "language: back", Back);
    }

    private static void FollowsGame()
    {
        SelfTest.Log($"language: game locale '{LanguageFeature.GameLocale}', mod table '{ModContent.Text.Language}'");
        SelfTest.Check("language: the mod table matches the game's locale", LanguageFeature.TestOverride != null
            || LanguageFeature.Setting.Value != LanguageFeature.Auto || ModContent.Text.Language == TextDb.MatchLanguage(LanguageFeature.GameLocale));
    }

    private static void SwitchAndCheck()
    {
        var text = ModContent.Text;
        string before = text.Language;
        Lang = before == "de" ? "fr" : "de";
        string titleBefore = UiText.T("ui.inventory.title", "Inventory & Character");
        // An item whose name differs between the current table and the target one.
        var item = ModContent.Db.Items.Values.First(i =>
            text.Has(i.Id + ".name", Lang) && text.Retranslate(text.Get(i.Id + ".name"), before, Lang) is { } n && n != text.Get(i.Id + ".name"));
        LanguageFeature.TestOverride = Lang;
        bool changed = LanguageFeature.Apply();
        string title = UiText.T("ui.inventory.title", "Inventory & Character");
        string name = UiText.ItemName(new Core.ItemInstance { BaseId = item.Id });
        SelfTest.Check($"language: switched to {Lang}", changed && text.Language == Lang, text.Language);
        SelfTest.Check("language: a UI string is translated", title != titleBefore && text.Has("ui.inventory.title", Lang), title);
        SelfTest.Check("language: an item name is translated", name == text.Get(item.Id + ".name") && name != text.Retranslate(name, Lang, before), name);
        var labels = SUI.SUI.SUIViewport.transform.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).ToList();
        SelfTest.Check("language: a built panel label was re-labelled", labels.Contains(title) && !labels.Contains(titleBefore), title);
    }

    private static void Back()
    {
        LanguageFeature.TestOverride = null;
        LanguageFeature.Apply();
        SelfTest.Check("language: back to the game's language", ModContent.Text.Language == LanguageFeature.Wanted(), ModContent.Text.Language);
    }
}
