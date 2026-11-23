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
    /// <summary>Merge mode (COS-144): a paper veil over items that cannot join, a tick on picked ones, a NEW tag on merge results.</summary>
    public Image Dim = null!, TickBg = null!, NewBg = null!;
    public Image[] TickMarks = null!;
    public TextMeshProUGUI NewText = null!;
    public ItemInstance? Shown;
    public bool Rendered, WasSelected;
}

/// <summary>
/// The mod pages on the guide book (COS-59, COS-168): Inventory (paper doll and bag), Character (attributes, level, XP, stat sheet),
/// Spells and Perks (their own classes) and Help, each opened by its sub-tab beside the book (<see cref="BookTabs"/>). Built once on
/// the page canvas; refreshed when the inventory or stats change. Clicks and hover come in as page pixels from <see cref="BookFeature"/>.
/// </summary>
internal static class BookPages
{
    public const int BagCols = 6, BagRows = 4, BagPerPage = BagCols * BagRows;
    /// <summary>Bag pages for the current filter (All shows every slot, empty ones too; a filter only the matching items).</summary>
    public static int BagPages => Math.Max(1, (BagKeys().Count + BagPerPage - 1) / BagPerPage);
    private const float Header = 80;
    /// <summary>Page margins: the spiral covers the left edge and the thumb the bottom left (COS-59 screenshots).</summary>
    private const float Left = 62, Right = 16, Bottom = 92;
    private const float PagerY = PageKit.H - Bottom - 50, PagerH = 44;
    private const float NextX = PageKit.W - Right - 56, PrevX = NextX - 56 - 90, SortX = PrevX - 124;
    private const float XpW = PageKit.W - Left - Right;

    /// <summary>The mod page the book tab opens (the last one shown).</summary>
    public static BookTab LastTab { get; private set; } = BookTab.Gear;

    /// <summary>Page titles (COS-168: the sub-tabs beside the book replace the tabs that were drawn in the title bar).</summary>
    private static string TitleOf(BookTab tab) => tab switch
    {
        BookTab.Gear or BookTab.Bag => UiText.T("ui.book.tab.inventory", "Inventory"),
        BookTab.Character => UiText.T("ui.book.tab.character", "Character"),
        _ => UiText.T("ui.book.tab.help", "Help"),
    };

    /// <summary>Equip slots in a body shape, page pixels (x, y from the top-left, width, height).</summary>
    private static readonly (string Key, float X, float Y, float W, float H)[] BodyLayout =
    {
        ("equip.helmet", 296, 84, 72, 72),
        ("equip.chest", 282, 164, 100, 112),
        ("equip.pants", 282, 284, 100, 104),
        ("equip.boots", 296, 396, 72, 64),
        ("equip.shoulders", 192, 164, 72, 72),
        ("equip.gloves", 192, 244, 72, 72),
        ("equip.ring1", 204, 324, 56, 56),
        ("equip.amulet", 400, 164, 72, 72),
        ("equip.bracer", 400, 244, 72, 72),
        ("equip.ring2", 412, 324, 56, 56),
        ("equip.weapon", 92, 176, 82, 170),
        ("equip.offhand", 490, 176, 82, 170),
    };

    private static TextMeshProUGUI _gearStats = null!;
    private static Image _ghost = null!;

    /// <summary>Bag filters (layout D, Max's pick): All, Weapons (weapons and off-hands), Armor, Jewelry.</summary>
    private static readonly (string Key, string Fallback, float W)[] Filters =
    {
        ("ui.book.filter.all", "All", 70), ("ui.book.filter.weapons", "Weapons", 120), ("ui.book.filter.armor", "Armor", 120), ("ui.book.filter.jewelry", "Jewelry", 120),
    };
    private static readonly List<(Image Bg, TextMeshProUGUI Text, float X)> FilterChips = new();
    private const float ChipY = 480, ChipH = 36, BagX = Left + 30, BagY = 526, BagCell = 80, BagGap = 8;
    public static int Filter { get; private set; }
    private static RectTransform _charStats = null!;

    private static bool Matches(ItemInstance item, int filter)
    {
        if (filter == 0) return true;
        if (!ModContent.Db.Items.TryGetValue(item.BaseId, out var def)) return false;
        return filter switch
        {
            1 => def.Type is "weapon" or "shield" or "quiver" or "spell_scroll",
            2 => def.Type is "helmet" or "chest_armor" or "pants" or "boot" or "shoulder_armor" or "glove" or "bracer",
            3 => def.Type is "ring" or "amulet",
            _ => true,
        };
    }

