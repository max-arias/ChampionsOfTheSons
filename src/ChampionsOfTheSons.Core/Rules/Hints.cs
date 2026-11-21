namespace ChampionsOfTheSons.Core.Rules;

/// <summary>What the hint rule looks at; the bridge fills it from the character and the session.</summary>
public sealed record HintContext(
    int Level,
    int UnspentPerkPoints,
    int BagItems,
    int EquippedItems,
    int SpellsUnlocked,
    int SpellsEquipped,
    bool InCoop,
    bool HasMergeSet,
    bool HasUsableItem,
    bool StreakSeen);

/// <summary>One hint: a stable id (text key <c>ui.hints.&lt;id&gt;</c>) and when it applies.</summary>
public sealed record Hint(string Id, Func<HintContext, bool> When);

/// <summary>
/// One-time hints (COS-97, Max 2026-11-21: toasts, not a pause-menu tip). Each is shown once per install, the first time its
/// condition holds; the list order is the priority. Our own wording lives in the text tables.
/// </summary>
public static class Hints
{
    public static readonly IReadOnlyList<Hint> All = new Hint[]
    {
        new("welcome", c => true),
        new("perk_points", c => c.UnspentPerkPoints > 0),
        new("first_loot", c => c.BagItems > 0 && c.EquippedItems == 0),
        new("spell_unlocked", c => c.SpellsUnlocked > 0 && c.SpellsEquipped == 0),
        new("no_spell", c => c.Level >= 10 && c.SpellsUnlocked == 0),
        new("merge", c => c.HasMergeSet),
        new("use_item", c => c.HasUsableItem),
        new("streak", c => c.StreakSeen),
        new("coop", c => c.InCoop),
        new("settings", c => c.Level >= 3),
    };

    /// <summary>The first hint whose condition holds and that was not shown yet, or null.</summary>
    public static string? Next(HintContext context, IReadOnlySet<string> seen)
    {
        foreach (var h in All)
            if (!seen.Contains(h.Id) && h.When(context)) return h.Id;
        return null;
    }
}
