using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// The detail card beside the guide book (COS-59): item text from <see cref="ItemDescriber"/> or a stat row's
/// explanation, on the free screen area right of the page. With a selected item it shows action buttons, clicked with
/// the book cursor (screen pixels).
/// </summary>
internal static class BookCard
{
    public const string PanelId = "cots-book-card";
    private const float W = 470;

    private static SPanelOptions _root = null!;
    private static SContainerOptions _card = null!;
    private static SLabelOptions _title = null!, _sub = null!, _body = null!, _compare = null!;
    private static SContainerOptions _divider = null!, _actions = null!;
    private static SBgButtonOptions _equip = null!, _drop = null!, _destroy = null!, _dropOne = null!, _split = null!;
    private static SContainerOptions _stackActions = null!;
    private static Image? _icon;
    private static bool _built, _destroyArmed;
    private static string? _shownKey;

    public static string? ShownKey => _shownKey;
    public static bool ActionsVisible => _built && !_hidden && _actions.Root.activeSelf;
    public static string Text => _built ? _title.TextObject.text + "\n" + _body.TextObject.text : "";

    public static void Build()
    {
        if (_built) return;
        _built = true;
        _root = RegisterNewPanel(PanelId, enableInput: false);
        _card = SContainer.TopLeft(0, 0).Size(W, 10).Background(PageKit.Hex("#5A4636"), EBackground.None).Vertical(0, "EC").Padding(3).AutoSize("XP");
        var inner = SContainer.Background(PageKit.Hex("#14120FF2"), EBackground.None).Vertical(5, "EC").Padding(16, 16, 12, 14);
        _card.Add(inner);
        _title = Label(22).PWidth(10).FlexWidth(1);
        var header = SContainer.Horizontal(10, "CC");
        header.Add(_title);
        _icon = BuildIcon(header.RectTransform);
        _sub = Label(16);
        _body = Label(17);
        _divider = SContainer.Background(PageKit.Hex("#5A4636"), EBackground.None).PHeight(1);
        _compare = Label(17);
        _actions = SContainer.Horizontal(8, "CC").PHeight(44);
        _equip = Button();
        _drop = Button();
        _destroy = Button();
        _actions.Add(_equip).Add(_drop).Add(_destroy);
        // A stack's own row (COS-96): Drop 1 and Split stack.
        _stackActions = SContainer.Horizontal(8, "CC").PHeight(44);
        _dropOne = Button();
        _split = Button();
        _stackActions.Add(_dropOne).Add(_split);
        inner.Add(header).Add(_sub).Add(_body).Add(_divider).Add(_compare).Add(_actions).Add(_stackActions);
        var layer = SContainer.Anchor(AnchorType.Fill);
        _root.Add(layer);
        layer.Add(_card);
        foreach (var img in _root.Root.GetComponentsInChildren<Image>(true)) img.raycastTarget = false;
        // The card stays active and hides by moving off screen: toggling it (and its RawImage icon) before its
        // graphics ever initialised threw inside uGUI.
        _hidden = true;
        _card.RectTransform.anchoredPosition = OffScreen;
        _root.Active(true); // panels start inactive; the card hides by moving off screen instead
    }

    public static string Debug => _built ? $"root active {_root.Root.activeInHierarchy}, pos {_card.RectTransform.anchoredPosition}, size {_card.RectTransform.rect.size}" : "not built";

    private static readonly Vector2 OffScreen = new(-5000, 5000);
    private static bool _hidden;

    /// <summary>
    /// The item icon left of the title: a plain uGUI Image on a GameObject of our own (a SUI RawImage here threw
    /// inside uGUI). It stays active and shows a clear color when there is no sprite.
    /// </summary>
    private static Image BuildIcon(RectTransform header)
    {
        var go = new GameObject("cots-card-icon");
        go.transform.SetParent(header, false);
        go.transform.SetAsFirstSibling();
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = IconSize;
        le.minHeight = le.preferredHeight = IconSize;
        le.flexibleWidth = 0;
        var img = go.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = Color.clear;
        return img;
    }

    private const float IconSize = 64;

