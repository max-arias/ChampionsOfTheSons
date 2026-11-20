using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>One item slot drawn on the page.</summary>
internal sealed class PageSlot
{
    public string Key = "";
    public float X, Y, Size, W, H;
    public Image Bg = null!, Icon = null!, Tape = null!;
    public Image[] Frame = null!, SelectFrame = null!;
    public TextMeshProUGUI Level = null!, Amount = null!, Fallback = null!;
    public ItemInstance? Shown;
    public bool Rendered, WasSelected;
}

/// <summary>
/// The three mod pages on the guide book (COS-59): Gear (paper doll + set bonuses), Bag (paged 5x7 grid + sort) and
/// Character (level, XP, stat sheet over two pages). Built once on the page canvas; refreshed when the inventory or
/// stats change. Clicks and hover come in as page pixels from <see cref="BookFeature"/>.
/// </summary>
internal static class BookPages
{
    public const int BagCols = 5, BagRows = 7, BagPerPage = BagCols * BagRows;
    public static int BagPages => (LootRoller.BagSlots + BagPerPage - 1) / BagPerPage;
    private const float Header = 80;
    /// <summary>Page margins: the spiral covers the left edge and the thumb the bottom left (COS-59 screenshots).</summary>
    private const float Left = 46, Right = 16, Bottom = 92;
    private const float PagerY = PageKit.H - Bottom - 50, PagerH = 44;
    private const float NextX = PageKit.W - Right - 56, PrevX = NextX - 56 - 90, SortX = PrevX - 124;
    private const float XpW = PageKit.W - Left - Right;

    /// <summary>The mod page the book tab opens (the last one shown).</summary>
    public static BookTab LastTab { get; private set; } = BookTab.Gear;

    private static readonly (BookTab Tab, string Key, string Fallback)[] SubTabs =
    {
        (BookTab.Gear, "ui.book.tab.gear", "Gear"), (BookTab.Bag, "ui.book.tab.bag", "Bag"), (BookTab.Character, "ui.book.tab.character", "Character"),
    };

    /// <summary>Equip slots in a body shape, page pixels (x, y from the top-left, width, height).</summary>
    private static readonly (string Key, float X, float Y, float W, float H)[] BodyLayout =
    {
        ("equip.helmet", 300, 92, 96, 96),
        ("equip.chest", 284, 204, 128, 150),
        ("equip.pants", 284, 370, 128, 150),
        ("equip.boots", 300, 536, 96, 96),
        ("equip.shoulders", 172, 204, 96, 96),
        ("equip.gloves", 172, 316, 96, 96),
        ("equip.ring1", 188, 428, 64, 64),
        ("equip.amulet", 428, 204, 96, 96),
        ("equip.bracer", 428, 316, 96, 96),
        ("equip.ring2", 444, 428, 64, 64),
        ("equip.weapon", 56, 248, 104, 210),
        ("equip.offhand", 536, 248, 104, 210),
    };

    private static TextMeshProUGUI _gearStats = null!;

    private static RectTransform _gear = null!, _bag = null!, _char = null!;
    private static readonly List<PageSlot> GearSlots = new(), BagSlots = new();
    private static readonly List<PageHit> Hits = new();
    private static TextMeshProUGUI _sets = null!, _gearCount = null!, _bagCount = null!, _bagPager = null!, _charPager = null!;
    private static TextMeshProUGUI _level = null!, _xpText = null!, _perks = null!;
    private static RectTransform _xpFill = null!;
    private static readonly List<(TextMeshProUGUI Name, TextMeshProUGUI Value, Image Shade)> Rows = new();
    private static List<SheetRow> _sheet = new();
    private static bool _dirty = true;

    public static int BagPage { get; private set; }
    public static int CharPage { get; private set; }

    /// <summary>The selected slot key (equip.* or bag.N), shown with a red frame.</summary>
    public static string? Selected { get; set; }

    public static IReadOnlyList<PageSlot> Slots(BookTab tab) => tab == BookTab.Gear ? GearSlots : tab == BookTab.Bag ? BagSlots : Array.Empty<PageSlot>();

    public static IReadOnlyList<PageHit> CurrentHits => Hits;

    private static PlayerCharacter Pc => PlayerState.Character;

    public static void MarkDirty() => _dirty = true;

    // ------------------------------------------------------------------ build

