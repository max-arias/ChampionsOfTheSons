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
        SelfTest.Add(0.5f, "book: unpause", TestKit.Unpause);
        SelfTest.Add(1f, "book: setup", Setup);
        SelfTest.Add(1.5f, "book: kill for drops", Kill);
        SelfTest.Add(5f, "book: drops are gear", CheckDrops);
        for (int i = 0; i < 14; i++) SelfTest.Add(5.5f + i * 0.35f, "book: walk over drops", WalkToNextDrop);
        SelfTest.Add(10.6f, "book: picked up", CheckPickedUp);
        SelfTest.Add(11f, "book: open on Gear", () => BookHost.Open(BookTab.Gear));
        SelfTest.Add(14f, "book: gear tab", GearTab);
        SelfTest.Add(14.2f, "book: shot gear", () => TestKit.Screenshot("book-gear"));
        SelfTest.Add(14.5f, "book: bag tab", BagTab);
        SelfTest.Add(15.2f, "book: shot bag", () => TestKit.Screenshot("book-bag-1"));
        SelfTest.Add(15.3f, "book: filters", Filters);
        SelfTest.Add(15.5f, "book: cursor maps onto slots", CursorMapping);
        SelfTest.Add(16f, "book: select shows the card", Select);
        SelfTest.Add(16.8f, "book: card pinned", CardPinned);
        SelfTest.Add(17f, "book: shot card", () => TestKit.Screenshot("book-card"));
        SelfTest.Add(17.5f, "book: equip from the card", EquipFromCard);
        SelfTest.Add(18.2f, "book: shot gear equipped", () => TestKit.Screenshot("book-gear-equipped"));
        SelfTest.Add(18.5f, "book: right-click unequips", Unequip);
        SelfTest.Add(19f, "book: drag bag to doll", DragBagToDoll);
        SelfTest.Add(19.3f, "book: drag doll to bag", DragDollToBag);
        SelfTest.Add(19.6f, "book: drag bag to bag", DragBagToBag);
        SelfTest.Add(19.9f, "book: pad South selects, West equips and unequips", PadButtons);
        SelfTest.Add(20.2f, "book: pad shoulders page the bag", PadShoulders);
        SelfTest.Add(20.5f, "book: empty-slot labels fit", LabelsFit);
        SelfTest.Add(20.6f, "book: quickbar strip (COS-212)", Quickbar);
        SelfTest.Add(20.7f, "book: shot quickbar", () => TestKit.Screenshot("book-quickbar"));
        SelfTest.Add(20.75f, "book: drag off the page drops (COS-46)", DragOffPage);
        for (int f = 1; f <= 3; f++)
        {
            int filter = f;
            SelfTest.Add(20.8f + (f - 1) * 0.7f, $"book: filter {f} for the shot", () => { BookPages.Click(BookTab.Gear, "filter." + filter, false); BookPages.Refresh(BookTab.Gear); });
            SelfTest.Add(21.2f + (f - 1) * 0.7f, $"book: shot filter {f}", () => TestKit.Screenshot("book-gear-filter-" + filter));
        }
        SelfTest.Add(22.9f, "book: filter All again", () => { BookPages.Click(BookTab.Gear, "filter.0", false); BookPages.Refresh(BookTab.Gear); });
        SelfTest.Add(23f, "book: sort", Sort);
        SelfTest.Add(23.5f, "book: drop and destroy", DropAndDestroy);
        SelfTest.Add(24.5f, "book: character tab", () => BookHost.ShowModPage(BookTab.Character));
        SelfTest.Add(25.2f, "book: character rows", CharacterRows);
        SelfTest.Add(25.3f, "book: shot character 1", () => TestKit.Screenshot("book-character-1"));
        SelfTest.Add(25.6f, "book: character page 2", () => BookPages.Page(BookTab.Character, 1));
        SelfTest.Add(26.2f, "book: shot character 2", () => TestKit.Screenshot("book-character-2"));
        SelfTest.Add(26.4f, "book: character page 3", () => BookPages.Page(BookTab.Character, 1));
        SelfTest.Add(27f, "book: shot character 3", () => TestKit.Screenshot("book-character-3"));
        // Help tab (COS-97): a third sub-tab after Character; its Keys page lists the live bindings.
        SelfTest.Add(27.05f, "book: help tab", () => BookHost.ShowModPage(BookTab.Help));
        SelfTest.Add(27.12f, "book: help tab shows the guide", () =>
        {
            SelfTest.Check("book: the Help tab opens on its first page", BookHost.Tab == BookTab.Help && BookPages.HelpPage == 0 && BookGuide.Pages >= 7,
                $"tab {BookHost.Tab}, page {BookPages.HelpPage}, pages {BookGuide.Pages}");
            BookPages.Page(BookTab.Help, 1);
            var keys = string.Join(" ", BookGuide.KeyRows().Select(r => r.Key));
            string spell1 = Settings.KeyNames.Short(Settings.ModKeys.Value("spell_slot_1"));
            SelfTest.Check("book: the Keys page lists the current spell key", keys.Contains(spell1), spell1);
        });
        SelfTest.Add(27.2f, "book: shot help keys", () => TestKit.Screenshot("book-help-keys"));
        SelfTest.Add(27.3f, "book: game tab gives the page back", GamePage);
        SelfTest.Add(28f, "book: shot game page", () => TestKit.Screenshot("book-game-page"));
        SelfTest.Add(28.3f, "book: tab idle", TabIdle);
        SelfTest.Add(28.5f, "book: shot tab idle", () => TestKit.Screenshot("book-tab-idle"));
        SelfTest.Add(28.6f, "book: cursor on our tab", () => SetCursor(BookTabs.ScreenCenter(BookTab.Gear)));
        SelfTest.Add(29.4f, "book: our tab slides out on hover", TabHovered);
        SelfTest.Add(29.5f, "book: shot tab hover", () => TestKit.Screenshot("book-tab-hover"));
        SelfTest.Add(29.6f, "book: cursor on the game's tab above, which slides out", () =>
        {
            _neighbourIdle = BookTabs.NeighbourSlideOffset();
            SetCursor(BookTabs.NeighbourScreenCenter());
            BookTabs.SlideNeighbourForTest(true);
        });
        SelfTest.Add(30.4f, "book: the neighbour's hover leaves our tab", NeighbourHovered);
        SelfTest.Add(30.5f, "book: shot neighbour hover", () => TestKit.Screenshot("book-tab-neighbour-hover"));
        SelfTest.Add(30.7f, "book: the game's tab back", () => BookTabs.SlideNeighbourForTest(false));
        // COS-194: with a mod page open the sub-tabs are out; the hovered one grows, the others keep their size.
        SelfTest.Add(30.75f, "book: help page for the sub-tab hover", () => BookHost.ShowModPage(BookTab.Help));
        SelfTest.Add(31.4f, "book: cursor on the Spells sub-tab", () => SetCursor(BookTabs.SubScreenCenter(BookTab.Spells)));
        SelfTest.Add(31.7f, "book: cursor kept on the Spells sub-tab", () => SetCursor(BookTabs.SubScreenCenter(BookTab.Spells))); // COS-135 drift
        SelfTest.Add(32f, "book: hovered sub-tab grows", () =>
        {
            SelfTest.Check("book: hovering a sub-tab grows it (COS-194)", BookTabs.Hovered == BookTab.Spells && BookTabs.SubGrow(BookTab.Spells) > 0.9f
                && BookTabs.SubGrow(BookTab.Help) < 0.1f && BookTabs.SubSlidOut(BookTab.Help) && BookTabs.SubSlidOut(BookTab.Bag),
                $"hovered {BookTabs.Hovered}, grow spells {BookTabs.SubGrow(BookTab.Spells):0.00} help {BookTabs.SubGrow(BookTab.Help):0.00}");
            TestKit.Screenshot("book-subtab-hover");
        });
        SelfTest.Add(32.3f, "book: close", () => BookHost.Close());
        SelfTest.Add(33.8f, "book: closed and cleanup", Cleanup);
    }

    private static (Vector3 Face, Vector3 Root) _tabIdle;
    private static float _neighbourIdle;

    private static void SetCursor(Vector2 screen)
    {
        BookHost.CursorScreen = screen;
    }

    // COS-131: our tab slides out on its own hover only, with the game's slide animation, and the hover of the game's
    // tab above it no longer moves it (it used to sit inside that tab's animated part).
    private static void TabIdle()
    {
        SetCursor(new Vector2(5, 5));
        BookTabs.Tick(new Vector2(5, 5));
        _tabIdle = BookTabs.Pose(BookTab.Gear);
        SelfTest.Check("book: our tab rests while not hovered and the game's page shows", !BookTabs.SlidOut(BookTab.Gear) && BookHost.Tab == BookTab.None,
            $"slid {BookTabs.SlidOut(BookTab.Gear)}, tab {BookHost.Tab}, pose {_tabIdle}");
    }

    private static void TabHovered()
    {
        // COS-135: the game moves its book cursor between steps (it drifted off our tab once); aim it again before checking.
        var center = BookTabs.ScreenCenter(BookTab.Gear);
        SetCursor(center);
        BookTabs.Tick(center);
        var pose = BookTabs.Pose(BookTab.Gear);
        float moved = (pose.Face - _tabIdle.Face).magnitude;
        SelfTest.Check("book: hovering our tab slides it out", BookTabs.Hovered == BookTab.Gear && BookTabs.SlidOut(BookTab.Gear) && moved > 0.0005f && BookTabs.OutlineShown(BookTab.Gear),
            $"hovered {BookTabs.Hovered}, slid {BookTabs.SlidOut(BookTab.Gear)}, face moved {moved:F4}, outline {BookTabs.OutlineShown(BookTab.Gear)}");
    }

    private static void NeighbourHovered()
    {
        var pose = BookTabs.Pose(BookTab.Gear);
        float face = (pose.Face - _tabIdle.Face).magnitude, root = (pose.Root - _tabIdle.Root).magnitude;
        float neighbour = Mathf.Abs(BookTabs.NeighbourSlideOffset() - _neighbourIdle);
        SelfTest.Check("book: the game's tab above slid out for the check", neighbour > 0.0005f, $"neighbour slid {BookTabs.NeighbourSlidOut}, moved {neighbour:F4}");
        SelfTest.Check("book: the game's tab above sliding out leaves ours in place", BookTabs.Hovered == BookTab.None && !BookTabs.SlidOut(BookTab.Gear) && !BookTabs.OutlineShown(BookTab.Gear) && face < 0.0002f && root < 0.0002f,
            $"hovered {BookTabs.Hovered}, slid {BookTabs.SlidOut(BookTab.Gear)}, face off {face:F4}, root off {root:F4}");
    }

    private static void Setup()
    {
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
        WorldState.SetRulesForTest(_savedRules! with { DropChance = 1000, DropQuantity = 3, AllowElites = false });
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
        SelfTest.Check("book: the book opened on Gear", BookHost.IsOpen && BookHost.Tab == BookTab.Gear, $"open {BookHost.IsOpen}, tab {BookHost.Tab}");
        SelfTest.Check("book: our tab and its five sub-tabs were added (COS-168)", BookTabs.Tabs.Count == 6 && BookTabs.SubTabs.Count == 5, $"{BookTabs.Tabs.Count} tabs, {BookTabs.SubTabs.Count} sub-tabs");
        SelfTest.Check("book: the game's page actions are blocked", BookPatches.Block);
        // COS-195: our tabs stand on the page's top edge (the thumb covered the place under the game's column).
        var page = BookHost.PageScreenRect();
        var at = new[] { BookTabs.ScreenCenter(BookTab.Gear) }.Concat(BookTabs.SubTabs.Select(BookTabs.SubScreenCenter)).ToList();
        SelfTest.Check("book: our tab and sub-tabs are above the page's top edge (COS-195)", at.All(p => p.y > page.yMax && p.x > page.xMin && p.x < page.xMax),
            $"page {page}, tabs {string.Join(" ", at.Select(p => $"({p.x:0},{p.y:0})"))}");
    }

    private static void BagTab()
    {
        BookHost.ShowModPage(BookTab.Bag);
        BookPages.Refresh(BookTab.Bag);
        int shown = BookPages.Slots(BookTab.Bag).Count(s => s.Shown != null);
        SelfTest.Check("book: the bag page shows the picked-up items", shown == Math.Min(_picked, BookPages.BagPerPage), $"{shown} of {_picked}");
    }

    private static void Filters()
    {
        foreach (int f in new[] { 1, 2, 3 })
        {
            BookPages.Click(BookTab.Gear, "filter." + f, right: false);
            BookPages.Refresh(BookTab.Gear);
            var shown = BookPages.Slots(BookTab.Bag).Where(s => s.Shown != null).Select(s => ModContent.Db.Items[s.Shown!.BaseId].Type).ToList();
            bool ok = f switch
            {
                1 => shown.All(t => t is "weapon" or "shield" or "quiver" or "spell_scroll"),
                2 => shown.All(t => t is "helmet" or "chest_armor" or "pants" or "boot" or "shoulder_armor" or "glove" or "bracer"),
                _ => shown.All(t => t is "ring" or "amulet"),
            };
            SelfTest.Check($"book: filter {f} shows only its kind", ok, string.Join(",", shown));
        }
        BookPages.Click(BookTab.Gear, "filter.0", right: false);
        BookPages.Refresh(BookTab.Gear);
    }

    private static void CursorMapping()
    {
        var book = BookHost.Book;
        var slot = BookPages.Slots(BookTab.Bag)[7];
        if (book == null) { SelfTest.Check("book: held", false); return; }
        var center = new Vector2(slot.X + slot.Size / 2, slot.Y + slot.Size / 2);
        var screen = BookHost.ScreenOfPagePx(center);
        BookHost.CursorScreen = screen;
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
        BookHost.CursorScreen = new Vector2(5, 5);
    }

    private static void CardPinned()
    {
        SelfTest.Check("book: the card shows the selected item with actions", BookCard.ShownKey == Inventory.Bag(0) && BookCard.ActionsVisible,
            $"shown {BookCard.ShownKey}, actions {BookCard.ActionsVisible}");
        bool hasSprite = Icons.IconCache.GetSprite(Pc.Inventory.Get(Inventory.Bag(0))?.BaseId ?? "") != null;
        SelfTest.Check("book: the card shows the item icon", !hasSprite || BookCard.IconShown, $"sprite {hasSprite}, shown {BookCard.IconShown}");
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

    // ------------------------------------------------------------------ drag and drop, pad (driven through BookFeature.Handle)

    private static Vector2 ScreenOf(PageSlot s) => BookHost.ScreenOfPagePx(new Vector2(s.X + s.W / 2, s.Y + s.H / 2));

    private static PageSlot? GearSlot(string key) => BookPages.Slots(BookTab.Gear).FirstOrDefault(s => s.Key == key);

    /// <summary>Mouse press on one screen point, two moves, release on another: one frame each.</summary>
    private static void Drag(Vector2 from, Vector2 to)
    {
        BookFeature.Handle(BookInput.MouseAt(from, down: true, held: true));
        BookFeature.Handle(BookInput.MouseAt(Vector2.Lerp(from, to, 0.5f), held: true));
        BookFeature.Handle(BookInput.MouseAt(to, held: true));
        BookFeature.Handle(BookInput.MouseAt(to, up: true));
        BookPages.Refresh(BookTab.Gear);
    }

    /// <summary>COS-212: weapons stay in the bag and go on the quickbar strip by drag, swap, right-click and pickup; a weapon's
    /// stats count only while drawn.</summary>
    private static void Quickbar()
    {
        foreach (int k in Pc.WeaponBar.Bindings.Keys.ToList()) Pc.WeaponBar.Unbind(k);
        Pc.Select(null);
        var def = ModContent.Db.Items["item.rusty_polearm"];
        var weapon = new LootRoller(ModContent.Db).RollItem(def, 10, new Rng(7));
        Pc.Inventory.AddToBag(weapon);
        BookHost.ShowModPage(BookTab.Gear);
        BookPages.Click(BookTab.Gear, "filter.1", false); // weapons first page
        BookPages.Selected = null;
        BookPages.Refresh(BookTab.Gear);
        var cells = BookPages.QuickbarSlots.ToList();
        var src = BookPages.Slots(BookTab.Bag).FirstOrDefault(s => ReferenceEquals(s.Shown, weapon));
        SelfTest.Check("book: quickbar: no weapon slot on the doll", GearSlot(WeaponTypes.LegacyWeaponSlot) == null);
        SelfTest.Check("book: quickbar: a cell per weapon key", cells.Count == Math.Min(6, ActionBar.ActionBarConfig.WeaponHotkeys.Count), $"{cells.Count} cells");
        if (src == null || cells.Count < 2) { SelfTest.Check("book: quickbar: the weapon shows in the bag", false); return; }
        string bag = src.Key;
        int k0 = BookPages.CellIndex(cells[0].Key), k1 = BookPages.CellIndex(cells[1].Key);

        Drag(ScreenOf(src), ScreenOf(cells[0]));
        SelfTest.Check("book: quickbar: dragging a bag weapon onto a cell binds it", Pc.WeaponBar.SlotOf(k0) == bag, BookFeature.LastAction);
        var badged = BookPages.Slots(BookTab.Bag).First(s => s.Key == bag);
        SelfTest.Check("book: quickbar: the bound bag weapon shows its key", badged.Badge.enabled && badged.Badge.text == Bridge.HotkeyBridge.KeyLabel(k0), badged.Badge.text);

        Drag(ScreenOf(cells[0]), ScreenOf(cells[1]));
        SelfTest.Check("book: quickbar: cell onto cell swaps keys", Pc.WeaponBar.SlotOf(k1) == bag && Pc.WeaponBar.SlotOf(k0) == null, BookFeature.LastAction);

        string r = BookPages.Click(BookTab.Gear, cells[1].Key, right: true);
        SelfTest.Check("book: quickbar: right-click on a cell unbinds", Pc.WeaponBar.KeyOf(bag) < 0 && Pc.Inventory.Get(bag) != null, r);

        BookPages.Refresh(BookTab.Gear);
        BookFeature.Handle(BookInput.Pad(ScreenOf(BookPages.Slots(BookTab.Bag).First(s => s.Key == bag)), west: true));
        SelfTest.Check("book: quickbar: pad West on a bag weapon binds it to the first free key", Pc.WeaponBar.KeyOf(bag) == k0, BookFeature.LastAction);

        var text = ItemDescriber.Describe(weapon, Pc, bag).Body;
        SelfTest.Check("book: quickbar: the card heads the weapon's stats 'While drawn'", weapon.Stats.Count == 0 || text.Contains(UiText.T("ui.quickbar.while_drawn", "While drawn")), text);

        Pc.Select(bag);
        Pc.SetDrawn(false);
        bool holstered = !Pc.Stats.Sources.Contains("equip.drawn");
        Pc.SetDrawn(true);
        bool drawn = Pc.Stats.Sources.Contains("equip.drawn");
        Pc.SetDrawn(false);
        SelfTest.Check("book: quickbar: a weapon's stats count only while drawn", holstered && drawn, $"holstered {holstered}, drawn {drawn}");

        // Picking a weapon up puts it on the first free key.
        var second = new LootRoller(ModContent.Db).RollItem(def, 10, new Rng(8));
        var drop = LootFeature.SpawnAt(second, LocalPlayer.Transform.position + LocalPlayer.Transform.forward * 1.5f);
        var picked = LootFeature.Pickup(drop.Id);
        var where = Pc.Inventory.Slots.FirstOrDefault(kv => ReferenceEquals(kv.Value, second)).Key;
        SelfTest.Check("book: quickbar: a picked-up weapon goes on the first free key", picked == PickupResult.Ok && where != null && Pc.WeaponBar.KeyOf(where) == k1,
            $"{picked}, in {where}, key {(where == null ? -1 : Pc.WeaponBar.KeyOf(where))}");

        BookPages.Click(BookTab.Gear, "filter.0", false);
        BookPages.Refresh(BookTab.Gear);
    }

    /// <summary>COS-46: an item dragged off the page lands in the world as a ground drop.</summary>
    private static void DragOffPage()
    {
        ShowBagFirstPage();
        var src = BookPages.Slots(BookTab.Bag).FirstOrDefault(s => s.Shown != null);
        if (src == null) { SelfTest.Check("book: drag off the page: a bag item", false); return; }
        var item = src.Shown!;
        string key = src.Key;
        var page = BookHost.PageScreenRect();
        var outside = new Vector2(Mathf.Min(Screen.width - 5, page.xMax + 0.6f * (Screen.width - page.xMax)), page.yMin + 10);
        Drag(ScreenOf(src), outside);
        bool gone = Pc.Inventory.Get(key) == null || !ReferenceEquals(Pc.Inventory.Get(key), item);
        bool lies = LootFeature.Ground.All.Any(d => ReferenceEquals(d.Item, item) || Equals(d.Item, item));
        SelfTest.Check("book: dragging an item off the page drops it into the world", gone && lies, BookFeature.LastAction);
    }

    private static void ShowBagFirstPage()
    {
        BookHost.ShowModPage(BookTab.Gear);
        BookPages.Click(BookTab.Gear, "filter.0", false);
        BookPages.Selected = null;
        BookPages.Refresh(BookTab.Gear);
    }

    private static string? _dragEquip;

    private static void DragBagToDoll()
    {
        ShowBagFirstPage();
        var src = BookPages.Slots(BookTab.Bag).FirstOrDefault(s => s.Shown != null && ModContent.Db.Items.TryGetValue(s.Shown.BaseId, out var d)
            && LootRoller.SlotsFor(d.Type).Any(k => Pc.Inventory.Get(k) == null && GearSlot(k) != null));
        if (src == null) { SelfTest.Check("book: drag bag to doll: a bag item with a free slot", false); return; }
        var item = src.Shown!;
        var target = LootRoller.SlotsFor(ModContent.Db.Items[item.BaseId].Type).First(k => Pc.Inventory.Get(k) == null && GearSlot(k) != null);
        Drag(ScreenOf(src), ScreenOf(GearSlot(target)!));
        _dragEquip = target;
        SelfTest.Check("book: dragging a bag item onto its doll slot equips it", Equals(Pc.Inventory.Get(target), item), $"{src.Key} -> {target}: {BookFeature.LastAction}");
    }

    private static void DragDollToBag()
    {
        if (_dragEquip == null) return;
        var item = Pc.Inventory.Get(_dragEquip);
        var empty = BookPages.Slots(BookTab.Bag).LastOrDefault(s => s.Key.Length > 0 && s.Shown == null);
        if (item == null || empty == null) { SelfTest.Check("book: drag doll to bag: an equipped item and an empty bag slot", false); return; }
        string to = empty.Key;
        Drag(ScreenOf(GearSlot(_dragEquip)!), ScreenOf(empty));
        SelfTest.Check("book: dragging an equipped item onto an empty bag slot unequips it there",
            Pc.Inventory.Get(_dragEquip) == null && Equals(Pc.Inventory.Get(to), item), $"{_dragEquip} -> {to}: {BookFeature.LastAction}");
    }

    private static void DragBagToBag()
    {
        var slots = BookPages.Slots(BookTab.Bag);
        var full = slots.Where(s => s.Shown != null).ToList();
        var empty = slots.FirstOrDefault(s => s.Key.Length > 0 && s.Shown == null);
        if (empty == null && full.Count > 2)
        {
            // The kills can fill the whole page: free its last slot for the move.
            Pc.Inventory.Remove(full[^1].Key);
            BookPages.Refresh(BookTab.Bag);
            slots = BookPages.Slots(BookTab.Bag);
            full = slots.Where(s => s.Shown != null).ToList();
            empty = slots.FirstOrDefault(s => s.Key.Length > 0 && s.Shown == null);
        }
        if (full.Count < 2 || empty == null) { SelfTest.Check("book: drag bag to bag: two items and an empty slot", false); return; }
        var a = full[0];
        var item = a.Shown!;
        string from = a.Key, to = empty.Key;
        Drag(ScreenOf(a), ScreenOf(empty));
        SelfTest.Check("book: dragging a bag item onto an empty bag slot moves it", Pc.Inventory.Get(from) == null && Equals(Pc.Inventory.Get(to), item), $"{from} -> {to}");
        // Swap two occupied slots.
        var x = slots.First(s => s.Key == to);
        var y = slots.First(s => s.Shown != null && s.Key != to);
        var itemX = x.Shown!;
        var itemY = y.Shown!;
        string kx = x.Key, ky = y.Key;
        Drag(ScreenOf(x), ScreenOf(y));
        SelfTest.Check("book: dragging onto another bag item swaps them", Equals(Pc.Inventory.Get(ky), itemX) && Equals(Pc.Inventory.Get(kx), itemY), $"{kx} <-> {ky}");
        // A short press without moving far is still a click (select).
        var at = ScreenOf(x);
        BookFeature.Handle(BookInput.MouseAt(at, down: true, held: true));
        BookFeature.Handle(BookInput.MouseAt(at + new Vector2(BookFeature.DragThreshold / 3, 0), up: true));
        SelfTest.Check("book: a click without moving still selects", BookPages.Selected == kx && BookFeature.Dragging == null, $"selected {BookPages.Selected}");
        BookPages.Selected = null;
    }

    private static void PadButtons()
    {
        ShowBagFirstPage();
        var src = BookPages.Slots(BookTab.Bag).FirstOrDefault(s => s.Shown != null && ModContent.Db.Items.TryGetValue(s.Shown.BaseId, out var d) && LootRoller.SlotsFor(d.Type).Count > 0);
        if (src == null) { SelfTest.Check("book: pad: an equippable bag item", false); return; }
        var item = src.Shown!;
        string key = src.Key;
        var at = ScreenOf(src);
        BookFeature.Handle(BookInput.Pad(at, southDown: true, southHeld: true));
        BookFeature.Handle(BookInput.Pad(at, southUp: true));
        SelfTest.Check("book: pad South on a bag item selects it", BookPages.Selected == key, $"selected {BookPages.Selected}");
        BookFeature.Handle(BookInput.Pad(at, west: true));
        var equipped = Pc.Inventory.Equipped.FirstOrDefault(kv => Equals(kv.Value, item)).Key;
        SelfTest.Check("book: pad West on a bag item equips it", equipped != null && Pc.Inventory.Get(key) == null, $"slot {equipped}");
        if (equipped == null) return;
        BookPages.Refresh(BookTab.Gear);
        var gear = GearSlot(equipped);
        if (gear == null) return;
        BookFeature.Handle(BookInput.Pad(ScreenOf(gear), west: true));
        bool back = Pc.Inventory.Get(equipped) == null && Pc.Inventory.Slots.Any(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && Equals(kv.Value, item));
        SelfTest.Check("book: pad West on an equipped item unequips it", back, BookFeature.LastAction);
    }

    private static void PadShoulders()
    {
        ShowBagFirstPage();
        int pages = BookPages.BagPages;
        var off = new Vector2(5, 5);
        BookFeature.Handle(BookInput.Pad(off, rb: true));
        int afterRb = BookPages.BagPage;
        BookFeature.Handle(BookInput.Pad(off, lb: true));
        int afterLb = BookPages.BagPage;
        SelfTest.Check("book: pad RB pages the bag forward and LB back", pages > 1 && afterRb == 1 && afterLb == 0, $"pages {pages}, RB {afterRb}, LB {afterLb}");
        BookHost.ShowModPage(BookTab.Character);
        int c0 = BookPages.CharPage;
        BookFeature.Handle(BookInput.Pad(off, rb: true));
        SelfTest.Check("book: pad RB pages the character sheet", BookPages.CharPage == (c0 + 1) % BookPages.CharPages, $"{c0} -> {BookPages.CharPage}");
        BookFeature.Handle(BookInput.Pad(off, lb: true));
        BookHost.ShowModPage(BookTab.Gear);
    }

    /// <summary>Empty doll slots show their slot name; with autosize it must fit inside the slot (current language).</summary>
    private static void LabelsFit()
    {
        ShowBagFirstPage();
        var bad = new List<string>();
        foreach (var s in BookPages.Slots(BookTab.Gear))
        {
            if (s.Shown != null || string.IsNullOrEmpty(s.Fallback.text)) continue;
            s.Fallback.ForceMeshUpdate();
            SelfTest.Log($"book: label {s.Key} '{s.Fallback.text}' font {s.Fallback.fontSize:0.#} in {s.W - 8}x{s.H - 8}, overflow {s.Fallback.isTextOverflowing}");
            if (s.Fallback.isTextOverflowing) bad.Add($"{s.Key} ({s.Fallback.text})");
        }
        SelfTest.Check("book: empty-slot labels fit their slots", bad.Count == 0, string.Join(", ", bad));
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
        // COS-174: the first character page holds the attributes; stat rows start on the next one.
        if (BookPages.CharPage == 0) { BookPages.Page(BookTab.Character, 1); BookPages.Refresh(BookTab.Character); }
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
