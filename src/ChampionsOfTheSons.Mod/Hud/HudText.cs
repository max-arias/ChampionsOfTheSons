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

    /// <summary>"Elite Cannibal": tier word (none for normal enemies) and the kind's translated name.</summary>
    public static string KindTitle(EnemyProfile profile)
    {
        // Local profiles carry the kind id, synced ones the actor type (content keys kinds by it): both resolve here.
        EnemyKind? kind = ModContent.Db.Enemies.TryGetValue(profile.KindId, out var byActor) ? byActor : null;
        if (kind == null)
            foreach (var k in ModContent.Db.Enemies.Values)
                if (k.Id == profile.KindId) { kind = k; break; }
        string kindName = kind == null ? profile.KindId : UiText.T(kind.Id + ".name", kind.Name);
        return profile.Tier == EnemyTier.Normal ? kindName : Tier(profile.Tier) + " " + kindName;
    }

    /// <summary>"Ashjaw the Unburied", or null when the enemy has no personal name (COS-68).</summary>
    public static string? PersonalName(EnemyProfile profile)
    {
        var n = profile.Name;
        if (n == null) return null;
        string Part(string id) => UiText.T(id + ".name", ModContent.Db.EliteNames.TryGetValue(id, out var d) ? d.Name : id);
        return string.Format(UiText.T("ui.elites.format", "{0} {1}"), Part(n.GivenId), Part(n.EpithetId));
    }

    public static string Danger(DangerRating r) => r switch
    {
        DangerRating.Trivial => UiText.T("ui.scanner.danger.trivial", "Beneath you"),
        DangerRating.Even => UiText.T("ui.scanner.danger.even", "Fair fight"),
        DangerRating.Tough => UiText.T("ui.scanner.danger.tough", "Tough"),
        DangerRating.Dangerous => UiText.T("ui.scanner.danger.dangerous", "Dangerous"),
        _ => UiText.T("ui.scanner.danger.deadly", "Deadly"),
    };

    /// <summary>Tally-mark color per danger rating: bone grey up through hazard yellow and tarp orange to red.</summary>
    public static string DangerHex(DangerRating r) => r switch
    {
        DangerRating.Trivial => "#7D7A72",
        DangerRating.Even => "#D9CFB8",
        DangerRating.Tough => "#E0B23A",
        DangerRating.Dangerous => "#E8742F",
        _ => "#E0442F",
    };

    /// <summary>One line per affix for the scanner: name, then what it does, in our own words.</summary>
    public static string AffixExplained(EnemyAbility a) =>
        UiText.T("ui.scanner.affix." + a.ToString().ToLowerInvariant(), "");

    /// <summary>Display name of an item base id (for level-up rewards).</summary>
    public static string ItemName(string baseId) =>
        ModContent.Text.Get(baseId + ".name", ModContent.Db.Items.TryGetValue(baseId, out var d) ? d.Name : baseId);
}