    private static void SetIcon(Sprite? sprite)
    {
        if (_icon == null) return;
        _icon.sprite = sprite;
        _icon.color = sprite != null ? Color.white : Color.clear;
        var le = _icon.GetComponent<LayoutElement>();
        if (le != null) le.minWidth = le.preferredWidth = sprite != null ? IconSize : 0;
    }

    /// <summary>True when the card shows an item icon (self-test).</summary>
    public static bool IconShown => _built && !_hidden && _icon != null && _icon.sprite != null && _icon.color.a > 0;

    private static SLabelOptions Label(int size)
    {
        var l = SLabel.Text("").FontSize(size).FontColor(UiTheme.Text).Alignment(TextAlignmentOptions.TopLeft).Wrap(true);
        l.TextObject.raycastTarget = false;
        l.TextObject.richText = true;
        return l;
    }

    private static SBgButtonOptions Button() =>
        SBgButton.Text("").FontSize(18).FontColor("#EFE7D6").Color(PageKit.Hex("#5A4636"), PageKit.Hex("#7A5E48")).PHeight(42).FlexWidth(1);

    private static void On(GameObject go, bool on) { if (go.activeSelf != on) go.SetActive(on); }

    public static void Hide()
    {
        _shownKey = null;
        _destroyArmed = false;
        if (_built && !_hidden) { _hidden = true; _card.RectTransform.anchoredPosition = OffScreen; }
    }

    /// <summary>Keys the card shows as text: a perk, a spell row or an attribute row (not their buttons).</summary>
    public static bool ShowsText(string key) =>
        (key.StartsWith("perk.", StringComparison.Ordinal) && ModContent.Db.Perks.ContainsKey(key))
        || (key.StartsWith("spell.", StringComparison.Ordinal) && ModContent.Db.Spells.ContainsKey(key))
        || (key.StartsWith("attr.", StringComparison.Ordinal) && Core.Rules.AttributePoints.Ids.Contains(key.Substring(5)));

    /// <summary>Shows an item (slot key) or a stat row ("row.id"). <paramref name="pinned"/>: the selected item, with actions.</summary>
    public static void Show(string key, bool pinned)
    {
        try
        {
            ShowInner(key, pinned);
        }
        catch (Exception e)
        {
            if (!_warned) RedLoader.RLog.Error($"Champions of the Sons book card: {e}");
            _warned = true;
        }
    }

    private static bool _warned;