    public static void Build(RectTransform page)
    {
        var bg = PageKit.Fill(page, "bg").gameObject.AddComponent<Image>();
        bg.color = PageKit.Paper;
        bg.raycastTarget = false;
        _gear = PageKit.Fill(page, "gear");
        _bag = PageKit.Fill(page, "bag");
        _char = PageKit.Fill(page, "character");
        BuildGear();
        BuildBag();
        BuildCharacter();
        Pc.Inventory.Changed += MarkDirty;
        Pc.Stats.Changed += MarkDirty;
        Show(BookTab.None);
    }

    /// <summary>The title bar holds the three sub-tabs; the open one is paper colored, like an open tab.</summary>
    private static TextMeshProUGUI TitleBar(RectTransform root, BookTab own)
    {
        PageKit.Box(root, "titlebar", 0, 0, PageKit.W, Header - 14, PageKit.TitleBar);
        float x = Left;
        foreach (var (tab, key, fallback) in SubTabs)
        {
            bool open = tab == own;
            PageKit.Box(root, "subtab-" + tab, x, 12, SubTabW, Header - 26, open ? PageKit.Paper : PageKit.TitleBar);
            PageKit.Text(root, "subtab-text-" + tab, x, 12, SubTabW, Header - 26, UiText.T(key, fallback).ToUpperInvariant(), 22,
                open ? PageKit.Ink : PageKit.TitleText, TextAlignmentOptions.Center);
            x += SubTabW + 6;
        }
        return PageKit.Text(root, "title-right", PageKit.W - Right - 110, 12, 104, Header - 26, "", 20, PageKit.TitleText, TextAlignmentOptions.MidlineRight);
    }

    private const float SubTabW = 150;

    private static void AddSubTabHits()
    {
        float x = Left;
        foreach (var (tab, _, _) in SubTabs)
        {
            Hits.Add(new PageHit("tab." + tab, x, 12, SubTabW, Header - 26));
            x += SubTabW + 6;
        }
    }

    private static PageSlot Slot(RectTransform root, string key, float x, float y, float size, string? emptyLabel) =>
        Slot(root, key, x, y, size, size, emptyLabel);

    /// <summary>A slot of any shape (weapons and armor pieces are taller); the icon stays square in the middle.</summary>
    private static PageSlot Slot(RectTransform root, string key, float x, float y, float w, float h, string? emptyLabel)
    {
        float size = Mathf.Min(w, h), icon = size * 0.76f;
        var s = new PageSlot { Key = key, X = x, Y = y, Size = size, W = w, H = h };
        s.Bg = PageKit.Box(root, key, x, y, w, h, PageKit.PaperShade);
        s.Frame = PageKit.Frame(root, x, y, w, h, PageKit.InkFaint, 2);
        s.Tape = PageKit.Box(root, key + "-tape", x - 6, y - 4, size * 0.42f, 14, Color.clear);
        s.Tape.rectTransform.localRotation = Quaternion.Euler(0, 0, 18);
        s.Icon = PageKit.Box(root, key + "-icon", x + (w - icon) / 2, y + (h - icon) / 2, icon, icon, Color.white);
        s.Icon.preserveAspect = true;
        s.Icon.enabled = false;
        s.Fallback = PageKit.Text(root, key + "-label", x + 4, y + 4, w - 8, h - 8, emptyLabel ?? "", Mathf.Max(15, size * 0.17f), PageKit.InkFaint, TextAlignmentOptions.Center, wrap: true);
        s.Level = PageKit.Text(root, key + "-lvl", x + 4, y + 2, w - 8, 22, "", 18, PageKit.Ink, TextAlignmentOptions.TopRight);
        s.Amount = PageKit.Text(root, key + "-amt", x + 4, y + h - 24, w - 8, 22, "", 18, PageKit.Ink, TextAlignmentOptions.BottomRight);
        s.SelectFrame = PageKit.Frame(root, x - 5, y - 5, w + 10, h + 10, PageKit.Select, 4);
        foreach (var f in s.SelectFrame) f.enabled = false;
        return s;
    }

