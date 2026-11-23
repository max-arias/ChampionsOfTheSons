using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;

namespace ChampionsOfTheSons.Mod.Perks;

public enum PerkState
{
    Bought,
    Available,
    NeedPoints,
    Locked,
}

/// <summary>
/// What the book's Perks, Spells and Character pages (COS-168) and the card beside the cursor say about a perk, a spell or an
/// attribute: names, state, cost and why it can't be bought. Shared so the page and the card never disagree.
/// </summary>
internal static class PerkInfo
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public static readonly PerkCategory[] Trees =
        { PerkCategory.MeleeOffense, PerkCategory.RangedOffense, PerkCategory.MagicOffense, PerkCategory.Defense, PerkCategory.Support, PerkCategory.Utility };

    public static string CategoryName(PerkCategory c) => UiText.T("ui.perks.cat." + c.ToString().ToLowerInvariant(), c switch
    {
        PerkCategory.MeleeOffense => "Melee",
        PerkCategory.RangedOffense => "Ranged",
        PerkCategory.MagicOffense => "Magic",
        PerkCategory.Defense => "Defense",
        PerkCategory.Support => "Support",
        _ => "Utility",
    });

    public static string Name(string id, string fallback) => ModContent.Text.Get(id + ".name", fallback);

    public static PerkState StateOf(string perkId) =>
        Pc.Perks.TimesBought(perkId) > 0 ? PerkState.Bought : Pc.CanBuyPerk(perkId) switch
        {
            BuyResult.Ok => PerkState.Available,
            BuyResult.NotEnoughPoints => PerkState.NeedPoints,
            BuyResult.AlreadyBought => PerkState.Bought,
            _ => PerkState.Locked,
        };

    /// <summary>Card text for a perk: title and body (cost, level, flavor, effect, why it can't be bought).</summary>
    public static (string Title, string Body) Perk(string id, string bad, string faint)
    {
        if (!ModContent.Db.Perks.TryGetValue(id, out var perk)) return ("", "");
        int times = Pc.Perks.TimesBought(id);
        string title = $"<b>{Name(id, perk.Name)}</b>" + (times > 0 ? $"  ×{times}" : "");
        var body = new System.Text.StringBuilder();
        body.Append($"<color={faint}>{CategoryName(perk.Category)}  ·  {UiText.T("ui.perks.cost", "Cost")} {perk.Cost}  ·  {UiText.T("ui.sheet.level", "Level")} {perk.LevelReq}");
        if (perk.Stackable) body.Append($"  ·  {UiText.T("ui.perks.stackable", "Stackable")}");
        body.Append("</color>\n");
        var flavor = ModContent.Text.Get(id + ".flavor", "");
        if (flavor.Length > 0) body.Append($"<i>{flavor}</i>\n");
        body.Append(ModContent.Text.Get(id + ".effect", ""));
        if (perk.Manual && perk.Effects.Count == 0)
            body.Append($"\n<color={faint}>{UiText.T("ui.perks.manual", "Effect applied by the game hooks; not active yet.")}</color>");
        string why = Pc.CanBuyPerk(id) switch
        {
            BuyResult.Ok => $"<color=#3E6B2A>{string.Format(UiText.T("ui.perks.buy", "Buy ({0} pt)"), perk.Cost)}: {UiText.T("ui.book.perks.click_buy", "click again or right-click")}</color>",
            BuyResult.AlreadyBought => UiText.T("ui.perks.owned", "Already unlocked."),
            BuyResult.LevelTooLow => $"<color={bad}>{string.Format(UiText.T("ui.perks.need_level", "Needs level {0}."), perk.LevelReq)}</color>",
            BuyResult.PathLocked => $"<color={bad}>{string.Format(UiText.T("ui.perks.need_path", "Unlock first: {0}"), NameList(perk.UnlockPath))}</color>",
            BuyResult.RequirementsMissing => $"<color={bad}>{string.Format(UiText.T("ui.perks.need_perks", "Requires: {0}"), NameList(perk.Requires.Where(r => Pc.Perks.TimesBought(r) == 0)))}</color>",
            BuyResult.NotEnoughPoints => $"<color={bad}>{UiText.T("ui.perks.need_points", "Not enough perk points.")}</color>",
            _ => $"<color={bad}>{UiText.T("ui.perks.not_buyable", "Can't be bought right now.")}</color>",
        };
        body.Append("\n\n").Append(why);
        return (title, body.ToString());
    }

    private static string NameList(IEnumerable<string> ids) => string.Join(", ", ids.Select(i => ModContent.Text.Get(i + ".name", i)));

    /// <summary>COS-172: the spell costs more than the whole energy bar, so it can never be cast as things stand.</summary>
    public static bool TooDear(SpellDef def) => Pc.Spells.CostOf(def.Id, Pc.Stats).Total > Spells.SpellCaster.MaxEnergy + 0.01;

    /// <summary>Level, energy and cooldown as they are for this player, with the base value after it when they differ (COS-97).</summary>
    public static string SpellMeta(SpellDef def, string faint)
    {
        double cost = Pc.Spells.CostOf(def.Id, Pc.Stats).Total;
        double cd = Pc.Spells.EffectiveCooldown(def.Id, Pc.Stats);
        string Was(double now, double baseValue, string fmt) => Math.Abs(now - baseValue) < 0.05 ? "" : $" <color={faint}>({baseValue.ToString(fmt, Inv)})</color>";
        return $"{UiText.T("ui.sheet.level", "Level")} {def.LevelReq}  ·  {cost.ToString("0", Inv)}{Was(cost, def.EnergyCost, "0")} {UiText.T("ui.spells.energy", "energy")}"
            + $"  ·  {cd.ToString("0.#", Inv)} s{Was(cd, def.Cooldown, "0.#")}";
    }

    public static string TooDearText() =>
        string.Format(UiText.T("ui.spells.needs_energy", "Costs more than your max energy ({0}): raise max energy to cast it"), Spells.SpellCaster.MaxEnergy.ToString("0", Inv));

    /// <summary>Card text for a spell: its numbers, description, mastery and its upgrades (COS-176).</summary>
    public static (string Title, string Body) Spell(string id, string bad, string faint)
    {
        if (!ModContent.Db.Spells.TryGetValue(id, out var def)) return ("", "");
        var body = $"<color={faint}>{SpellMeta(def, faint)}</color>\n" + ModContent.Text.Get(id + ".description", "");
        if (TooDear(def)) body += $"\n\n<color={bad}>{TooDearText()}</color>";
        body += "\n\n" + MasteryLine(def.Id);
        int level = Pc.Mastery.Level(def.Id);
        for (int i = 0; i < def.Mastery.Count; i++)
        {
            string perk = def.Mastery[i];
            int at = SpellMastery.UnlockLevel(i, def.Mastery.Count);
            string line = $"{Name(perk, perk)}: {ModContent.Text.Get(perk + ".effect", "")}";
            body += level >= at ? $"\n<color=#3E6B2A>+ {line}</color>" : $"\n<color={faint}>{string.Format(UiText.T("ui.book.mastery.at", "At mastery {0}"), at)}: {line}</color>";
        }
        return ($"<b>{Name(id, def.Name)}</b>", body);
    }

    /// <summary>"Mastery 2/5 · 12/35 casts" (COS-176).</summary>
    public static string MasteryLine(string spellId)
    {
        int casts = Pc.Mastery.CastsOf(spellId), level = SpellMastery.LevelFor(casts);
        var next = SpellMastery.NextThreshold(level);
        return string.Format(UiText.T("ui.book.mastery.line", "Mastery {0}/{1}"), level, SpellMastery.MaxLevel)
            + "  ·  " + (next is int n ? string.Format(UiText.T("ui.book.mastery.casts", "{0}/{1} casts"), casts, n) : UiText.T("ui.book.mastery.max", "fully mastered"));
    }

    /// <summary>The message when a spell reaches a mastery level, with what it unlocked.</summary>
    public static string MasteryUpText(string spellId, int level)
    {
        var def = ModContent.Db.Spells[spellId];
        var unlocked = Enumerable.Range(0, def.Mastery.Count).Where(i => SpellMastery.UnlockLevel(i, def.Mastery.Count) == level)
            .Select(i => Name(def.Mastery[i], def.Mastery[i])).ToList();
        string text = string.Format(UiText.T("ui.book.mastery.up", "{0}: mastery {1}"), Name(spellId, def.Name), level);
        return unlocked.Count == 0 ? text : text + " - " + string.Join(", ", unlocked);
    }

    // ------------------------------------------------------------------ attributes (COS-174)

    public static string AttributeName(string id) => UiText.T("ui.book.attr." + id, id switch
    {
        StatIds.Strength => "Strength",
        StatIds.Agility => "Agility",
        StatIds.Vitality => "Vitality",
        _ => "Intelligence",
    });

    /// <summary>What one point gives, for the row and the card.</summary>
    public static string AttributePerPoint(string id) => id switch
    {
        StatIds.Strength => string.Format(UiText.T("ui.book.attr.strength.per", "Each point: melee damage +{0}%, carry 1 more of each kind per {1} points"),
            (AttributeRules.MeleePerStrength * 100).ToString("0.##", Inv), (1 / AttributeRules.CarryPerStrength).ToString("0", Inv)),
        StatIds.Vitality => string.Format(UiText.T("ui.book.attr.vitality.per", "Each point: max health +{0}, health regeneration +{1}%"),
            AttributeRules.HealthPerVitality.ToString("0.##", Inv), (AttributeRules.RegenPerVitality * 100).ToString("0.##", Inv)),
        StatIds.Agility => string.Format(UiText.T("ui.book.attr.agility.per", "Each point: ranged damage +{0}%, attack speed +{1}%, max energy +{2}"),
            (AttributeRules.RangedPerAgility * 100).ToString("0.##", Inv), (AttributeRules.AttackSpeedPerAgility * 100).ToString("0.##", Inv), AttributeRules.EnergyPerAgility.ToString("0.##", Inv)),
        _ => string.Format(UiText.T("ui.book.attr.intelligence.per", "Each point: spell damage +{0}%, energy regeneration +{1}%, spell cooldowns -{2}% (at most -{3}%)"),
            (AttributeRules.SpellPerIntelligence * 100).ToString("0.##", Inv), (AttributeRules.EnergyRegenPerIntelligence * 100).ToString("0.##", Inv),
            (AttributeRules.CooldownPerIntelligence * 100).ToString("0.##", Inv), (AttributeRules.MaxCooldownCut * 100).ToString("0", Inv)),
    };

    /// <summary>Card text for an attribute row.</summary>
    public static (string Title, string Body) Attribute(string id, string faint)
    {
        int spent = Pc.Attributes.Get(id);
        double total = Pc.Stats.Get(id);
        var body = AttributePerPoint(id) + "\n\n" +
                   $"<color={faint}>{string.Format(UiText.T("ui.book.attr.card", "{0} spent; {1} in total with perks. Click + to add one (right-click: 10), - to take one back. Free."), spent, total.ToString("0", Inv))}</color>";
        return ($"<b>{AttributeName(id)}</b>  {total.ToString("0", Inv)}", body);
    }
}
