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
    public float X, Y, Size;
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

    private static readonly string[][] EquipLayout =
    {
        new[] { "equip.shoulders", "equip.helmet", "equip.amulet" },
        new[] { "equip.weapon", "equip.chest", "equip.offhand" },
        new[] { "equip.gloves", "equip.pants", "equip.bracer" },
        new[] { "equip.ring1", "equip.boots", "equip.ring2" },
    };

    private static RectTransform _gear = null!, _bag = null!, _char = null!;
    private static readonly List<PageSlot> GearSlots = new(), BagSlots = new();
    private static readonly List<PageHit> Hits = new();
    private static TextMeshProUGUI _sets = null!, _gearCount = null!, _bagCount = null!, _bagPager = null!, _charPager = null!;
    private static TextMeshProUGUI _level = null!, _xpText = null!, _perks = null!;
    private static RectTransform _xpFill = null!;
    private static readonly List<(TextMeshProUGUI Name, TextMeshProUGUI Value)> Rows = new();
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

    private static TextMeshProUGUI TitleBar(RectTransform root, string key, string fallback)
    {
        PageKit.Box(root, "titlebar", 0, 0, PageKit.W, Header - 14, PageKit.TitleBar);
        PageKit.Text(root, "title", 28, 6, 400, Header - 26, UiText.T(key, fallback).ToUpperInvariant(), 38, PageKit.TitleText);
        return PageKit.Text(root, "title-right", PageKit.W - 260, 6, 232, Header - 26, "", 24, PageKit.TitleText, TextAlignmentOptions.MidlineRight);
    }

    private static PageSlot Slot(RectTransform root, string key, float x, float y, float size, string? emptyLabel)
    {
        var s = new PageSlot { Key = key, X = x, Y = y, Size = size };
        s.Bg = PageKit.Box(root, key, x, y, size, size, PageKit.PaperShade);
        s.Frame = PageKit.Frame(root, x, y, size, size, PageKit.InkFaint, 2);
        s.Tape = PageKit.Box(root, key + "-tape", x - 6, y - 4, size * 0.42f, 14, Color.clear);
        s.Tape.rectTransform.localRotation = Quaternion.Euler(0, 0, 18);
        s.Icon = PageKit.Box(root, key + "-icon", x + size * 0.12f, y + size * 0.12f, size * 0.76f, size * 0.76f, Color.white);
        s.Icon.preserveAspect = true;
        s.Icon.enabled = false;
        s.Fallback = PageKit.Text(root, key + "-label", x + 4, y + 4, size - 8, size - 8, emptyLabel ?? "", size * 0.15f, PageKit.InkFaint, TextAlignmentOptions.Center, wrap: true);
        s.Level = PageKit.Text(root, key + "-lvl", x + 4, y + 2, size - 8, 22, "", 18, PageKit.Ink, TextAlignmentOptions.TopRight);
        s.Amount = PageKit.Text(root, key + "-amt", x + 4, y + size - 24, size - 8, 22, "", 18, PageKit.Ink, TextAlignmentOptions.BottomRight);
        s.SelectFrame = PageKit.Frame(root, x - 5, y - 5, size + 10, size + 10, PageKit.Select, 4);
        foreach (var f in s.SelectFrame) f.enabled = false;
        return s;
    }

    private static void BuildGear()
    {
        _gearCount = TitleBar(_gear, "ui.book.tab.gear", "Gear");
        const float cell = 132, gapX = 26, gapY = 26;
        float x0 = (PageKit.W - (3 * cell + 2 * gapX)) / 2, y0 = Header + 28;
        for (int r = 0; r < EquipLayout.Length; r++)
            for (int c = 0; c < 3; c++)
            {
                var key = EquipLayout[r][c];
                GearSlots.Add(Slot(_gear, key, x0 + c * (cell + gapX), y0 + r * (cell + gapY), cell, UiText.SlotName(key)));
            }
        float setsY = y0 + 4 * (cell + gapY) + 4;
        PageKit.Text(_gear, "sets-title", 34, setsY, 600, 30, UiText.T("ui.book.sets", "Set bonuses"), 24, PageKit.Ink);
        _sets = PageKit.Text(_gear, "sets", 34, setsY + 32, PageKit.W - 68, PageKit.H - setsY - 44, "", 20, PageKit.Ink, TextAlignmentOptions.TopLeft, wrap: true);
    }

    private static void BuildBag()
    {
        _bagCount = TitleBar(_bag, "ui.book.tab.bag", "Bag");
        const float cell = 112, gap = 10;
        float x0 = (PageKit.W - (BagCols * cell + (BagCols - 1) * gap)) / 2, y0 = Header + 14;
        for (int i = 0; i < BagPerPage; i++)
            BagSlots.Add(Slot(_bag, "page-bag-" + i, x0 + (i % BagCols) * (cell + gap), y0 + (i / BagCols) * (cell + gap), cell, null));
        _bagPager = Pager(_bag);
        // Sort button in the pager row.
        PageKit.Box(_bag, "sort-bg", PageKit.W - 170, PageKit.H - 66, 140, 46, PageKit.TitleBar);
        PageKit.Text(_bag, "sort", PageKit.W - 170, PageKit.H - 66, 140, 46, UiText.T("ui.book.sort", "Sort"), 24, PageKit.TitleText, TextAlignmentOptions.Center);
    }

    private static TextMeshProUGUI Pager(RectTransform root)
    {
        PageKit.Box(root, "prev-bg", 30, PageKit.H - 66, 60, 46, PageKit.TitleBar);
        PageKit.Text(root, "prev", 30, PageKit.H - 66, 60, 46, "<", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        PageKit.Box(root, "next-bg", 210, PageKit.H - 66, 60, 46, PageKit.TitleBar);
        PageKit.Text(root, "next", 210, PageKit.H - 66, 60, 46, ">", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        return PageKit.Text(root, "pager", 90, PageKit.H - 66, 120, 46, "", 26, PageKit.Ink, TextAlignmentOptions.Center);
    }

    private static void BuildCharacter()
    {
        TitleBar(_char, "ui.book.tab.character", "Character");
        _level = PageKit.Text(_char, "level", 34, Header + 8, 400, 48, "", 40, PageKit.Ink);
        _perks = PageKit.Text(_char, "perks", 330, Header + 14, 302, 40, "", 22, PageKit.Good, TextAlignmentOptions.MidlineRight);
        PageKit.Box(_char, "xp-back", 34, Header + 62, PageKit.W - 68, 16, PageKit.PaperShade);
        _xpFill = PageKit.Box(_char, "xp-fill", 34, Header + 62, PageKit.W - 68, 16, PageKit.RarityColor(3)).rectTransform;
        _xpText = PageKit.Text(_char, "xp-text", 34, Header + 80, PageKit.W - 68, 28, "", 20, PageKit.InkFaint, TextAlignmentOptions.Center);
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
            Hits.Add(new PageHit(s.Key, s.X, s.Y, s.Size, s.Size));
        }
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
            if (exists) Hits.Add(new PageHit(s.Key, s.X, s.Y, s.Size, s.Size));
        }
        _bagCount.text = $"{InventoryActions.BagCount()}/{LootRoller.BagSlots}";
        _bagPager.text = $"{BagPage + 1}/{BagPages}";
        AddPagerHits();
        Hits.Add(new PageHit("sort", PageKit.W - 170, PageKit.H - 66, 140, 46));
    }

    private static void AddPagerHits()
    {
        Hits.Add(new PageHit("prev", 30, PageKit.H - 66, 60, 46));
        Hits.Add(new PageHit("next", 210, PageKit.H - 66, 60, 46));
    }

    private static void RefreshCharacter()
    {
        var sum = SheetData.Summary(Pc);
        _level.text = $"{UiText.T("ui.sheet.level", "Level")} {sum.Level}";
        _perks.text = sum.PerkPoints > 0 ? $"{sum.PerkPoints} {UiText.T("ui.book.perk_points", "perk points")}" : "";
        _xpFill.sizeDelta = new Vector2((PageKit.W - 68) * (float)sum.Fraction, 16);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _xpText.text = $"{Math.Floor(sum.Xp).ToString("N0", inv)} / {sum.Goal.ToString("N0", inv)} XP";

        _sheet = SheetData.Build(Pc);
        // Page 1: attributes, vitals, offense. Page 2: defense, utility.
        int split = _sheet.FindIndex(r => r.Header && r.Id == "defense");
        if (split < 0) split = _sheet.Count / 2;
        var rows = CharPage == 0 ? _sheet.Take(split).ToList() : _sheet.Skip(split).ToList();
        const float rowH = 38, top = Header + 124;
        while (Rows.Count < rows.Count)
        {
            int i = Rows.Count;
            Rows.Add((PageKit.Text(_char, "row-name-" + i, 34, 0, 420, rowH, "", 24, PageKit.Ink),
                PageKit.Text(_char, "row-value-" + i, 420, 0, PageKit.W - 454, rowH, "", 24, PageKit.Ink, TextAlignmentOptions.MidlineRight)));
        }
        for (int i = 0; i < Rows.Count; i++)
        {
            var (name, value) = Rows[i];
            bool on = i < rows.Count;
            name.gameObject.SetActive(on);
            value.gameObject.SetActive(on);
            if (!on) continue;
            var row = rows[i];
            float y = top + i * rowH;
            name.rectTransform.anchoredPosition = new Vector2(34, -y);
            value.rectTransform.anchoredPosition = new Vector2(420, -y);
            name.text = row.Header ? $"<b>{row.Label.ToUpperInvariant()}</b>" : row.Label;
            name.color = row.Header ? PageKit.TitleBar : PageKit.Ink;
            value.text = row.Value;
            if (!row.Header) Hits.Add(new PageHit("row." + row.Id, 30, y, PageKit.W - 60, rowH));
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
