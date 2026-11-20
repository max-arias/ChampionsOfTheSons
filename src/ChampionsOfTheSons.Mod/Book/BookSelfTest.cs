using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.UI;
using Sons.Ai.Vail;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// End to end (COS-59): enemies die and drop random gear, the player walks over the drops, the items show on the
/// guide book's Bag page, and can be selected, equipped, unequipped, sorted, dropped and destroyed from the book.
/// Screenshots of each tab go to %TEMP%\cots-ui\ (windowed run).
/// </summary>
internal static class BookSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static GroundLoot Ground => LootFeature.Ground;
    private static WorldRules? _savedRules;
    private static Vector3 _home;
    private static readonly List<VailActor> Victims = new();
    private static int _dropped, _picked;
    private static ItemInstance? _equippedItem;
    private static string? _equippedSlot;
    private static double _statBefore;

    public static void Register()
    {
        SelfTest.Add(0.5f, "book: unpause", Unpause);
        SelfTest.Add(1f, "book: setup", Setup);
        SelfTest.Add(1.5f, "book: kill for drops", Kill);
        SelfTest.Add(5f, "book: drops are gear", CheckDrops);
        for (int i = 0; i < 14; i++) SelfTest.Add(5.5f + i * 0.35f, "book: walk over drops", WalkToNextDrop);
        SelfTest.Add(10.6f, "book: picked up", CheckPickedUp);
        SelfTest.Add(11f, "book: open with the inventory key", () => CotsUi.ToggleInventory());
        SelfTest.Add(14f, "book: gear tab", GearTab);
        SelfTest.Add(14.2f, "book: shot gear", () => TestKit.Screenshot("book-gear"));
        SelfTest.Add(14.5f, "book: bag tab", BagTab);
        SelfTest.Add(15.2f, "book: shot bag", () => TestKit.Screenshot("book-bag-1"));
        SelfTest.Add(15.5f, "book: cursor maps onto slots", CursorMapping);
        SelfTest.Add(16f, "book: select shows the card", Select);
        SelfTest.Add(16.8f, "book: card pinned", CardPinned);
        SelfTest.Add(17f, "book: shot card", () => TestKit.Screenshot("book-card"));
        SelfTest.Add(17.5f, "book: equip from the card", EquipFromCard);
        SelfTest.Add(18.2f, "book: shot gear equipped", () => TestKit.Screenshot("book-gear-equipped"));
        SelfTest.Add(18.5f, "book: right-click unequips", Unequip);
        SelfTest.Add(19f, "book: sort", Sort);
        SelfTest.Add(19.5f, "book: drop and destroy", DropAndDestroy);
        SelfTest.Add(20.5f, "book: character tab", () => BookHost.ShowModPage(BookTab.Character));
        SelfTest.Add(21.2f, "book: character rows", CharacterRows);
        SelfTest.Add(21.3f, "book: shot character 1", () => TestKit.Screenshot("book-character-1"));
        SelfTest.Add(21.6f, "book: character page 2", () => BookPages.Page(BookTab.Character, 1));
        SelfTest.Add(22.2f, "book: shot character 2", () => TestKit.Screenshot("book-character-2"));
        SelfTest.Add(22.5f, "book: game tab gives the page back", GamePage);
        SelfTest.Add(23.2f, "book: shot game page", () => TestKit.Screenshot("book-game-page"));
        SelfTest.Add(23.5f, "book: close", () => BookHost.Close());
        SelfTest.Add(25f, "book: closed and cleanup", Cleanup);
    }

    private static void Unpause()
    {
        // The test save is an early-access save: the game shows a warning over the pause menu and stays paused.
        foreach (var w in UnityEngine.Object.FindObjectsOfType<Sons.Gui.VersionUpgrade.ShowLegacySaveWarning>()) w.gameObject.SetActive(false);
        Sons.Gui.PauseMenu._instance?.Close();
    }

    private static void Setup()
    {
        CotsUi.Inventory.Close();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        LootFeature.ClearAll();
        var save = PlayerState.ToSave();
        save.Level = 30;
        PlayerState.FromSave(save);
        _home = LocalPlayer.Transform.position;
        _savedRules = WorldState.Rules;
        SelfTest.Check("book: start with an empty bag and ground", Pc.Inventory.Slots.Count == 0 && Ground.Count == 0);
    }

    private static void Kill()
    {
        WorldState.SetRulesForTest(_savedRules! with { DropChance = 1000, AllowElites = false });
        Victims.Clear();
        for (int i = 0; i < 3; i++)
        {
            var v = TestKit.Spawn(distance: 7 + i, sideways: (i - 1) * 2.5f);
            if (v != null) Victims.Add(v);
        }
        foreach (var v in Victims) TestKit.Kill(v);
        SelfTest.Check("book: three enemies spawned and killed", Victims.Count == 3, Victims.Count.ToString());
    }

    private static void CheckDrops()
    {
        WorldState.SetRulesForTest(_savedRules!);
        _dropped = Ground.Count;
        var notGear = Ground.All.Where(d => !ModContent.Db.Items.TryGetValue(d.Item.BaseId, out var def) || !LootRoller.IsEquippable(def)).ToList();
        SelfTest.Log("book: drops " + string.Join(", ", Ground.All.Select(d => $"{d.Item.BaseId}/{d.Item.Rarity}/L{d.Item.Level}")));
        SelfTest.Check("book: kills dropped items", _dropped >= 6, $"{_dropped} drops");
        SelfTest.Check("book: every drop is equipment", notGear.Count == 0, string.Join(",", notGear.Select(d => d.Item.BaseId)));
    }

    private static void WalkToNextDrop()
    {
        var d = Ground.All.OrderBy(x => x.Id).FirstOrDefault();
        if (d == null) return;
        LocalPlayer.Transform.position = new Vector3(d.X, d.Y, d.Z);
        Physics.SyncTransforms();
    }

    private static void CheckPickedUp()
    {
        LocalPlayer.Transform.position = _home;
        Physics.SyncTransforms();
        _picked = InventoryActions.BagCount();
        SelfTest.Check("book: every drop was picked up into the bag", Ground.Count == 0 && _picked == _dropped, $"bag {_picked}, dropped {_dropped}, left {Ground.Count}");
    }

    private static void GearTab()
    {
        SelfTest.Check("book: the inventory key opened the book on Gear", BookHost.IsOpen && BookHost.Tab == BookTab.Gear, $"open {BookHost.IsOpen}, tab {BookHost.Tab}");
        SelfTest.Check("book: our tab was added", BookTabs.Tabs.Count == 1, BookTabs.Tabs.Count.ToString());
        SelfTest.Check("book: the screen panel stayed closed", !CotsUi.Inventory.IsOpen);
        SelfTest.Check("book: the game's page actions are blocked", BookPatches.Block);
    }

    private static void BagTab()
    {
        BookHost.ShowModPage(BookTab.Bag);
        BookPages.Refresh(BookTab.Bag);
        int shown = BookPages.Slots(BookTab.Bag).Count(s => s.Shown != null);
        SelfTest.Check("book: the bag page shows the picked-up items", shown == Math.Min(_picked, BookPages.BagPerPage), $"{shown} of {_picked}");
    }

    private static void CursorMapping()
    {
        var book = BookHost.Book;
        var slot = BookPages.Slots(BookTab.Bag)[7];
        if (book == null) { SelfTest.Check("book: held", false); return; }
        var center = new Vector2(slot.X + slot.Size / 2, slot.Y + slot.Size / 2);
        var screen = BookHost.ScreenOfPagePx(center);
        book._mouseScreenPosition = screen;
        bool on = BookHost.CursorOnPage(out var px);
        var hit = on ? BookPages.HitAt(px) : null;
        SelfTest.Check("book: a cursor over a slot hits that slot", hit == slot.Key, $"screen {screen} -> page {px} -> {hit ?? "nothing"}, wanted {slot.Key}");
    }

    private static void Select()
    {
        BookPages.Selected = null;
        string r = BookPages.Click(BookTab.Bag, Inventory.Bag(0), right: false);
        SelfTest.Check("book: clicking a bag item selects it", r == "select" && BookPages.Selected == Inventory.Bag(0), r);
        // Park the cursor off the page so the card shows the selection, not a hover.
        if (BookHost.Book != null) BookHost.Book._mouseScreenPosition = new Vector2(5, 5);
    }

    private static void CardPinned()
    {
        SelfTest.Check("book: the card shows the selected item with actions", BookCard.ShownKey == Inventory.Bag(0) && BookCard.ActionsVisible,
            $"shown {BookCard.ShownKey}, actions {BookCard.ActionsVisible}");
        var page = BookHost.PageScreenRect();
        SelfTest.Log($"book: page on screen {page}, card text {BookCard.Text.Length} chars, {BookCard.Debug}");
    }

    private static void EquipFromCard()
    {
        var item = Pc.Inventory.Get(Inventory.Bag(0));
        _statBefore = Derived.MeleeMultiplier(Pc.Stats) + Derived.MaxHealth(Pc.Stats) + Pc.Stats.Get(StatIds.Armor);
        string r = BookCard.Act("equip", Inventory.Bag(0));
        _equippedItem = item;
        _equippedSlot = Pc.Inventory.Equipped.FirstOrDefault(kv => Equals(kv.Value, item)).Key;
        double after = Derived.MeleeMultiplier(Pc.Stats) + Derived.MaxHealth(Pc.Stats) + Pc.Stats.Get(StatIds.Armor);
        SelfTest.Check("book: Equip in the card puts the item on", r == "equipped" && _equippedSlot != null, $"{r}, slot {_equippedSlot}");
        SelfTest.Log($"book: equipped {item?.BaseId} into {_equippedSlot}; melee+health+armor {_statBefore:0.##} -> {after:0.##}");
        BookHost.ShowModPage(BookTab.Gear);
    }

    private static void Unequip()
    {
        if (_equippedSlot == null) return;
        BookPages.Refresh(BookTab.Gear);
        string r = BookPages.Click(BookTab.Gear, _equippedSlot, right: true);
        bool back = Pc.Inventory.Get(_equippedSlot) == null && Pc.Inventory.Slots.Any(kv => kv.Key.StartsWith("bag.") && Equals(kv.Value, _equippedItem));
        SelfTest.Check("book: right-click on an equipped item puts it back in the bag", r == "toggle" && back, r);
    }

    private static void Sort()
    {
        int before = InventoryActions.BagCount();
        BookHost.ShowModPage(BookTab.Bag);
        string r = BookPages.Click(BookTab.Bag, "sort", right: false);
        bool packed = Enumerable.Range(0, before).All(i => Pc.Inventory.Get(Inventory.Bag(i)) != null);
        SelfTest.Check("book: Sort packs the bag", r == "sort" && packed && InventoryActions.BagCount() == before, $"{r}, {before} items");
    }

    private static void DropAndDestroy()
    {
        int bag = InventoryActions.BagCount(), ground = Ground.Count;
        BookPages.Click(BookTab.Bag, Inventory.Bag(0), right: false);
        string drop = BookCard.Act("drop", Inventory.Bag(0));
        SelfTest.Check("book: Drop puts the item on the ground", drop == "dropped" && Ground.Count == ground + 1 && InventoryActions.BagCount() == bag - 1, drop);
        string first = BookCard.Act("destroy", Inventory.Bag(1));
        string second = BookCard.Act("destroy", Inventory.Bag(1));
        SelfTest.Check("book: Destroy needs a second click", first == "armed" && second == "destroyed" && InventoryActions.BagCount() == bag - 2, $"{first}, {second}");
    }

    private static void CharacterRows()
    {
        var rows = BookPages.CurrentHits.Count(h => h.Key.StartsWith("row.", StringComparison.Ordinal));
        SelfTest.Check("book: the character page lists stat rows", BookHost.Tab == BookTab.Character && rows >= 8, $"{rows} rows");
    }

    private static void GamePage()
    {
        BookHost.ShowGamePage();
        SelfTest.Check("book: a game tab hands the page back", !BookPatches.Block && BookHost.Tab == BookTab.None && BookHost.IsOpen);
    }

    private static void Cleanup()
    {
        SelfTest.Check("book: closed", !BookHost.IsOpen && !BookPatches.Block);
        BookCard.Hide();
        WorldState.SetRulesForTest(_savedRules ?? WorldState.Rules);
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        LootFeature.ClearAll();
        LocalPlayer.Transform.position = _home;
        Physics.SyncTransforms();
    }
}