    private static void BuildGear()
    {
        _gearCount = TitleBar(_gear, BookTab.Gear);
        // Slots in the shape of a body (COS-59, Max's reference): head, chest, legs and feet down the middle,
        // shoulders/gloves/ring on one side, neck/bracer/ring on the other, weapon and off-hand as tall slots outside.
        foreach (var (key, x, y, w, h) in BodyLayout)
            GearSlots.Add(Slot(_gear, key, x, y, w, h, UiText.SlotName(key)));
        // A small stat box like a character card: armor, health and melee damage.
        float boxY = 664;
        PageKit.Box(_gear, "stats-box", Left, boxY, PageKit.W - Left - Right, 70, PageKit.TitleBar);
        _gearStats = PageKit.Text(_gear, "stats", Left + 16, boxY, PageKit.W - Left - Right - 32, 70, "", 24, PageKit.TitleText, TextAlignmentOptions.Center);
        float setsY = boxY + 82;
        PageKit.Text(_gear, "sets-title", Left, setsY, 560, 30, UiText.T("ui.book.sets", "Set bonuses"), 24, PageKit.Ink);
        _sets = PageKit.Text(_gear, "sets", Left, setsY + 32, PageKit.W - Left - Right, PageKit.H - Bottom - setsY - 32, "", 20, PageKit.Ink, TextAlignmentOptions.TopLeft, wrap: true);
    }

    private static void BuildBag()
    {
        _bagCount = TitleBar(_bag, BookTab.Bag);
        const float cell = 104, gap = 10;
        float x0 = Left + (PageKit.W - Left - Right - (BagCols * cell + (BagCols - 1) * gap)) / 2, y0 = Header + 6;
        for (int i = 0; i < BagPerPage; i++)
            BagSlots.Add(Slot(_bag, "page-bag-" + i, x0 + (i % BagCols) * (cell + gap), y0 + (i / BagCols) * (cell + gap), cell, null));
        _bagPager = Pager(_bag);
        // Sort button in the pager row.
        PageKit.Box(_bag, "sort-bg", SortX, PagerY, 110, PagerH, PageKit.TitleBar);
        PageKit.Text(_bag, "sort", SortX, PagerY, 110, PagerH, UiText.T("ui.book.sort", "Sort"), 24, PageKit.TitleText, TextAlignmentOptions.Center);
    }

