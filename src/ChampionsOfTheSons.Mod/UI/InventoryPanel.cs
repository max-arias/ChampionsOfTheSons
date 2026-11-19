using System.Globalization;
using System.Text;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Icons;
using ChampionsOfTheSons.Mod.Loot;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>One cell of the bag or equipment grid.</summary>
internal sealed class SlotWidget
{
    public string Key = "";
    public bool IsEquip;

    /// <summary>Top-left corner relative to the window's top-left, in canvas units.</summary>
    public float X, Y, Size;

    public SBgButtonOptions Button = null!;
    public SImageOptions Icon = null!;
    public SLabelOptions Level = null!;
    public SLabelOptions Amount = null!;

    public bool Rendered;
    public ItemInstance? Shown;
}

/// <summary>
/// The inventory window: character sheet (left), 12 equipment slots (middle) and the 120-slot bag (right),
/// with an item tooltip. Built once with SUI (uGUI), shown and hidden with <see cref="Open"/>/<see cref="Close"/>.
/// Slots are placed absolutely so the tooltip can be positioned from the slot's known rectangle.
/// </summary>
internal sealed partial class InventoryPanel
{
    public const string PanelId = "cots-inventory";

    private const float WinW = 1640, WinH = 900;
    private const float BodyTop = 72, PanelH = WinH - BodyTop - 20;

    private const float SheetX = 20, SheetW = 400;
    private const float EquipX = 436, EquipW = 330;
    private const float BagX = 782, BagW = 838;

    private const float EquipCell = 92, EquipGap = 12;
    private const float BagCell = 62, BagGap = 4;
    private const int BagCols = 12;

    private const float TipW = 460;

    private static readonly string[][] EquipLayout =
    {
        new[] { "equip.shoulders", "equip.helmet", "equip.amulet" },
        new[] { "equip.weapon", "equip.chest", "equip.offhand" },
        new[] { "equip.gloves", "equip.pants", "equip.bracer" },
        new[] { "equip.ring1", "equip.boots", "equip.ring2" },
    };

    private SPanelOptions _root = null!;
    private SContainerOptions _window = null!;

    private readonly List<SlotWidget> _slots = new();
    private readonly Dictionary<string, SlotWidget> _slotByKey = new();
    private readonly Dictionary<int, string> _slotByObjectId = new();

    // character sheet
    private SLabelOptions _levelLabel = null!;
    private SLabelOptions _xpLabel = null!;
    private SContainerOptions _xpFill = null!;
    private readonly Dictionary<string, (SLabelOptions Name, SLabelOptions Value)> _sheetRows = new();
    private SContainerOptions _sheetPanel = null!;
    private List<SheetRow> _sheetData = new();

    // side widgets
    private SLabelOptions _bagCount = null!;
    private SLabelOptions _setsLabel = null!;

    // tooltip
    private SContainerOptions _tip = null!;
    private SImageOptions _tipIcon = null!;
    private SLabelOptions _tipTitle = null!, _tipSub = null!, _tipBody = null!, _tipCompare = null!;
    private SContainerOptions _tipDivider = null!;

    private bool _dirty = true;
    private float _sheetTimer;
    private string? _hoverKey;
    private string? _simulatedHover;
    private bool _useSimulatedHover;
    private int _lastToggleFrame = -1;

    public bool IsOpen => _root != null && _root.Root.activeSelf;

    /// <summary>What the tooltip currently shows, or null when hidden.</summary>
    public ItemDescription? TooltipShown { get; private set; }

    public string? TooltipSlot { get; private set; }

    /// <summary>True when the tooltip had no room to the right of its slot and sits on the left.</summary>
    public bool TooltipOnLeft { get; private set; }

    /// <summary>Where the tooltip sits, in canvas units with the origin at the top-left corner.</summary>
    public Rect TooltipBounds { get; private set; }

    /// <summary>The canvas size in canvas units (1080 high; the width follows the screen's aspect ratio).</summary>
    public Vector2 CanvasSize => SUIViewport.GetComponent<RectTransform>().rect.size;

