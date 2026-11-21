using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// Merge mode on the bag (COS-144, Max's layout E): the Merge button beside Sort starts it from the selected bag item. That item
/// stays picked; items that cannot join it dim; the player clicks matching items up to the row's count and presses Merge.
/// The result takes the start item's slot with a NEW tag (memory only). If more of the kind remain, the mode stays on with the
/// highest one as the next start item; otherwise it ends with the result selected.
/// </summary>
internal static class BookMerge
{
    private static Merger? _merger;
    private static Merger M => _merger ??= new Merger(ModContent.Db, new LootRoller(ModContent.Db));
    private static Inventory Inv => PlayerState.Character.Inventory;

    /// <summary>Merged items still showing their NEW tag, by reference (the tag follows the item when the bag is sorted).</summary>
    private static readonly HashSet<ItemInstance> NewItems = new(ReferenceEqualityComparer.Instance);

    public static bool Active { get; private set; }
    public static MergeKind Kind { get; private set; }
    /// <summary>The item merge mode started from (or the next batch's start item); it cannot be unpicked.</summary>
    public static string? Anchor { get; private set; }
    public static readonly List<string> Picks = new();

    public static int Need => Merger.Need(Kind.Rarity);
    public static bool Ready => Active && Picks.Count == Need;
    public static int InBag => Active ? M.Matching(Inv, Kind).Count : 0;

    /// <summary>Whether merge mode can start from this slot: a bag slot holding an item that can merge.</summary>
    public static bool CanStart(string? slot) => slot != null && Merger.IsBagSlot(slot) && M.KindOf(Inv.Get(slot)) != null;

    public static bool Start(string slot)
    {
        if (!CanStart(slot)) return false;
        Kind = M.KindOf(Inv.Get(slot))!.Value;
        Active = true;
        Anchor = slot;
        Picks.Clear();
        Picks.Add(slot);
        return true;
    }

    /// <summary>Leaves merge mode; returns the start item's slot so the caller can select it again.</summary>
    public static string? Cancel()
    {
        var anchor = Anchor;
        Active = false;
        Anchor = null;
        Picks.Clear();
        return anchor;
    }

    public static bool Matches(string key) => Active && Merger.IsBagSlot(key) && M.Matches(Inv.Get(key), Kind);

    /// <summary>A click on a bag slot in merge mode: adds or removes a matching item. Returns what happened.</summary>
    public static string Toggle(string key)
    {
        if (!Active) return "none";
        if (key == Anchor) return "anchor";
        if (!Matches(key)) return "no-match";
        if (Picks.Remove(key)) return "unpick";
        if (Picks.Count >= Need) return "full";
        Picks.Add(key);
        return "pick";
    }

    /// <summary>Merges the picked items. Null when the batch is not complete.</summary>
    public static MergeResult? Go()
    {
        if (!Ready) return null;
        var result = M.Merge(Inv, Picks.ToList(), Rng.FromTime(), Anchor);
        if (!result.Ok) return result;
        NewItems.Add(result.Item!);
        int count = Picks.Count;
        LootFeature.Message(string.Format(UiText.T("ui.merge.done", "Merged {0} items into {1}, level {2}."), count, UiText.ItemName(result.Item!), result.Item!.Level));
        Audio.ModAudio.Play(Audio.SoundCues.Craft);
        Picks.Clear();
        var next = M.Matching(Inv, Kind).FirstOrDefault(k => !IsNew(Inv.Get(k)));
        if (next != null)
        {
            Anchor = next;
            Picks.Add(next);
        }
        else
        {
            Active = false;
            Anchor = null;
        }
        return result;
    }

    /// <summary>Drops picks that no longer hold a matching item (the bag changed under merge mode); ends it if the start item went.</summary>
    public static void Validate()
    {
        if (!Active) return;
        if (Anchor == null || !Matches(Anchor)) { Cancel(); return; }
        Picks.RemoveAll(k => !Matches(k));
        if (!Picks.Contains(Anchor)) Picks.Insert(0, Anchor);
    }

    /// <summary>One-click merge (self-tests run every row through it): the item plus the highest-level others of its kind.</summary>
    public static MergeResult? QuickMerge(string slot)
    {
        if (Active) return null;
        var kind = M.KindOf(Inv.Get(slot));
        if (kind == null || !Merger.IsBagSlot(slot)) return null;
        int need = Merger.Need(kind.Value.Rarity);
        var picks = new List<string> { slot };
        picks.AddRange(M.Matching(Inv, kind.Value).Where(k => k != slot).Take(need - 1));
        if (picks.Count < need) return null;
        var result = M.Merge(Inv, picks, Rng.FromTime(), slot);
        if (!result.Ok) return result;
        NewItems.Add(result.Item!);
        LootFeature.Message(string.Format(UiText.T("ui.merge.done", "Merged {0} items into {1}, level {2}."), need, UiText.ItemName(result.Item!), result.Item!.Level));
        Audio.ModAudio.Play(Audio.SoundCues.Craft);
        return result;
    }

    public static bool IsNew(ItemInstance? item) => item != null && NewItems.Contains(item);

    public static bool ClearNew(ItemInstance? item) => item != null && NewItems.Remove(item);

    /// <summary>"5 Magic Boots into 1 Rare, level 31" or "3 Legendary into 1 random Legendary, level 40".</summary>
    public static string RuleText()
    {
        if (!Active) return "";
        string rule = Kind.AnyType
            ? UiText.T("ui.merge.rule_legendary", "3 Legendary into 1 random Legendary")
            : string.Format(UiText.T("ui.merge.rule", "{0} {1} {2} into 1 {3}"), Need, UiText.RarityName(Kind.Rarity), UiText.TypeName(Kind.Type),
                UiText.RarityName((Rarity)Math.Min((int)Kind.Rarity + 1, Rarities.Max)));
        int level = Picks.Select(k => Inv.Get(k)?.Level ?? int.MaxValue).DefaultIfEmpty(int.MaxValue).Min();
        return level == int.MaxValue ? rule : rule + ", " + string.Format(UiText.T("ui.merge.level", "level {0}"), level);
    }

    /// <summary>"3/5 picked, 8 in bag".</summary>
    public static string PickedText() =>
        Active ? string.Format(UiText.T("ui.merge.picked", "{0}/{1} picked, {2} in bag"), Picks.Count, Need, InBag) : "";
}
