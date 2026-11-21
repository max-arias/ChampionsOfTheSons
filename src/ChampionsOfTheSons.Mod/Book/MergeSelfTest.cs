using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>Merge (COS-144) has no wiring of its own: it lives on the book's Gear page. This feature only owns its self-test.</summary>
public sealed class MergeFeature : IModFeature
{
    public void RegisterSelfTests() => MergeSelfTest.Register();
}

/// <summary>
/// COS-144 in game: merge mode on the book's Gear page (Merge beside Sort, start item stays picked, non-matching items dim,
/// Merge n/N, Cancel, NEW tag) and every row once. Items come from TestKit, never from the test save.
/// </summary>
internal static class MergeSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static string _anchor = "", _other = "";
    private static readonly List<string> _boots = new();

    /// <summary>A fixed equippable base of the type and rarity that can roll at the level.</summary>
    private static string Base(string? type, int rarity, int level) =>
        ModContent.Db.Items.Values.Where(d => LootRoller.IsEquippable(d) && d.Rarity == rarity && d.MinLevel <= level && (type == null || d.Type == type))
            .OrderBy(d => d.Id, StringComparer.Ordinal).First().Id;

    private static string SlotOf(ItemInstance item) =>
        Pc.Inventory.Slots.First(kv => ReferenceEquals(kv.Value, item)).Key;

    private static PageSlot? Cell(string key) => BookPages.Slots(BookTab.Gear).Concat(BookPages.Slots(BookTab.Bag)).FirstOrDefault(s => s.Key == key);

    public static void Register()
    {
        SelfTest.Add(0.5f, "merge: setup", Setup);
        SelfTest.Add(1f, "merge: open the book", () => CotsUi.ToggleInventory());
        SelfTest.Add(4f, "merge: start mode", StartMode);
        SelfTest.Add(4.3f, "merge: shot merge mode", () => TestKit.Screenshot("merge-mode"));
        SelfTest.Add(4.6f, "merge: pick and merge", PickAndMerge);
        SelfTest.Add(4.9f, "merge: shot after merge", () => TestKit.Screenshot("merge-after"));
        SelfTest.Add(5.2f, "merge: cancel and NEW tag", CancelAndNew);
        SelfTest.Add(5.5f, "merge: every row", EveryRow);
        SelfTest.Add(6f, "merge: close", () => BookHost.Close());
        SelfTest.Add(7f, "merge: cleanup", Cleanup);
    }

    private static void Setup()
    {
        CotsUi.Inventory.Close();
        LootFeature.ClearAll();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        _boots.Clear();
        string boot = Base("boot", 0, 5);
        foreach (int level in new[] { 5, 12, 8, 3 })
            _boots.Add(SlotOf(TestKit.Give(boot, 0, level)));
        _other = SlotOf(TestKit.Give(Base("ring", 0, 5), 0, 9));
        _anchor = _boots[0];
        SelfTest.Check("merge: four Common boots and a Common ring in the bag", Pc.Inventory.Slots.Count == 5, Pc.Inventory.Slots.Count.ToString());
    }

    private static void StartMode()
    {
        BookHost.ShowModPage(BookTab.Gear);
        BookPages.Selected = null;
        SelfTest.Check("merge: Merge does nothing with no item selected", BookPages.Click(BookTab.Gear, "merge.start", false) == "merge-off" && !BookMerge.Active);
        BookPages.Click(BookTab.Gear, _anchor, false);
        string started = BookPages.Click(BookTab.Gear, "merge.start", false);
        BookPages.Refresh(BookTab.Gear);
        SelfTest.Check("merge: Merge starts merge mode from the selected item", started == "merge-start" && BookMerge.Active && BookMerge.Picks.SequenceEqual(new[] { _anchor }), started);
        SelfTest.Check("merge: an item of another type dims", Cell(_other)?.Dim.enabled == true);
        SelfTest.Check("merge: matching items stay lit", Cell(_boots[1])?.Dim.enabled == false && Cell(_anchor)?.TickBg.enabled == true);
    }

    private static void PickAndMerge()
    {
        SelfTest.Check("merge: a dimmed item ignores clicks", BookPages.Click(BookTab.Gear, _other, false) == "merge-no-match");
        SelfTest.Check("merge: the start item cannot be unpicked", BookPages.Click(BookTab.Gear, _anchor, false) == "merge-anchor" && BookMerge.Picks.Contains(_anchor));
        int before = Pc.Inventory.Slots.Count;
        SelfTest.Check("merge: Merge is refused before the batch is full", BookPages.Click(BookTab.Gear, "merge.go", false) == "merge-not-ready" && Pc.Inventory.Slots.Count == before);
        BookPages.Click(BookTab.Gear, _boots[1], false);
        BookPages.Click(BookTab.Gear, _boots[2], false);
        SelfTest.Check("merge: a fourth pick is refused when the row needs three", BookPages.Click(BookTab.Gear, _boots[3], false) == "merge-full");
        string merged = BookPages.Click(BookTab.Gear, "merge.go", false);
        BookPages.Refresh(BookTab.Gear);
        var result = Pc.Inventory.Get(_anchor);
        SelfTest.Check("merge: three Commons became a Magic of their type in the start item's slot", merged == "merged" && result != null && result.Rarity == Rarity.Magic
            && ModContent.Db.Items[result.BaseId].Type == "boot", $"{merged}: {result?.BaseId} r{(int?)result?.Rarity}");
        SelfTest.Check("merge: the result has the lowest input level", result?.Level == 5, result?.Level.ToString());
        SelfTest.Check("merge: the result shows NEW", BookMerge.IsNew(result) && Cell(_anchor)?.NewBg.enabled == true);
        SelfTest.Check("merge: one boot left becomes the next start item", BookMerge.Active && BookMerge.Anchor == _boots[3], BookMerge.Anchor ?? "none");
    }

    private static void CancelAndNew()
    {
        string cancelled = BookPages.Click(BookTab.Gear, "merge.cancel", false);
        SelfTest.Check("merge: Cancel leaves merge mode with the start item selected", cancelled == "merge-cancel" && !BookMerge.Active && BookPages.Selected == _boots[3]);
        var result = Pc.Inventory.Get(_anchor);
        BookPages.Click(BookTab.Gear, _anchor, false);
        BookPages.Refresh(BookTab.Gear);
        SelfTest.Check("merge: clicking the result clears NEW", !BookMerge.IsNew(result) && Cell(_anchor)?.NewBg.enabled == false);
    }

    /// <summary>Each row once through the one-click path (the screen panel's menu uses it too).</summary>
    private static void EveryRow()
    {
        foreach (var (rarity, type) in new[] { (1, "ring"), (2, "helmet"), (3, (string?)null) })
        {
            Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
            int need = Merger.Need((Rarity)rarity);
            var given = new List<ItemInstance>();
            for (int i = 0; i < need; i++)
                given.Add(TestKit.Give(type == null ? Base(null, 3, 30 + i) : Base(type, rarity, 30), rarity, 30 + i));
            string slot = SlotOf(given[0]);
            var result = BookMerge.QuickMerge(slot);
            var item = result?.Item;
            bool typeOk = type == null ? !given.Any(g => g.BaseId == item?.BaseId) : item != null && ModContent.Db.Items[item.BaseId].Type == type;
            SelfTest.Check($"merge: {need} x {(Rarity)rarity} give one {(Rarity)Math.Min(rarity + 1, 3)}", result is { Ok: true } && item!.Rarity == (Rarity)Math.Min(rarity + 1, 3)
                && typeOk && item.Level == 30 && Pc.Inventory.Slots.Count == 1 && Pc.Inventory.Get(slot) == item, $"{result?.Error} {item?.BaseId} lvl {item?.Level}");
        }
    }

    private static void Cleanup()
    {
        if (BookMerge.Active) BookMerge.Cancel();
        BookCard.Hide();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        LootFeature.ClearAll();
    }
}
