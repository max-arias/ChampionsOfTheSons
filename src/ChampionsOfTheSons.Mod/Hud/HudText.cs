using System.Text;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>HUD strings (keys ui.hud.*, English fallbacks in code and in content/text/en.hud.json).</summary>
internal static class HudText
{
    public static string Level => UiText.T("ui.hud.level", "Lv");

    public static string Tier(EnemyTier t) => t switch
    {
        EnemyTier.Elite => UiText.T("ui.hud.tier.elite", "Elite"),
        EnemyTier.Miniboss => UiText.T("ui.hud.tier.miniboss", "Miniboss"),
        EnemyTier.Boss => UiText.T("ui.hud.tier.boss", "Boss"),
        _ => "",
    };

    public static string Ability(EnemyAbility a) => UiText.T("ui.hud.ability." + a.ToString().ToLowerInvariant(), Spaced(a.ToString()));

    public static string Affixes(IReadOnlyList<EnemyAbility> abilities)
    {
        if (abilities.Count == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < abilities.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Ability(abilities[i]));
        }
        return sb.ToString();
    }

    private static string Spaced(string pascal)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append(' ');
            sb.Append(pascal[i]);
        }
        return sb.ToString();
    }

    /// <summary>Display name of an item base id (for level-up rewards).</summary>
    public static string ItemName(string baseId) =>
        ModContent.Text.Get(baseId + ".name", ModContent.Db.Items.TryGetValue(baseId, out var d) ? d.Name : baseId);
}