    /// <summary>A slot's rectangle in canvas units with the origin at the top-left corner.</summary>
    public Rect SlotBounds(string slotKey)
    {
        var w = _slotByKey[slotKey];
        var c = CanvasSize;
        return new Rect((c.x - WinW) / 2 + w.X, (c.y - WinH) / 2 + w.Y, w.Size, w.Size);
    }

    public IReadOnlyList<SlotWidget> Slots => _slots;

    public IReadOnlyList<SheetRow> SheetRows => _sheetData;

    public SlotWidget? Slot(string key) => _slotByKey.TryGetValue(key, out var w) ? w : null;

    private static PlayerCharacter Character => PlayerState.Character;

    private static Color C(string hex) => ColorFromString(hex);

    // ---------------------------------------------------------------- build

    public void Build()
    {
        _root = RegisterNewPanel(PanelId, enableInput: true);
        _root.Add(SContainer.Anchor(AnchorType.Fill).Background(C(UiTheme.Dim), EBackground.None));

        _window = SContainer.Anchor(AnchorType.MiddleCenter).Pivot(0.5f, 0.5f).Size(WinW, WinH)
            .Background(C(UiTheme.Window), EBackground.Round10);
        _root.Add(_window);

        _window.Add(Label(UiText.T("ui.inventory.title", "Inventory & Character"), 30, UiTheme.Gold, TextAlignmentOptions.MidlineLeft)
            .TopLeft(28, 14).Size(700, 44));
        _window.Add(Label(UiText.T("ui.inventory.hint_actions", "Click: equip/unequip. Right-click: menu. Drag to move."), 17, UiTheme.TextDim,
                TextAlignmentOptions.MidlineRight)
            .TopLeft(WinW - 64 - 28 - 700, 14).Size(700, 44));
        _window.Add(SBgButton.Text("X").FontSize(24).FontColor(UiTheme.Text).Color(C("#2B333E"), C("#4A3036")).Notify(Close)
            .TopLeft(WinW - 64, 16).Size(40, 40));

        BuildSheet();
        BuildEquipment();
        BuildBag();
        BuildTooltip();
        BuildMenu();
        BuildDragGhost();

        Character.Inventory.Changed += MarkDirty;
        Character.Stats.Changed += MarkDirty;

        _root.Active(false);
    }

    private SContainerOptions PanelBox(float x, float w, string title)
    {
        var panel = SContainer.TopLeft(x, BodyTop).Size(w, PanelH).Background(C(UiTheme.Panel), EBackground.Round10);
        _window.Add(panel);
        panel.Add(Label(title, 20, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).UpperCase().TopLeft(18, 10).Size(w - 36, 30));
        return panel;
    }

    private void BuildSheet()
    {
        _sheetPanel = PanelBox(SheetX, SheetW, UiText.T("ui.sheet.title", "Character"));
        var p = _sheetPanel;

        _levelLabel = Label("", 30, UiTheme.Text, TextAlignmentOptions.MidlineRight).TopLeft(16, 8).Size(SheetW - 32, 34);
        p.Add(_levelLabel);

        var bar = SContainer.TopLeft(16, 54).Size(SheetW - 32, 14).Background(C(UiTheme.BarBack), EBackground.None);
        p.Add(bar);
        _xpFill = SContainer.Background(C(UiTheme.Gold), EBackground.None);
        bar.Add(_xpFill);
        SetFill(0);

        _xpLabel = Label("", 15, UiTheme.TextDim, TextAlignmentOptions.Center).TopLeft(16, 72).Size(SheetW - 32, 22);
        p.Add(_xpLabel);
    }