    private static TextMeshProUGUI Pager(RectTransform root)
    {
        PageKit.Box(root, "prev-bg", PrevX, PagerY, 56, PagerH, PageKit.TitleBar);
        PageKit.Text(root, "prev", PrevX, PagerY, 56, PagerH, "<", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        PageKit.Box(root, "next-bg", NextX, PagerY, 56, PagerH, PageKit.TitleBar);
        PageKit.Text(root, "next", NextX, PagerY, 56, PagerH, ">", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        return PageKit.Text(root, "pager", PrevX + 56, PagerY, 90, PagerH, "", 26, PageKit.Ink, TextAlignmentOptions.Center);
    }

    private static void BuildCharacter()
    {
        TitleBar(_char, BookTab.Character);
        _level = PageKit.Text(_char, "level", Left, Header + 8, 320, 48, "", 40, PageKit.Ink);
        _perks = PageKit.Text(_char, "perks", Left + 300, Header + 14, XpW - 300, 40, "", 22, PageKit.Good, TextAlignmentOptions.MidlineRight);
        PageKit.Box(_char, "xp-back", Left, Header + 62, XpW, 16, PageKit.PaperShade);
        _xpFill = PageKit.Box(_char, "xp-fill", Left, Header + 62, XpW, 16, PageKit.RarityColor(3)).rectTransform;
        _xpText = PageKit.Text(_char, "xp-text", Left, Header + 80, XpW, 28, "", 20, PageKit.InkFaint, TextAlignmentOptions.Center);
        _charPager = Pager(_char);
    }

    // ------------------------------------------------------------------ show / refresh

    public static void Show(BookTab tab)
    {
        _gear.gameObject.SetActive(tab == BookTab.Gear);
        _bag.gameObject.SetActive(tab == BookTab.Bag);
        _char.gameObject.SetActive(tab == BookTab.Character);
        _dirty = true;
        Refresh(tab);
    }

    public static void Tick(BookTab tab)
    {
        if (_dirty) Refresh(tab);
    }

    public static void Refresh(BookTab tab)
    {
        _dirty = false;
        Hits.Clear();
        AddSubTabHits();
        if (tab != BookTab.None) LastTab = tab;
        switch (tab)
        {
            case BookTab.Gear: RefreshGear(); break;
            case BookTab.Bag: RefreshBag(); break;
            case BookTab.Character: RefreshCharacter(); break;
        }
    }

    private static void RefreshGear()
    {
        foreach (var s in GearSlots)
        {
            RenderSlot(s, Pc.Inventory.Get(s.Key));
            Hits.Add(new PageHit(s.Key, s.X, s.Y, s.W, s.H));
        }
        var st = Pc.Stats;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _gearStats.text = $"{UiText.T("ui.sheet.armor", "Armor")} <b>{st.Get(StatIds.Armor).ToString("N0", inv)}</b>    "
            + $"{UiText.T("ui.sheet.max_health", "Max health")} <b>{Derived.MaxHealth(st).ToString("N0", inv)}</b>    "
            + $"{UiText.T("ui.sheet.melee_damage", "Melee damage")} <b>x{Derived.MeleeMultiplier(st).ToString("0.00", inv)}</b>";
        _gearCount.text = $"{UiText.T("ui.book.tab.bag", "Bag")} {InventoryActions.BagCount()}/{LootRoller.BagSlots}";
        var sets = InventoryActions.SetBonusText(PageKit.HexOf(PageKit.TitleBar), PageKit.HexOf(PageKit.Good), PageKit.HexOf(PageKit.InkFaint));
        _sets.text = sets.Length == 0 ? $"<color={PageKit.HexOf(PageKit.InkFaint)}>{UiText.T("ui.sets.none", "No set pieces equipped.")}</color>" : sets;
    }

    private static void RefreshBag()
    {
        BagPage = Math.Clamp(BagPage, 0, BagPages - 1);
        for (int i = 0; i < BagSlots.Count; i++)
        {
            var s = BagSlots[i];
            int index = BagPage * BagPerPage + i;
            bool exists = index < LootRoller.BagSlots;
            s.Key = exists ? Inventory.Bag(index) : "";
            RenderSlot(s, exists ? Pc.Inventory.Get(s.Key) : null, force: true);
            if (exists) Hits.Add(new PageHit(s.Key, s.X, s.Y, s.W, s.H));
        }
        _bagCount.text = $"{InventoryActions.BagCount()}/{LootRoller.BagSlots}";
        _bagPager.text = $"{BagPage + 1}/{BagPages}";
        AddPagerHits();
        Hits.Add(new PageHit("sort", SortX, PagerY, 110, PagerH));
    }

    private static void AddPagerHits()
    {
        Hits.Add(new PageHit("prev", PrevX, PagerY, 56, PagerH));
        Hits.Add(new PageHit("next", NextX, PagerY, 56, PagerH));
    }

    private static void RefreshCharacter()
    {
        var sum = SheetData.Summary(Pc);
        _level.text = $"{UiText.T("ui.sheet.level", "Level")} {sum.Level}";
        _perks.text = sum.PerkPoints > 0 ? $"{sum.PerkPoints} {UiText.T("ui.book.perk_points", "perk points")}" : "";
        _xpFill.sizeDelta = new Vector2(XpW * (float)sum.Fraction, 16);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _xpText.text = $"{Math.Floor(sum.Xp).ToString("N0", inv)} / {sum.Goal.ToString("N0", inv)} XP";

        _sheet = SheetData.Build(Pc);
        // Page 1: attributes, vitals, offense. Page 2: defense, utility.
        int split = _sheet.FindIndex(r => r.Header && r.Id == "defense");
        if (split < 0) split = _sheet.Count / 2;
        var rows = CharPage == 0 ? _sheet.Take(split).ToList() : _sheet.Skip(split).ToList();
        const float rowH = 36, top = Header + 116;
        while (Rows.Count < rows.Count)
        {
            int i = Rows.Count;
            // Alternate rows are shaded so label and value read as one line on the tilted page.
            var shade = PageKit.Box(_char, "row-shade-" + i, Left - 6, 0, XpW + 12, rowH, i % 2 == 0 ? new Color(0, 0, 0, 0.06f) : Color.clear);
            shade.transform.SetAsFirstSibling();
            Rows.Add((PageKit.Text(_char, "row-name-" + i, Left, 0, 380, rowH, "", 24, PageKit.Ink),
                PageKit.Text(_char, "row-value-" + i, Left + 380, 0, XpW - 380, rowH, "", 24, PageKit.Ink, TextAlignmentOptions.MidlineRight), shade));
        }
        for (int i = 0; i < Rows.Count; i++)
        {
            var (name, value, shadeImg) = Rows[i];
            bool on = i < rows.Count;
            name.gameObject.SetActive(on);
            value.gameObject.SetActive(on);
            shadeImg.gameObject.SetActive(on);
            if (!on) continue;
            var row = rows[i];
            float y = top + i * rowH;
            name.rectTransform.anchoredPosition = new Vector2(Left, -y);
            shadeImg.rectTransform.anchoredPosition = new Vector2(Left - 6, -y);
            value.rectTransform.anchoredPosition = new Vector2(Left + 380, -y);
            name.text = row.Header ? $"<b>{row.Label.ToUpperInvariant()}</b>" : row.Label;
            name.color = row.Header ? PageKit.TitleBar : PageKit.Ink;
            value.text = row.Value;
            if (!row.Header) Hits.Add(new PageHit("row." + row.Id, Left, y, XpW, rowH));
        }
        _charPager.text = $"{CharPage + 1}/2";
        AddPagerHits();
    }

    private static void RenderSlot(PageSlot s, ItemInstance? item, bool force = false)
    {
        bool selected = s.Key.Length > 0 && s.Key == Selected;
        if (selected != s.WasSelected)
        {
            foreach (var f in s.SelectFrame) f.enabled = selected;
            s.WasSelected = selected;
        }
        if (!force && s.Rendered && Equals(item, s.Shown)) return;
        s.Rendered = true;
        s.Shown = item;
        if (item == null)
        {
            s.Icon.enabled = false;
            s.Tape.color = Color.clear;
            s.Level.text = s.Amount.text = "";
            s.Fallback.text = s.Key.StartsWith("equip.", StringComparison.Ordinal) ? UiText.SlotName(s.Key) : "";
            s.Fallback.color = PageKit.InkFaint;
            return;
        }
        var sprite = Icons.IconCache.GetSprite(item.BaseId);
        s.Icon.sprite = sprite;
        s.Icon.enabled = sprite != null;
        s.Fallback.text = sprite == null ? UiText.ItemName(item) : "";
        s.Fallback.color = PageKit.Ink;
        s.Tape.color = PageKit.RarityColor((int)item.Rarity);
        bool leveled = ModContent.Db.Items.TryGetValue(item.BaseId, out var def) && !LootRoller.IsMaterialOrOther(def);
        s.Level.text = leveled ? item.Level.ToString() : "";
        s.Amount.text = item.Amount > 1 ? "x" + item.Amount : "";
    }

    // ------------------------------------------------------------------ input

    public static string? HitAt(Vector2 px)
    {
        foreach (var h in Hits)
            if (h.Contains(px)) return h.Key;
        return null;
    }

    public static void Page(BookTab tab, int delta)
    {
        if (tab == BookTab.Bag) BagPage = (BagPage + delta + BagPages) % BagPages;
        else if (tab == BookTab.Character) CharPage = (CharPage + delta + 2) % 2;
        _dirty = true;
    }

    /// <summary>The bag page that shows a bag key, so a test or the card can jump to it.</summary>
    public static void ShowBagSlot(string bagKey)
    {
        if (!int.TryParse(bagKey.AsSpan(4), out int i)) return;
        BagPage = i / BagPerPage;
        _dirty = true;
    }

    /// <summary>
    /// A click on the page. Left: select a slot; with a bag item selected, a click on an equip slot equips it there.
    /// Right: equip or unequip right away. Returns what happened, for the log and tests.
    /// </summary>
    public static string Click(BookTab tab, string key, bool right)
    {
        switch (key)
        {
            case "tab.Gear": BookHost.ShowModPage(BookTab.Gear); return "tab";
            case "tab.Bag": BookHost.ShowModPage(BookTab.Bag); return "tab";
            case "tab.Character": BookHost.ShowModPage(BookTab.Character); return "tab";
            case "prev": Page(tab, -1); return "page";
            case "next": Page(tab, +1); return "page";
            case "sort": InventoryActions.SortBag(); Selected = null; return "sort";
        }
        if (key.StartsWith("row.", StringComparison.Ordinal)) return "row";
        var item = Pc.Inventory.Get(key);
        _dirty = true;
        if (right)
        {
            Selected = null;
            return item != null && InventoryActions.ToggleEquip(key) ? "toggle" : "none";
        }
        if (Selected != null && Selected.StartsWith("bag.", StringComparison.Ordinal) && key.StartsWith("equip.", StringComparison.Ordinal))
        {
            bool ok = InventoryActions.EquipInto(Selected, key);
            Selected = null;
            return ok ? "equip-into" : "no-fit";
        }
        Selected = item == null || Selected == key ? null : key;
        return Selected == null ? "deselect" : "select";
    }
}