    /// <summary>The bag slot keys the grid shows, in order, for the current filter.</summary>
    private static List<string> BagKeys()
    {
        var keys = new List<string>();
        for (int i = 0; i < LootRoller.BagSlots; i++)
        {
            var key = Inventory.Bag(i);
            if (Filter == 0) { keys.Add(key); continue; }
            var item = Pc.Inventory.Get(key);
            if (item != null && Matches(item, Filter)) keys.Add(key);
        }
        return keys;
    }

    private static RectTransform _gear = null!, _char = null!, _help = null!, _spells = null!, _perksPage = null!;
    private static TextMeshProUGUI _helpTitle = null!, _helpBody = null!, _helpPager = null!;
    private static readonly List<(TextMeshProUGUI Name, TextMeshProUGUI Key)> HelpRows = new();
    private const float HelpRowH = 30;
    public static int HelpPage { get; private set; }
    private static readonly List<PageSlot> GearSlots = new(), BagSlots = new();
    private static readonly List<PageHit> Hits = new();
    private static TextMeshProUGUI _sets = null!, _gearCount = null!, _bagPager = null!, _charPager = null!;
    private static TextMeshProUGUI _level = null!, _xpText = null!, _perks = null!;
    private static RectTransform _xpFill = null!;
    private static readonly List<(TextMeshProUGUI Name, TextMeshProUGUI Value, Image Shade)> Rows = new();
    private static List<SheetRow> _sheet = new();
    private static bool _dirty = true;

    public static int BagPage { get; private set; }
    public static int CharPage { get; private set; }

    /// <summary>Character pages: the stat pages plus the stat box page (set when the sheet is built).</summary>
    public static int CharPages { get; private set; } = 3;