    private void SetFill(double fraction)
    {
        var rt = _xpFill.RectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2((float)Math.Clamp(fraction, 0, 1), 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void BuildEquipment()
    {
        var p = PanelBox(EquipX, EquipW, UiText.T("ui.equipment.title", "Equipment"));
        float gx = (EquipW - (3 * EquipCell + 2 * EquipGap)) / 2;
        for (int r = 0; r < EquipLayout.Length; r++)
        for (int c = 0; c < 3; c++)
            AddSlot(p, EquipX, EquipLayout[r][c], gx + c * (EquipCell + EquipGap), 52 + r * (EquipCell + EquipGap), EquipCell, true);

        float setsY = 52 + 4 * (EquipCell + EquipGap) + 6;
        p.Add(Label(UiText.T("ui.sets.title", "Set bonuses"), 17, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).UpperCase()
            .TopLeft(18, setsY).Size(EquipW - 36, 26));
        _setsLabel = Label("", 16, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(18, setsY + 28).Size(EquipW - 36, PanelH - setsY - 40);
        p.Add(_setsLabel);
    }

    private void BuildBag()
    {
        var p = PanelBox(BagX, BagW, UiText.T("ui.bag.title", "Bag"));
        float gx = (BagW - (BagCols * BagCell + (BagCols - 1) * BagGap)) / 2;
        for (int i = 0; i < LootRoller.BagSlots; i++)
            AddSlot(p, BagX, Inventory.Bag(i), gx + i % BagCols * (BagCell + BagGap), 52 + i / BagCols * (BagCell + BagGap), BagCell, false);

        _bagCount = Label("", 17, UiTheme.TextDim, TextAlignmentOptions.MidlineRight).TopLeft(BagW - 18 - 200, 10).Size(200, 30);
        p.Add(_bagCount);
    }

    private void AddSlot(SContainerOptions parent, float parentX, string key, float x, float y, float size, bool equip)
    {
        var btn = SBgButton.Text("").FontColor(UiTheme.Text); // clicks, right-clicks and drags are handled by PollPointer
        btn.TopLeft(x, y).Size(size, size);
        btn.Color(C(UiTheme.SlotFrameEmpty), C(UiTheme.SlotFrameEmpty));
        parent.Add(btn);

        // Dark inner plate under the text; the button's own image is the colored frame.
        var inner = SContainer.Anchor(AnchorType.Fill).Margin(2f).Background(C(UiTheme.SlotInner), EBackground.None);
        btn.Add(inner);
        inner.Root.transform.SetAsFirstSibling();
        inner.Root.GetComponent<Image>().raycastTarget = false;

        var text = btn.TextObject;
        text.enableWordWrapping = true;
        text.enableAutoSizing = true;
        text.fontSizeMin = 8;
        text.fontSizeMax = equip ? 14 : 13;
        text.margin = new Vector4(3, 13, 3, 3);
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;

        var icon = IconView.Create().Anchor(AnchorType.Fill).Margin(3f);
        btn.Add(icon);

        var level = Label("", 12, UiTheme.TextDim, TextAlignmentOptions.TopLeft).Anchor(AnchorType.Fill);
        level.TextObject.margin = new Vector4(5, 3, 3, 3);
        btn.Add(level);
        var amount = Label("", 12, UiTheme.Text, TextAlignmentOptions.BottomRight).Anchor(AnchorType.Fill);
        amount.TextObject.margin = new Vector4(3, 3, 5, 3);
        btn.Add(amount);

        var w = new SlotWidget
        {
            Key = key, IsEquip = equip, X = parentX + x, Y = BodyTop + y, Size = size,
            Button = btn, Icon = icon, Level = level, Amount = amount,
        };
        _slots.Add(w);
        _slotByKey[key] = w;
        _slotByObjectId[btn.Root.GetInstanceID()] = key;
    }

    private void BuildTooltip()
    {
        _tip = SContainer.TopLeft(0, 0).Size(TipW, 10).Background(Color.white, EBackground.None)
            .Vertical(0, "EC").Padding(2).AutoSize("XP");
        var inner = SContainer.Background(C(UiTheme.TooltipInner), EBackground.None).Vertical(4, "EC").Padding(14, 14, 11, 12);
        _tip.Add(inner);

        _tipIcon = IconView.Create().PWidth(72).PHeight(72);
        _tipTitle = Label("", 22, UiTheme.Text, TextAlignmentOptions.MidlineLeft, wrap: true).PWidth(10).FlexWidth(1);
        var header = SContainer.Horizontal(10, "CC");
        header.Add(_tipIcon).Add(_tipTitle);
        _tipSub = Label("", 16, UiTheme.TextDim, TextAlignmentOptions.TopLeft, wrap: true);
        _tipBody = Label("", 17, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true);
        _tipDivider = SContainer.Background(C(UiTheme.Divider), EBackground.None).PHeight(1);
        _tipCompare = Label("", 17, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true);
        inner.Add(header).Add(_tipSub).Add(_tipBody).Add(_tipDivider).Add(_tipCompare);

        foreach (var img in _tip.Root.GetComponentsInChildren<Image>(true)) img.raycastTarget = false;
        _root.Add(_tip);
        _tip.Active(false);
    }

    private static SLabelOptions Label(string text, int size, string colorHex, TextAlignmentOptions align, bool wrap = false)
    {
        var l = SLabel.Text(text).FontSize(size).FontColor(colorHex).Alignment(align).Wrap(wrap);
        l.TextObject.raycastTarget = false;
        l.TextObject.richText = true;
        return l;
    }

    // ---------------------------------------------------------------- show / hide

    public void Toggle()
    {
        if (Time.frameCount == _lastToggleFrame) return;
        _lastToggleFrame = Time.frameCount;
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        _lastToggleFrame = Time.frameCount;
        _root.Active(true);
        _dirty = false;
        RefreshAll();
    }

    public void Close()
    {
        HideTooltip();
        CloseMenu();
        EndDrag();
        _hoverKey = null;
        _root.Active(false);
    }

    private void MarkDirty() => _dirty = true;

    /// <summary>Per-frame update while the window is open: refresh after changes and drive the hover tooltip.</summary>
    public void Tick()
    {
        if (!IsOpen) return;
        _sheetTimer += Time.unscaledDeltaTime;
        if (_dirty || _sheetTimer > 0.5f)
        {
            _dirty = false;
            _sheetTimer = 0;
            RefreshAll();
        }
        PollPointer();
        UpdateHover();
    }

    // ---------------------------------------------------------------- interaction

    /// <summary>What a click does: bag item -> equip, equipped item -> back into the first free bag slot.</summary>
    public bool ClickSlot(string key)
    {
        var inv = Character.Inventory;
        var db = ModContent.Db;
        if (inv.Get(key) == null) return false;

        bool ok;
        if (key.StartsWith("bag.", StringComparison.Ordinal))
            ok = inv.Equip(key, db);
        else
        {
            var free = FirstFreeBagSlot(inv);
            ok = free != null && inv.Move(key, free, db);
        }
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }

    public static string? FirstFreeBagSlot(Inventory inv)
    {
        for (int i = 0; i < LootRoller.BagSlots; i++)
            if (inv.Get(Inventory.Bag(i)) == null) return Inventory.Bag(i);
        return null;
    }

    /// <summary>For automated tests: act as if the pointer rests on this slot (null = on no slot). Cleared with <see cref="ReleaseSimulatedHover"/>.</summary>
    public void SimulateHover(string? key)
    {
        _useSimulatedHover = true;
        _simulatedHover = key;
        UpdateHover();
    }

    public void ReleaseSimulatedHover()
    {
        _useSimulatedHover = false;
        _simulatedHover = null;
    }

    private void UpdateHover()
    {
        if (MenuVisible || Dragging)
        {
            if (_hoverKey != null) { _hoverKey = null; HideTooltip(); }
            return;
        }
        string? hovered = _useSimulatedHover ? _simulatedHover : RaycastSlot();
        if (hovered == _hoverKey) return;
        _hoverKey = hovered;
        if (hovered == null) HideTooltip(); else ShowTooltip(hovered);
    }

    private string? RaycastSlot() => RaycastSlotAt(Input.mousePosition);

    /// <summary>The slot under a screen point (pixels, origin bottom-left), found the way a real pointer would: a UI raycast.</summary>
    public string? RaycastSlotAt(Vector2 screenPoint)
    {
        if (EventSystem.current == null || SUIRaycaster == null) return null;
        var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        SUIRaycaster.Raycast(new PointerEventData(EventSystem.current) { position = screenPoint }, hits);
        foreach (var hit in hits)
        {
            var t = hit.gameObject.transform;
            while (t != null)
            {
                if (_slotByObjectId.TryGetValue(t.gameObject.GetInstanceID(), out var key)) return key;
                t = t.parent;
            }
        }
        return null;
    }

    /// <summary>Screen point (pixels, origin bottom-left) at the center of a slot, for hit-testing.</summary>
    public Vector2 ScreenPointOf(string slotKey)
    {
        var w = _slotByKey[slotKey];
        var canvas = SUIViewport.GetComponent<RectTransform>().rect;
        float cx = (canvas.width - WinW) / 2 + w.X + w.Size / 2;
        float cy = (canvas.height - WinH) / 2 + w.Y + w.Size / 2;
        float scale = SUIViewport.scaleFactor;
        return new Vector2(cx * scale, (canvas.height - cy) * scale);
    }

    // ---------------------------------------------------------------- refresh

    public void RefreshAll()
    {
        foreach (var w in _slots) RefreshSlot(w);
        RefreshSheet();
        RefreshSets();
        int used = Character.Inventory.Slots.Count(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal));
        _bagCount.Text($"{used} / {LootRoller.BagSlots}");
        if (_hoverKey != null) ShowTooltip(_hoverKey);
    }

    private void RefreshSlot(SlotWidget w)
    {
        var item = Character.Inventory.Get(w.Key);
        if (w.Rendered && Equals(item, w.Shown)) return;
        w.Rendered = true;
        w.Shown = item;

        if (item == null)
        {
            w.Button.Color(C(UiTheme.SlotFrameEmpty), C("#3C4756"));
            w.Button.Text(w.IsEquip ? UiText.SlotName(w.Key) : "").FontColor(UiTheme.TextFaint);
            w.Icon.Active(false);
            w.Level.Text("");
            w.Amount.Text("");
            return;
        }

        var rarity = C(UiTheme.RarityHex(item.Rarity));
        w.Button.Color(rarity, Color.Lerp(rarity, Color.white, 0.4f));
        w.Button.Text(IconView.Show(w.Icon, item.BaseId) ? "" : UiText.ItemName(item)).FontColor(UiTheme.Text);
        bool leveled = ModContent.Db.Items.TryGetValue(item.BaseId, out var def) && !LootRoller.IsMaterialOrOther(def);
        w.Level.Text(leveled ? item.Level.ToString() : "");
        w.Amount.Text(item.Amount > 1 ? "×" + item.Amount : "");
    }

    private void RefreshSheet()
    {
        var pc = Character;
        var sum = SheetData.Summary(pc);
        _levelLabel.Text($"{UiText.T("ui.sheet.level", "Level")} {sum.Level}");
        SetFill(sum.Fraction);
        var inv = CultureInfo.InvariantCulture;
        UpdatePerkLabel(sum);
        _xpLabel.Text($"{Math.Floor(sum.Xp).ToString("N0", inv)} / {sum.Goal.ToString("N0", inv)} XP  ({(sum.Fraction * 100).ToString("0", inv)}%)");

        _sheetData = SheetData.Build(pc);
        float y = 104;
        foreach (var row in _sheetData)
        {
            if (!_sheetRows.TryGetValue(row.Id, out var widgets))
            {
                float h = row.Header ? 30 : 25;
                var name = Label("", row.Header ? 16 : 18, row.Header ? UiTheme.Gold : UiTheme.TextDim, TextAlignmentOptions.BottomLeft)
                    .TopLeft(18, y).Size(SheetW - 36, h);
                var value = Label("", 18, UiTheme.Text, TextAlignmentOptions.BottomRight).TopLeft(18, y).Size(SheetW - 36, h);
                if (row.Header) name.UpperCase();
                _sheetPanel.Add(name);
                _sheetPanel.Add(value);
                _sheetRows[row.Id] = widgets = (name, value);
                AddRowHit(row, y, h);
            }
            widgets.Name.Text(row.Label);
            widgets.Value.Text(row.Value);
            y += row.Header ? 30 : 25;
        }
    }

    private void RefreshSets()
    {
        var pc = Character;
        var counts = ItemEffects.SetCounts(pc.Inventory.Equipped.Select(kv => kv.Value), pc.Content);
        var sb = new StringBuilder();
        foreach (var (setId, have) in counts)
        {
            if (!pc.Content.Sets.TryGetValue(setId, out var set)) continue;
            int total = pc.Content.Items.Values.Count(i => i.Set == setId);
            sb.Append($"<color={UiTheme.Gold}><b>{ModContent.Text.Get(setId + ".name", set.Name)}</b> ({have}/{total})</color>\n");
            foreach (var bonus in set.Bonuses.OrderBy(b => b.Pieces))
            {
                var text = ModContent.Text.Get($"{setId}.bonus.{bonus.Pieces}", "");
                if (text.Length == 0) continue;
                sb.Append($"<color={(have >= bonus.Pieces ? UiTheme.Good : UiTheme.TextFaint)}>({bonus.Pieces}) {text}</color>\n");
            }
            sb.Append('\n');
        }
        _setsLabel.Text(sb.Length == 0 ? $"<color={UiTheme.TextFaint}>{UiText.T("ui.sets.none", "No set pieces equipped.")}</color>" : sb.ToString().TrimEnd());
    }

    // ---------------------------------------------------------------- tooltip

    public void ShowTooltip(string slotKey)
    {
        if (slotKey.StartsWith(RowPrefix, StringComparison.Ordinal))
        {
            ShowRowTooltip(slotKey);
            return;
        }
        var item = Character.Inventory.Get(slotKey);
        if (item == null || !_slotByKey.TryGetValue(slotKey, out var w))
        {
            HideTooltip();
            return;
        }

        var d = ItemDescriber.Describe(item, Character, slotKey);
        TooltipShown = d;
        TooltipSlot = slotKey;

        _tip.Root.GetComponent<Image>().color = C(d.RarityHex);
        _tipTitle.RichText(d.Title);
        IconView.Show(_tipIcon, item.BaseId);
        _tipSub.RichText(d.Subtitle);
        _tipSub.Active(true);
        RowTipShown = null;
        _tipBody.RichText(d.Body);
        _tipBody.Active(d.Body.Length > 0);
        _tipCompare.RichText(d.Compare ?? "");
        _tipCompare.Active(d.Compare != null);
        _tipDivider.Active(d.Compare != null);

        _tip.Active(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tip.RectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tip.RectTransform);

        // Place beside the slot; flip to its left when there is no room, and keep inside the canvas.
        var canvas = SUIViewport.GetComponent<RectTransform>().rect;
        float cw = canvas.width, ch = canvas.height;
        float winLeft = (cw - WinW) / 2, winTop = (ch - WinH) / 2;
        float sx = winLeft + w.X, sy = winTop + w.Y;
        float x = sx + w.Size + 10;
        TooltipOnLeft = x + TipW > cw - 8;
        if (TooltipOnLeft) x = sx - TipW - 10;
        x = Mathf.Max(8, x);
        float h = _tip.RectTransform.rect.height;
        float y = Mathf.Max(8, Mathf.Min(sy, ch - 8 - h));
        _tip.RectTransform.anchoredPosition = new Vector2(x, -y);
        TooltipBounds = new Rect(x, y, TipW, h);
    }

    public void HideTooltip()
    {
        TooltipShown = null;
        TooltipSlot = null;
        RowTipShown = null;
        if (_tip != null) _tip.Active(false);
    }
}