    private static void ShowInner(string key, bool pinned)
    {
        Build();
        if (key != _shownKey) _destroyArmed = false;
        _shownKey = key;
        var pc = PlayerState.Character;
        if (ShowsText(key))
        {
            // COS-168: a perk, spell or attribute on its page: title and text only.
            var bad = UiTheme.Bad;
            var faint = UiTheme.TextFaint;
            var (title, body) = key.StartsWith("attr.", StringComparison.Ordinal) ? Perks.PerkInfo.Attribute(key.Substring(5), faint)
                : key.StartsWith("spell.", StringComparison.Ordinal) ? Perks.PerkInfo.Spell(key, bad, faint)
                : Perks.PerkInfo.Perk(key, bad, faint);
            if (title.Length == 0) { Hide(); return; }
            _title.Text(title);
            SetIcon(key.StartsWith("attr.", StringComparison.Ordinal) ? null : Icons.IconCache.GetSprite(key));
            On(_sub.Root, false);
            _body.Text(body);
            On(_divider.Root, false);
            On(_compare.Root, false);
            On(_actions.Root, false);
            On(_stackActions.Root, false);
        }
        else if (key.StartsWith("row.", StringComparison.Ordinal))
        {
            var row = SheetData.Build(pc).FirstOrDefault(r => r.Id == key.Substring(4));
            if (row.Id == null) { Hide(); return; }
            _title.Text($"<b>{row.Label}</b>  <color={UiTheme.Gold}>{row.Value}</color>");
            SetIcon(null);
            On(_sub.Root, false);
            _body.Text(row.Tip);
            On(_divider.Root, false);
            On(_compare.Root, false);
            On(_actions.Root, false);
            On(_stackActions.Root, false);
        }
        else
        {
            var item = pc.Inventory.Get(key);
            if (item == null) { Hide(); return; }
            var d = ItemDescriber.Describe(item, pc, key);
            _title.Text(d.Title);
            SetIcon(Icons.IconCache.GetSprite(item.BaseId));
            On(_sub.Root, true);
            _sub.Text(d.Subtitle);
            _body.Text(d.Body);
            bool cmp = !string.IsNullOrEmpty(d.Compare);
            On(_divider.Root, cmp);
            On(_compare.Root, cmp);
            if (cmp) _compare.Text(d.Compare!);
            On(_actions.Root, pinned);
            bool stack = pinned && InventoryActions.IsStack(key);
            On(_stackActions.Root, stack);
            if (stack)
            {
                _dropOne.Text(UiText.T("ui.inventory.menu.drop_one", "Drop 1"));
                _split.Text(UiText.T("ui.inventory.menu.split", "Split stack"));
            }
            if (pinned)
            {
                bool equipped = key.StartsWith("equip.", StringComparison.Ordinal);
                _equip.Text(InventoryActions.IsUsable(key)
                    ? UiText.T("ui.catalog.use", "Use")
                    : UiText.T(equipped ? "ui.inventory.menu.unequip" : "ui.inventory.menu.equip", equipped ? "Unequip" : "Equip"));
                _drop.Text(InventoryActions.DropLabel(key));
                _destroy.Text(_destroyArmed ? UiText.T("ui.book.destroy_confirm", "Again to destroy") : UiText.T("ui.inventory.menu.destroy", "Destroy"));
            }
        }
        _hidden = false;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_card.RectTransform);
        Place();
    }

    /// <summary>Right of the page on screen, top-aligned; flips left of the page when there is no room.</summary>
    private static void Place()
    {
        var page = BookHost.PageScreenRect();
        float scale = Mathf.Max(0.01f, SUIViewport.scaleFactor);
        var canvas = SUIViewport.GetComponent<RectTransform>().rect;
        float h = _card.RectTransform.rect.height;
        float x = page.xMax / scale + 30;
        if (x + W > canvas.width - 10) x = page.xMin / scale - 30 - W;
        float yTop = canvas.height - page.yMax / scale;
        yTop = Mathf.Clamp(yTop, 10, Mathf.Max(10, canvas.height - h - 10));
        _card.RectTransform.anchoredPosition = new Vector2(Mathf.Max(10, x), -yTop);
    }

    /// <summary>Which action button is under a screen point: "equip", "drop", "destroy" or null.</summary>
    public static string? ActionAt(Vector2 screen)
    {
        if (!ActionsVisible) return null;
        if (Contains(_equip, screen)) return "equip";
        if (Contains(_drop, screen)) return "drop";
        if (Contains(_destroy, screen)) return "destroy";
        if (_stackActions.Root.activeSelf && Contains(_dropOne, screen)) return "drop_one";
        if (_stackActions.Root.activeSelf && Contains(_split, screen)) return "split";
        return null;
    }

    private static bool Contains(SBgButtonOptions b, Vector2 screen)
    {
        var corners = new Vector3[4];
        b.RectTransform.GetWorldCorners(corners);
        return screen.x >= corners[0].x && screen.x <= corners[2].x && screen.y >= corners[0].y && screen.y <= corners[2].y;
    }

    /// <summary>Runs a card action on the selected slot. Destroy needs two clicks.</summary>
    public static string Act(string action, string key)
    {
        switch (action)
        {
            case "equip":
                if (InventoryActions.IsUsable(key))
                {
                    bool more = InventoryActions.Use(key) && PlayerState.Character.Inventory.Get(key) != null;
                    if (!more) BookPages.Selected = null;
                    return "used";
                }
                BookPages.Selected = null;
                return InventoryActions.ToggleEquip(key) ? "equipped" : "no-room";
            case "drop":
                BookPages.Selected = null;
                return InventoryActions.Drop(key) ? "dropped" : "failed";
            case "drop_one":
                bool left = InventoryActions.DropOne(key) && PlayerState.Character.Inventory.Get(key) != null;
                if (!left) BookPages.Selected = null;
                return "dropped one";
            case "split":
                return InventoryActions.Split(key) ? "split" : "no-room";
            case "destroy":
                if (!_destroyArmed) { _destroyArmed = true; return "armed"; }
                _destroyArmed = false;
                BookPages.Selected = null;
                return InventoryActions.Destroy(key) ? "destroyed" : "failed";
        }
        return "none";
    }
}
