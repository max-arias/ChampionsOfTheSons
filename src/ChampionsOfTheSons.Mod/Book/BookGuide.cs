using System.Text;
using ChampionsOfTheSons.Mod.Settings;
using ChampionsOfTheSons.Mod.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// The Help tab's pages (COS-97, Max 2026-11-20: a tab after Character). One short page per topic in our own words; the
/// Keys page lists the player's current bindings, read live so it stays right after a rebind.
/// </summary>
internal static class BookGuide
{
    /// <summary>Topic id, then the English title and body (fallbacks for the <c>ui.guide.*</c> strings).</summary>
    private static readonly (string Id, string Title, string Body)[] Topics =
    {
        ("welcome", "Champions of the Sons",
            "An RPG layer over the island. Every enemy has a level, many are elites with abilities, and every kill gives XP. " +
            "Levels give perk points, and enemies drop gear with rolled stats.\n\nTurn the pages for the keys and each system. " +
            "This tab is always here."),
        ("keys", "Keys", ""),
        ("levels", "Levels and XP",
            "Kills give XP. Kill again quickly to build a massacre streak: the XP is banked and multiplied when the streak ends.\n\n" +
            "Each level gives a perk point and raises your attributes. Dying costs part of the XP in your current level, never a level."),
        ("loot", "Loot",
            "Enemies drop gear when they die; elites and bosses drop more. Walk over a drop to pick it up.\n\n" +
            "Rarity sets how strong the rolls are: Common (white), Magic (blue), Rare (yellow) and Legendary (red-orange). " +
            "Uniques are Legendary and carry a special effect. Magic find raises the rarity of drops."),
        ("perks", "Perks and spells",
            "Spend perk points in the perk tree ({0}). Some perks unlock spells: put them on the spell bar and cast them with the spell keys.\n\n" +
            "Spells cost energy, which is your stamina bar, and each has a cooldown."),
        ("enemies", "Enemies",
            "Enemies match the level of the players near them. The game's difficulty adds levels and makes elites more common.\n\n" +
            "Elites, minibosses and bosses are tougher and use abilities, named on their nameplate. Look at an enemy to read more about it."),
    };

    public static int Pages => Topics.Length;

    /// <summary>Title and rich-text body of a page; <paramref name="accent"/> and <paramref name="faint"/> are "#rrggbb".</summary>
    public static (string Title, string Body) Page(int index, string accent, string faint)
    {
        var (id, title, body) = Topics[Math.Clamp(index, 0, Topics.Length - 1)];
        string t = UiText.T($"ui.guide.{id}.title", title);
        if (id == "keys") return (t, KeysBody(accent, faint));
        string b = UiText.T($"ui.guide.{id}.body", body);
        string key = id switch { "perks" => "perks_key", _ => "" };
        if (key.Length > 0) b = string.Format(b, $"<color={accent}><b>{KeyNames.Short(ModKeys.Value(key))}</b></color>");
        return (t, b);
    }

    /// <summary>The Keys page rows: each action's name and its current key.</summary>
    public static List<(string Name, string Key)> KeyRows() =>
        ModKeys.All.Select(k => (ModKeys.Label(k), KeyNames.Short(ModKeys.Value(k.Id)))).ToList();

    public static string KeysFooter(string faint) =>
        $"<color={faint}>" + string.Format(UiText.T("ui.guide.keys.footer", "Change any key in the settings menu ({0}). On a gamepad, LB + Select opens it."),
            KeyNames.Short(ModKeys.Value("settings_key"))) + "</color>";

    private static string KeysBody(string accent, string faint) =>
        string.Join(NL, KeyRows().Select(r => $"{r.Name}  <color={accent}>{r.Key}</color>")) + NL + NL + KeysFooter(faint);

    private const string NL = "\n";
}