    private const int RowsPerPage = 20;

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
        _char = PageKit.Fill(page, "character");
        _help = PageKit.Fill(page, "help");
        BuildGear();
        BuildCharacter();
        BuildHelp();
        _spells = BookSpellsPage.Build(page);
        _perksPage = BookPerksPage.Build(page);
        Pc.Inventory.Changed += MarkDirty;
        Pc.Stats.Changed += MarkDirty;
        Show(BookTab.None);
    }

    /// <summary>The title bar: the page's name on the left, a short note on the right (returned).</summary>
    private static TextMeshProUGUI TitleBar(RectTransform root, BookTab own)
    {
        PageKit.Box(root, "titlebar", 0, 0, PageKit.W, Header - 14, PageKit.TitleBar);
        PageKit.Text(root, "title", Left, 12, 320, Header - 26, TitleOf(own).ToUpperInvariant(), 26, PageKit.TitleText);
        return PageKit.Text(root, "title-right", PageKit.W - Right - 230, 12, 224, Header - 26, "", 20, PageKit.TitleText, TextAlignmentOptions.MidlineRight);
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
        // Empty-slot labels shrink to fit narrow slots ("Shoulders" wrapped in 72 px).
        s.Fallback.enableAutoSizing = true;
        s.Fallback.fontSizeMin = 10;
        s.Fallback.fontSizeMax = Mathf.Max(15, size * 0.17f);
        s.Fallback.enableWordWrapping = false;
        s.Level = PageKit.Text(root, key + "-lvl", x + 4, y + 2, w - 8, 22, "", 18, PageKit.Ink, TextAlignmentOptions.TopRight);
        s.Amount = PageKit.Text(root, key + "-amt", x + 4, y + h - 24, w - 8, 22, "", 18, PageKit.Ink, TextAlignmentOptions.BottomRight);
        s.SelectFrame = PageKit.Frame(root, x - 5, y - 5, w + 10, h + 10, PageKit.Select, 4);
        foreach (var f in s.SelectFrame) f.enabled = false;
        s.Dim = PageKit.Box(root, key + "-dim", x, y, w, h, new Color(PageKit.Paper.r, PageKit.Paper.g, PageKit.Paper.b, 0.72f));
        s.Dim.enabled = false;
        // A tick drawn from two bars on a red square, bottom right (the font may not have a check glyph).
        float tx = x + w - 28, ty = y + h - 28;
        s.TickBg = PageKit.Box(root, key + "-tick", tx, ty, 24, 24, PageKit.Select);
        var shortBar = PageKit.Box(root, key + "-tick-a", tx + 5, ty + 11, 8, 3, PageKit.TitleText);
        shortBar.rectTransform.localRotation = Quaternion.Euler(0, 0, -45);
        var longBar = PageKit.Box(root, key + "-tick-b", tx + 9, ty + 16, 13, 3, PageKit.TitleText);
        longBar.rectTransform.localRotation = Quaternion.Euler(0, 0, 50);
        s.TickMarks = new[] { s.TickBg, shortBar, longBar };
        foreach (var m in s.TickMarks) m.enabled = false;
        s.NewBg = PageKit.Box(root, key + "-new", x + 3, y + h - 21, 42, 18, PageKit.Good);
        s.NewText = PageKit.Text(root, key + "-new-text", x + 3, y + h - 21, 42, 18, UiText.T("ui.merge.new", "NEW"), 14, PageKit.TitleText, TextAlignmentOptions.Center);
        s.NewBg.enabled = false;
        s.NewText.enabled = false;
        return s;
    }

    private static void BuildGear()
    {
        _gearCount = TitleBar(_gear, BookTab.Gear);
        // Slots in the shape of a body (COS-59, Max's reference): head, chest, legs and feet down the middle,
        // shoulders/gloves/ring on one side, neck/bracer/ring on the other, weapon and off-hand as tall slots outside.
        foreach (var (key, x, y, w, h) in BodyLayout)
            GearSlots.Add(Slot(_gear, key, x, y, w, h, UiText.SlotName(key)));
        // The bag on the same page (layout D): a rule, the filter chips, a 6x4 grid, then Sort and the pager.
        PageKit.Box(_gear, "bag-rule", Left, 470, PageKit.W - Left - Right, 2, PageKit.InkFaint);
        float cx = BagX;
        foreach (var (key, fallback, w) in Filters)
        {
            var bg = PageKit.Box(_gear, "chip-" + key, cx, ChipY, w, ChipH, PageKit.PaperShade);
            var text = PageKit.Text(_gear, "chip-text-" + key, cx, ChipY, w, ChipH, UiText.T(key, fallback), 18, PageKit.Ink, TextAlignmentOptions.Center);
            FilterChips.Add((bg, text, cx));
            cx += w + 8;
        }
        for (int i = 0; i < BagPerPage; i++)
            BagSlots.Add(Slot(_gear, "page-bag-" + i, BagX + (i % BagCols) * (BagCell + BagGap), BagY + (i / BagCols) * (BagCell + BagGap), BagCell, null));
        _bagPager = Pager(_gear);
        _sortBg = PageKit.Box(_gear, "sort-bg", SortX, PagerY, 110, PagerH, PageKit.TitleBar);
        PageKit.Text(_gear, "sort", SortX, PagerY, 110, PagerH, UiText.T("ui.book.sort", "Sort"), 24, PageKit.TitleText, TextAlignmentOptions.Center);
        BuildMerge();
        // The dragged item's icon follows the cursor, drawn last so it sits over the slots.
        _ghost = PageKit.Box(_gear, "drag-ghost", 0, 0, BagCell * 0.8f, BagCell * 0.8f, new Color(1, 1, 1, 0.85f));
        _ghost.preserveAspect = true;
        _ghost.enabled = false;
    }

    // Merge mode (COS-144, layout E): the Merge button left of Sort; in merge mode a strip replaces the filter chips and the
    // pager row holds Cancel and "Merge n/N".
    private const float MergeX = SortX - 118, MergeW = 110, CancelX = BagX, CancelW = 100, GoX = CancelX + CancelW + 6, GoW = SortX - 6 - GoX;
    private static Image _sortBg = null!, _mergeBg = null!, _cancelBg = null!, _goBg = null!, _stripBg = null!, _stripTape = null!;
    private static TextMeshProUGUI _mergeText = null!, _cancelText = null!, _goText = null!, _stripTitle = null!, _stripRule = null!, _stripPicked = null!;

    private static void BuildMerge()
    {
        _mergeBg = PageKit.Box(_gear, "merge-bg", MergeX, PagerY, MergeW, PagerH, PageKit.PaperShade);
        _mergeText = PageKit.Text(_gear, "merge", MergeX, PagerY, MergeW, PagerH, UiText.T("ui.merge.button", "Merge"), 24, PageKit.InkFaint, TextAlignmentOptions.Center);
        _cancelBg = PageKit.Box(_gear, "merge-cancel-bg", CancelX, PagerY, CancelW, PagerH, PageKit.PaperShade);
        _cancelText = PageKit.Text(_gear, "merge-cancel", CancelX, PagerY, CancelW, PagerH, UiText.T("ui.merge.cancel", "Cancel"), 22, PageKit.Ink, TextAlignmentOptions.Center);
        _goBg = PageKit.Box(_gear, "merge-go-bg", GoX, PagerY, GoW, PagerH, PageKit.Select);
        _goText = PageKit.Text(_gear, "merge-go", GoX, PagerY, GoW, PagerH, "", 22, PageKit.TitleText, TextAlignmentOptions.Center);
        float w = PageKit.W - Left - Right;
        _stripBg = PageKit.Box(_gear, "merge-strip", Left, ChipY - 4, w, ChipH + 8, PageKit.TitleBar);
        _stripTitle = PageKit.Text(_gear, "merge-strip-title", Left + 10, ChipY - 4, 90, ChipH + 8, UiText.T("ui.merge.title", "MERGE"), 20, PageKit.TitleText);
        _stripTape = PageKit.Box(_gear, "merge-strip-tape", Left + 100, ChipY + 4, 10, ChipH - 8, Color.clear);
        _stripRule = PageKit.Text(_gear, "merge-strip-rule", Left + 118, ChipY - 4, 300, ChipH + 8, "", 17, PageKit.TitleText);
        _stripRule.enableAutoSizing = true;
        _stripRule.fontSizeMin = 12;
        _stripRule.fontSizeMax = 17;
        _stripPicked = PageKit.Text(_gear, "merge-strip-picked", Left + 420, ChipY - 4, w - 430, ChipH + 8, "", 16, PageKit.TitleText, TextAlignmentOptions.MidlineRight);
        _stripPicked.enableAutoSizing = true;
        _stripPicked.fontSizeMin = 11;
        _stripPicked.fontSizeMax = 16;
    }

    private static void RefreshMerge()
    {
        bool on = BookMerge.Active;
        foreach (var (bg, text, _) in FilterChips) { bg.enabled = !on; text.enabled = !on; }
        _stripBg.enabled = _stripTitle.enabled = _stripRule.enabled = _stripPicked.enabled = _stripTape.enabled = on;
        _mergeBg.enabled = _mergeText.enabled = !on;
        _cancelBg.enabled = _cancelText.enabled = _goBg.enabled = _goText.enabled = on;
        _sortBg.color = on ? PageKit.PaperShade : PageKit.TitleBar;
        if (on)
        {
            _stripTape.color = PageKit.RarityColor((int)BookMerge.Kind.Rarity);
            _stripRule.text = BookMerge.RuleText();
            _stripPicked.text = BookMerge.PickedText();
            _goText.text = string.Format(UiText.T("ui.merge.go", "Merge {0}/{1}"), BookMerge.Picks.Count, BookMerge.Need);
            _goBg.color = BookMerge.Ready ? PageKit.Select : PageKit.PaperShade;
            _goText.color = BookMerge.Ready ? PageKit.TitleText : PageKit.InkFaint;
            Hits.Add(new PageHit("merge.cancel", CancelX, PagerY, CancelW, PagerH));
            Hits.Add(new PageHit("merge.go", GoX, PagerY, GoW, PagerH));
        }
        else
        {
            bool can = BookMerge.CanStart(Selected);
            _mergeBg.color = can ? PageKit.Select : PageKit.PaperShade;
            _mergeText.color = can ? PageKit.TitleText : PageKit.InkFaint;
            Hits.Add(new PageHit("merge.start", MergeX, PagerY, MergeW, PagerH));
        }
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
        BuildAttributes();
        // Page 3: the stat box and set bonuses (moved off the Gear page to make room for the bag).
        _charStats = PageKit.Fill(_char, "char-stats");
        float boxY = Header + 124;
        PageKit.Box(_charStats, "stats-box", Left, boxY, XpW, 70, PageKit.TitleBar);
        _gearStats = PageKit.Text(_charStats, "stats", Left + 16, boxY, XpW - 32, 70, "", 24, PageKit.TitleText, TextAlignmentOptions.Center);
        float setsY = boxY + 86;
        PageKit.Text(_charStats, "sets-title", Left, setsY, 560, 30, UiText.T("ui.book.sets", "Set bonuses"), 24, PageKit.Ink);
        _sets = PageKit.Text(_charStats, "sets", Left, setsY + 34, XpW, PagerY - setsY - 44, "", 20, PageKit.Ink, TextAlignmentOptions.TopLeft, wrap: true);
    }

    // Attributes (COS-174): the first character page. One row each: name and value, what a point gives, - and +.
    private static RectTransform _attr = null!;
    private static TextMeshProUGUI _attrLeft = null!, _attrResetText = null!;
    private static Image _attrResetBg = null!;
    private static readonly List<(string Id, TextMeshProUGUI Name, TextMeshProUGUI Value, TextMeshProUGUI Per, Image MinusBg, Image PlusBg)> AttrRows = new();
    private const float AttrTop = Header + 156, AttrRowH = 116, AttrBtn = 52, PlusX = PageKit.W - Right - AttrBtn, MinusX = PlusX - AttrBtn - 10;
    private const float ResetX = Left, ResetW = 230, ResetY = AttrTop + 4 * AttrRowH + 8, ResetH = 44;
    private static bool _attrArmed;
    private static float _attrArmedAt;

    private static void BuildAttributes()
    {
        _attr = PageKit.Fill(_char, "attributes");
        _attrLeft = PageKit.Text(_attr, "attr-left", Left, AttrTop - 34, XpW, 30, "", 22, PageKit.Good);
        for (int i = 0; i < AttributePoints.Ids.Length; i++)
        {
            var id = AttributePoints.Ids[i];
            float y = AttrTop + i * AttrRowH;
            PageKit.Box(_attr, "attr-shade-" + id, Left - 6, y, XpW + 12, AttrRowH - 10, i % 2 == 0 ? new Color(0, 0, 0, 0.06f) : Color.clear);
            var name = PageKit.Text(_attr, "attr-name-" + id, Left, y + 6, 300, 38, "", 30, PageKit.Ink);
            var value = PageKit.Text(_attr, "attr-value-" + id, Left + 300, y + 6, MinusX - Left - 310, 38, "", 30, PageKit.TitleBar, TextAlignmentOptions.MidlineRight);
            var per = PageKit.Text(_attr, "attr-per-" + id, Left, y + 46, MinusX - Left - 10, AttrRowH - 60, "", 17, PageKit.Ink, TextAlignmentOptions.TopLeft, wrap: true);
            var minus = PageKit.Box(_attr, "attr-minus-bg-" + id, MinusX, y + 20, AttrBtn, AttrBtn, PageKit.PaperShade);
            PageKit.Text(_attr, "attr-minus-" + id, MinusX, y + 20, AttrBtn, AttrBtn, "-", 34, PageKit.Ink, TextAlignmentOptions.Center);
            var plus = PageKit.Box(_attr, "attr-plus-bg-" + id, PlusX, y + 20, AttrBtn, AttrBtn, PageKit.PaperShade);
            PageKit.Text(_attr, "attr-plus-" + id, PlusX, y + 20, AttrBtn, AttrBtn, "+", 34, PageKit.Ink, TextAlignmentOptions.Center);
            AttrRows.Add((id, name, value, per, minus, plus));
        }
        _attrResetBg = PageKit.Box(_attr, "attr-reset-bg", ResetX, ResetY, ResetW, ResetH, PageKit.TitleBar);
        _attrResetText = PageKit.Text(_attr, "attr-reset", ResetX, ResetY, ResetW, ResetH, "", 20, PageKit.TitleText, TextAlignmentOptions.Center);
        PageKit.Text(_attr, "attr-hint", ResetX + ResetW + 10, ResetY, XpW - ResetW - 10, ResetH,
            UiText.T("ui.book.attr.hint", "Right-click + or - for 10. Free to change."), 16, PageKit.Ink, TextAlignmentOptions.MidlineRight);
    }

    private static void RefreshAttributes()
    {
        if (_attrArmed && Time.unscaledTime - _attrArmedAt > 4) _attrArmed = false;
        int left = Pc.AttributePointsLeft;
        _attrLeft.text = string.Format(UiText.T("ui.book.attr.left", "{0} attribute points to spend"), left);
        _attrLeft.color = left > 0 ? PageKit.Good : PageKit.InkFaint;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var (id, name, value, per, minusBg, plusBg) in AttrRows)
        {
            float y = AttrTop + AttributePoints.Ids.ToList().IndexOf(id) * AttrRowH;
            int spent = Pc.Attributes.Get(id);
            name.text = Perks.PerkInfo.AttributeName(id);
            value.text = $"{Pc.Stats.Get(id).ToString("0", inv)}  <size=18><color={PageKit.HexOf(PageKit.InkFaint)}>({spent})</color></size>";
            per.text = Perks.PerkInfo.AttributePerPoint(id);
            minusBg.color = spent > 0 ? PageKit.PaperShade : new Color(PageKit.PaperShade.r, PageKit.PaperShade.g, PageKit.PaperShade.b, 0.4f);
            plusBg.color = left > 0 ? PageKit.Good : new Color(PageKit.PaperShade.r, PageKit.PaperShade.g, PageKit.PaperShade.b, 0.4f);
            Hits.Add(new PageHit("attr.minus." + id, MinusX, y + 20, AttrBtn, AttrBtn));
            Hits.Add(new PageHit("attr.plus." + id, PlusX, y + 20, AttrBtn, AttrBtn));
            Hits.Add(new PageHit("attr." + id, Left, y, MinusX - Left - 10, AttrRowH - 10));
        }
        _attrResetText.text = _attrArmed ? UiText.T("ui.book.perks.confirm", "Click again to refund") : UiText.T("ui.book.attr.reset", "Reset attributes");
        _attrResetBg.color = _attrArmed ? PageKit.Select : PageKit.TitleBar;
        Hits.Add(new PageHit("attr.reset", ResetX, ResetY, ResetW, ResetH));
    }

    private static string? ClickAttribute(string key, bool right)
    {
        int n = right ? 10 : 1;
        if (key.StartsWith("attr.plus.", StringComparison.Ordinal)) return "spent " + Pc.SpendAttribute(key.Substring("attr.plus.".Length), n);
        if (key.StartsWith("attr.minus.", StringComparison.Ordinal)) return "refunded " + Pc.RefundAttribute(key.Substring("attr.minus.".Length), n);
        if (key == "attr.reset")
        {
            if (!_attrArmed || Time.unscaledTime - _attrArmedAt > 4) { _attrArmed = true; _attrArmedAt = Time.unscaledTime; return "armed"; }
            _attrArmed = false;
            Pc.ResetAttributes();
            return "reset";
        }
        return key.StartsWith("attr.", StringComparison.Ordinal) ? "row" : null;
    }

    private static void BuildHelp()
    {
        TitleBar(_help, BookTab.Help);
        _helpTitle = PageKit.Text(_help, "help-title", Left, Header + 12, XpW, 48, "", 34, PageKit.TitleBar);
        PageKit.Box(_help, "help-rule", Left, Header + 64, XpW, 2, PageKit.InkFaint);
        _helpBody = PageKit.Text(_help, "help-body", Left, Header + 80, XpW, PagerY - Header - 96, "", 23, PageKit.Ink, TextAlignmentOptions.TopLeft, wrap: true);
        _helpPager = Pager(_help);
    }

    // ------------------------------------------------------------------ show / refresh

    public static void Show(BookTab tab)
    {
        _gear.gameObject.SetActive(tab is BookTab.Gear or BookTab.Bag);
        _char.gameObject.SetActive(tab == BookTab.Character);
        _help.gameObject.SetActive(tab == BookTab.Help);
        _spells.gameObject.SetActive(tab == BookTab.Spells);
        _perksPage.gameObject.SetActive(tab == BookTab.Perks);
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
        if (tab != BookTab.None) LastTab = tab;
        switch (tab)
        {
            case BookTab.Gear:
            case BookTab.Bag:
                RefreshGear();
                RefreshBag();
                break;
            case BookTab.Character: RefreshCharacter(); break;
            case BookTab.Help: RefreshHelp(); break;
            case BookTab.Spells: BookSpellsPage.Refresh(Hits); break;
            case BookTab.Perks: BookPerksPage.Refresh(Hits); break;
        }
    }

    private static void RefreshGear()
    {
        foreach (var s in GearSlots)
        {
            RenderSlot(s, Pc.Inventory.Get(s.Key));
            Hits.Add(new PageHit(s.Key, s.X, s.Y, s.W, s.H));
        }
        _gearCount.text = $"{UiText.T("ui.book.tab.bag", "Bag")} {InventoryActions.BagCount()}/{LootRoller.BagSlots}";
    }

    private static void RefreshBag()
    {
        BookMerge.Validate();
        var keys = BagKeys();
        int pages = Math.Max(1, (keys.Count + BagPerPage - 1) / BagPerPage);
        BagPage = Math.Clamp(BagPage, 0, pages - 1);
        for (int i = 0; i < BagSlots.Count; i++)
        {
            var s = BagSlots[i];
            int index = BagPage * BagPerPage + i;
            bool exists = index < keys.Count;
            s.Key = exists ? keys[index] : "";
            RenderSlot(s, exists ? Pc.Inventory.Get(s.Key) : null, force: true);
            if (exists) Hits.Add(new PageHit(s.Key, s.X, s.Y, s.W, s.H));
        }
        for (int i = 0; i < FilterChips.Count; i++)
        {
            var (bg, text, x) = FilterChips[i];
            bool on = i == Filter;
            bg.color = on ? PageKit.TitleBar : PageKit.PaperShade;
            text.color = on ? PageKit.TitleText : PageKit.Ink;
            if (!BookMerge.Active) Hits.Add(new PageHit("filter." + i, x, ChipY, Filters[i].W, ChipH));
        }
        _bagPager.text = $"{BagPage + 1}/{pages}";
        AddPagerHits();
        Hits.Add(new PageHit("sort", SortX, PagerY, 110, PagerH));
        RefreshMerge();
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
        // Page 1: attributes (COS-174). Then stat pages of whole sections, up to RowsPerPage rows each (COS-97); the last page is
        // the stat box and set bonuses.
        var pages = SheetData.Paginate(_sheet, RowsPerPage);
        CharPages = pages.Count + 2;
        if (CharPage >= CharPages) CharPage = CharPages - 1;
        bool attributes = CharPage == 0;
        bool statBox = CharPage == CharPages - 1;
        var rows = statBox || attributes ? new List<SheetRow>() : pages[CharPage - 1];
        _attr.gameObject.SetActive(attributes);
        if (attributes) RefreshAttributes();
        _charStats.gameObject.SetActive(statBox);
        if (statBox)
        {
            var st = Pc.Stats;
            _gearStats.text = $"{UiText.T("ui.sheet.armor", "Armor")} <b>{st.Get(StatIds.Armor).ToString("N0", inv)}</b>    "
                + $"{UiText.T("ui.sheet.max_health", "Max health")} <b>{Derived.MaxHealth(st).ToString("N0", inv)}</b>    "
                + $"{UiText.T("ui.sheet.melee_damage", "Melee damage")} <b>x{Derived.MeleeMultiplier(st).ToString("0.00", inv)}</b>";
            var sets = InventoryActions.SetBonusText(PageKit.HexOf(PageKit.TitleBar), PageKit.HexOf(PageKit.Good), PageKit.HexOf(PageKit.InkFaint));
            _sets.text = (sets.Length == 0 ? $"<color={PageKit.HexOf(PageKit.InkFaint)}>{UiText.T("ui.sets.none", "No set pieces equipped.")}</color>" : sets)
                + SheetData.PartyText(PageKit.HexOf(PageKit.TitleBar), PageKit.HexOf(PageKit.InkFaint));
        }
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
        _charPager.text = $"{CharPage + 1}/{CharPages}";
        AddPagerHits();
    }

    private static void RefreshHelp()
    {
        int pages = BookGuide.Pages;
        HelpPage = Math.Clamp(HelpPage, 0, pages - 1);
        var (title, body) = BookGuide.Page(HelpPage, PageKit.HexOf(PageKit.TitleBar), PageKit.HexOf(PageKit.InkFaint));
        _helpTitle.text = title;
        // The Keys page: one fixed row per action (name left, key right) so each key sits on its line on the tilted page;
        // the body then holds only the footer, below the rows.
        var keys = HelpPage == 1 ? BookGuide.KeyRows() : new List<(string Name, string Key)>();
        float top = Header + 80;
        while (HelpRows.Count < keys.Count)
        {
            int n = HelpRows.Count;
            // Keys sit right after the names: the book is tilted, so a key at the far right edge reads as one row higher.
            HelpRows.Add((PageKit.Text(_help, "help-row-name-" + n, Left, top + n * HelpRowH, 230, HelpRowH, "", 23, PageKit.Ink, TextAlignmentOptions.MidlineLeft),
                PageKit.Text(_help, "help-row-key-" + n, Left + 240, top + n * HelpRowH, 160, HelpRowH, "", 23, PageKit.TitleBar, TextAlignmentOptions.MidlineLeft)));
        }
        for (int n = 0; n < HelpRows.Count; n++)
        {
            bool on = n < keys.Count;
            HelpRows[n].Name.gameObject.SetActive(on);
            HelpRows[n].Key.gameObject.SetActive(on);
            if (!on) continue;
            HelpRows[n].Name.text = keys[n].Name;
            HelpRows[n].Key.text = keys[n].Key;
        }
        float bodyTop = keys.Count > 0 ? top + keys.Count * HelpRowH + 16 : top;
        _helpBody.rectTransform.anchoredPosition = new Vector2(Left, -bodyTop);
        _helpBody.text = HelpPage == 1 ? BookGuide.KeysFooter(PageKit.HexOf(PageKit.InkFaint)) : body;
        _helpPager.text = $"{HelpPage + 1}/{pages}";
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
        bool merging = BookMerge.Active && item != null;
        bool dim = merging && !BookMerge.Matches(s.Key);
        bool picked = merging && BookMerge.Picks.Contains(s.Key);
        bool isNew = BookMerge.IsNew(item);
        s.Dim.enabled = dim;
        foreach (var m in s.TickMarks) m.enabled = picked;
        s.NewBg.enabled = s.NewText.enabled = isNew;
        s.Bg.color = merging && !dim ? PageKit.Paper : PageKit.PaperShade;
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
        if (tab is BookTab.Bag or BookTab.Gear) BagPage = (BagPage + delta + BagPages) % BagPages;
        else if (tab == BookTab.Character) CharPage = (CharPage + delta + CharPages) % CharPages;
        else if (tab == BookTab.Help) HelpPage = (HelpPage + delta + BookGuide.Pages) % BookGuide.Pages;
        else if (tab == BookTab.Spells) BookSpellsPage.Page = (BookSpellsPage.Page + delta + BookSpellsPage.Pages) % BookSpellsPage.Pages;
        else if (tab == BookTab.Perks)
        {
            // Shoulders and the wheel step through the trees.
            int i = Array.IndexOf(Perks.PerkInfo.Trees, BookPerksPage.Current);
            BookPerksPage.ShowTree(Perks.PerkInfo.Trees[(i + delta + Perks.PerkInfo.Trees.Length) % Perks.PerkInfo.Trees.Length]);
        }
        _dirty = true;
    }

    /// <summary>Shows the dragged item's icon centred on a page point; null hides it.</summary>
    public static void SetDragGhost(ItemInstance? item, Vector2 px)
    {
        if (_ghost == null) return;
        var sprite = item != null ? Icons.IconCache.GetSprite(item.BaseId) : null;
        _ghost.enabled = sprite != null;
        if (sprite == null) return;
        _ghost.sprite = sprite;
        float half = _ghost.rectTransform.sizeDelta.x / 2;
        _ghost.rectTransform.anchoredPosition = new Vector2(px.x - half, -(px.y - half));
        _ghost.transform.SetAsLastSibling();
    }

    /// <summary>
    /// A drag released on <paramref name="to"/>: bag to equip slot (equip, swapping out what was there), equip slot to
    /// bag (unequip into that slot, or swap with a fitting item), bag to bag (move or swap). Returns what happened.
    /// </summary>
    public static string Drop(string from, string? to)
    {
        _dirty = true;
        Selected = null;
        if (to == null || to == from || BookMerge.Active) return "cancel";
        bool fromItem = from.StartsWith("bag.", StringComparison.Ordinal) || from.StartsWith("equip.", StringComparison.Ordinal);
        bool toItem = to.StartsWith("bag.", StringComparison.Ordinal) || to.StartsWith("equip.", StringComparison.Ordinal);
        if (!fromItem || !toItem || Pc.Inventory.Get(from) == null) return "cancel";
        return InventoryActions.MoveTo(from, to) ? "moved" : "no-fit";
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
            case "tab.Help": BookHost.ShowModPage(BookTab.Help); return "tab";
            case "tab.Spells": BookHost.ShowModPage(BookTab.Spells); return "tab";
            case "tab.Perks": BookHost.ShowModPage(BookTab.Perks); return "tab";
            case "prev": Page(tab, -1); return "page";
            case "next": Page(tab, +1); return "page";
            case "sort":
                if (BookMerge.Active) return "merge-busy";
                InventoryActions.SortBag(); Selected = null; return "sort";
            case "merge.start":
                if (Selected == null || !BookMerge.Start(Selected)) return "merge-off";
                Selected = null;
                _dirty = true;
                return "merge-start";
            case "merge.cancel":
                Selected = BookMerge.Cancel();
                _dirty = true;
                return "merge-cancel";
            case "merge.go":
                var merged = BookMerge.Go();
                _dirty = true;
                if (merged is not { Ok: true }) return merged == null ? "merge-not-ready" : "merge-error " + merged.Error;
                if (!BookMerge.Active) Selected = merged.Slot;
                return "merged";
            case "filter.0": case "filter.1": case "filter.2": case "filter.3":
                Filter = key[^1] - '0';
                BagPage = 0;
                _dirty = true;
                return "filter";
        }
        if (key.StartsWith("row.", StringComparison.Ordinal)) return "row";
        _dirty = true;
        if (tab == BookTab.Character && ClickAttribute(key, right) is string attr) return attr;
        if (tab == BookTab.Spells) return BookSpellsPage.Click(key) ?? "none";
        if (tab == BookTab.Perks) return BookPerksPage.Click(key, right) ?? "none";
        var item = Pc.Inventory.Get(key);
        _dirty = true;
        BookMerge.ClearNew(item);
        if (BookMerge.Active) return right ? "merge-busy" : "merge-" + BookMerge.Toggle(key);
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
