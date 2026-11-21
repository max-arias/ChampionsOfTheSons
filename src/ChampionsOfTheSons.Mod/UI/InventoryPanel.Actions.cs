using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Item handling in the inventory window (COS-31): drag and drop, right-click menu, drop and destroy.</summary>
internal sealed partial class InventoryPanel
{
    private const float MenuW = 230, MenuRowH = 38, MenuPad = 4;
    /// <summary>Four rows, six for a stack (Drop 1 and Split, COS-96).</summary>
    private float MenuH => MenuRowH * (_menuStack ? 6 : 4) + MenuPad * 2;
    private bool _menuStack;
    private const float DragThreshold = 8;

    private SContainerOptions _menu = null!;
    private SBgButtonOptions _menuEquip = null!, _menuDrop = null!, _menuDestroy = null!, _menuMerge = null!, _menuDropOne = null!, _menuSplit = null!;
    private SContainerOptions _ghost = null!;
    private SLabelOptions _ghostLabel = null!;
    private Vector2 _menuPos;
    private string? _pressKey;
    private Vector2 _pressPoint;
    private bool _destroyArmed;

    /// <summary>The slot the right-click menu is open for, or null.</summary>
    public string? MenuSlot { get; private set; }

    public bool MenuVisible => MenuSlot != null;

    /// <summary>The right-click menu object while it is open (gamepad focus is limited to it), else null.</summary>
    public GameObject? PadMenuRoot => MenuVisible ? _menu.Root : null;

    /// <summary>The menu's rectangle in canvas units (origin top-left); empty when closed.</summary>
    public Rect MenuBounds => MenuVisible ? new Rect(_menuPos.x, _menuPos.y, MenuW, MenuH) : default;

    public bool Dragging { get; private set; }

    public string MenuEquipText => _menuEquip.TextObject.text;

    public string MenuDestroyText => _menuDestroy.TextObject.text;

    public string MenuMergeText => _menuMerge.TextObject.text;

    public string MenuDropText => _menuDrop.TextObject.text;

    /// <summary>Whether the stack rows (Drop 1, Split) show.</summary>
    public bool MenuStackRows => MenuVisible && _menuStack;

    private void BuildMenu()
    {
        _menu = SContainer.TopLeft(0, 0).Size(MenuW, MenuH).Background(C(UiTheme.Divider), EBackground.None);
        var inner = SContainer.Anchor(AnchorType.Fill).Margin(2f).Background(C(UiTheme.TooltipInner), EBackground.None);
        _menu.Add(inner);
        SBgButtonOptions Row(int i, Action onClick)
        {
            var b = SBgButton.Text("").FontSize(19).FontColor(UiTheme.Text).Color(C(UiTheme.Panel), C("#34404E")).Notify(onClick);
            b.TopLeft(MenuPad, MenuPad + i * MenuRowH).Size(MenuW - MenuPad * 2, MenuRowH - 2);
            _menu.Add(b);
            return b;
        }
        _menuEquip = Row(0, () => MenuEquipClicked());
        _menuDrop = Row(1, () => MenuDropClicked());
        _menuDestroy = Row(2, () => MenuDestroyClicked());
        _menuMerge = Row(3, () => MenuMergeClicked());
        _menuDropOne = Row(4, () => MenuDropOneClicked());
        _menuSplit = Row(5, () => MenuSplitClicked());
        _root.Add(_menu);
        _menu.Active(false);
    }

    private void BuildDragGhost()
    {
        _ghost = SContainer.TopLeft(0, 0).Size(190, 34).Background(Color.white, EBackground.None);
        var inner = SContainer.Anchor(AnchorType.Fill).Margin(2f).Background(C(UiTheme.TooltipInner), EBackground.None);
        _ghost.Add(inner);
        _ghostLabel = Label("", 16, UiTheme.Text, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
        _ghost.Add(_ghostLabel);
        foreach (var img in _ghost.Root.GetComponentsInChildren<Image>(true)) img.raycastTarget = false;
        _root.Add(_ghost);
        _ghost.Active(false);
    }

    /// <summary>Screen pixels (origin bottom-left) to canvas units (origin top-left).</summary>
    private Vector2 ScreenToCanvas(Vector2 screen)
    {
        float scale = SUIViewport.scaleFactor;
        var canvas = SUIViewport.GetComponent<RectTransform>().rect;
        return new Vector2(screen.x / scale, canvas.height - screen.y / scale);
    }

    /// <summary>Left button: press on an item, drag past a small threshold to move it, release on a slot to drop it there;
    /// a release without a drag on the same slot is a click (equip/unequip). Right button opens the item menu.</summary>
    private void PollPointer()
    {
        var mouse = Pad.PadPointer.Position;
        var canvasPos = ScreenToCanvas(mouse);
        bool overMenu = MenuVisible && MenuBounds.Contains(canvasPos);

        if (Pad.PadPointer.ButtonDown(0))
        {
            if (MenuVisible && !overMenu) CloseMenu();
            _pressKey = null;
            if (!overMenu)
            {
                var key = RaycastSlotAt(mouse);
                if (key != null && Character.Inventory.Get(key) != null) { _pressKey = key; _pressPoint = mouse; }
            }
        }
        else if (Pad.PadPointer.Button(0) && _pressKey != null)
        {
            if (!Dragging && Vector2.Distance(mouse, _pressPoint) > DragThreshold) BeginDrag(_pressKey);
            if (Dragging) MoveGhost(canvasPos);
        }
        else if (Pad.PadPointer.ButtonUp(0) && _pressKey != null)
        {
            var from = _pressKey;
            bool wasDragging = Dragging;
            var over = RaycastSlotAt(mouse);
            EndDrag();
            if (wasDragging) { if (over != null) DragDrop(from, over); }
            else if (over == from) ClickSlot(from);
        }

        if (Pad.PadPointer.ButtonDown(1))
        {
            EndDrag();
            var key = overMenu ? null : RaycastSlotAt(mouse);
            if (key != null && Character.Inventory.Get(key) != null) OpenMenu(key, canvasPos);
            else if (!overMenu) CloseMenu();
        }
    }

    // ---- drag and drop

    private void BeginDrag(string key)
    {
        var item = Character.Inventory.Get(key);
        if (item == null) return;
        Dragging = true;
        CloseMenu();
        HideTooltip();
        _ghost.Root.GetComponent<Image>().color = C(UiTheme.RarityHex(item.Rarity));
        _ghostLabel.Text(UiText.ItemName(item));
        _ghost.Active(true);
    }

    private void MoveGhost(Vector2 canvasPos) =>
        _ghost.RectTransform.anchoredPosition = new Vector2(canvasPos.x + 14, -(canvasPos.y + 14));

    private void EndDrag()
    {
        Dragging = false;
        _pressKey = null;
        if (_ghost != null) _ghost.Active(false);
    }

    /// <summary>Moves or swaps the item in <paramref name="from"/> into <paramref name="to"/>. Refuses items that do not fit the target slot.</summary>
    public bool DragDrop(string from, string to)
    {
        bool ok = from != to && Character.Inventory.MoveOrMerge(from, to, ModContent.Db); // equal stacks merge (COS-96)
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }

    // ---- right-click menu

    public void OpenMenu(string key, Vector2 canvasPos)
    {
        if (Character.Inventory.Get(key) == null) return;
        MenuSlot = key;
        _destroyArmed = false;
        bool equipped = key.StartsWith("equip.", StringComparison.Ordinal);
        _menuEquip.Text(InventoryActions.IsUsable(key)
            ? UiText.T("ui.catalog.use", "Use")
            : UiText.T(equipped ? "ui.inventory.menu.unequip" : "ui.inventory.menu.equip", equipped ? "Unequip" : "Equip"));
        _menuDrop.Text(InventoryActions.DropLabel(key));
        _menuDestroy.Text(UiText.T("ui.inventory.menu.destroy", "Destroy")).FontColor(UiTheme.Bad);
        _menuStack = InventoryActions.IsStack(key);
        _menuDropOne.Text(UiText.T("ui.inventory.menu.drop_one", "Drop 1"));
        _menuSplit.Text(UiText.T("ui.inventory.menu.split", "Split stack"));
        _menuDropOne.Root.SetActive(_menuStack);
        _menuSplit.Root.SetActive(_menuStack);
        _menu.RectTransform.sizeDelta = new Vector2(MenuW, MenuH);
        var (have, need) = Book.BookMerge.QuickCount(key);
        _menuMerge.Text(need == 0 ? UiText.T("ui.merge.button", "Merge") : string.Format(UiText.T("ui.merge.menu", "Merge ({0}/{1})"), have, need))
            .FontColor(need > 0 && have >= need ? UiTheme.Text : UiTheme.TextFaint);

        var canvas = CanvasSize;
        _menuPos = new Vector2(Mathf.Clamp(canvasPos.x, 4, canvas.x - MenuW - 4), Mathf.Clamp(canvasPos.y, 4, canvas.y - MenuH - 4));
        _menu.RectTransform.anchoredPosition = new Vector2(_menuPos.x, -_menuPos.y);
        _menu.Active(true);
        HideTooltip();
    }

    /// <summary>Closes the menu; true if one was open.</summary>
    public bool CloseMenu()
    {
        bool was = MenuVisible;
        MenuSlot = null;
        _destroyArmed = false;
        if (_menu != null) _menu.Active(false);
        return was;
    }

    /// <summary>The first row: Use for usable items, else Equip/Unequip.</summary>
    public bool MenuEquipClicked()
    {
        var key = MenuSlot;
        CloseMenu();
        if (key == null) return false;
        if (!InventoryActions.IsUsable(key)) return ClickSlot(key);
        bool ok = InventoryActions.Use(key);
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }

    public bool MenuDropClicked()
    {
        var key = MenuSlot;
        CloseMenu();
        if (key == null) return false;
        bool ok = InventoryActions.Drop(key);
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }

    /// <summary>COS-96: drops one of the stack.</summary>
    public bool MenuDropOneClicked()
    {
        var key = MenuSlot;
        CloseMenu();
        if (key == null) return false;
        bool ok = InventoryActions.DropOne(key);
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }

    /// <summary>COS-96: half the stack to the first free bag slot.</summary>
    public bool MenuSplitClicked()
    {
        var key = MenuSlot;
        CloseMenu();
        if (key == null) return false;
        bool ok = InventoryActions.Split(key);
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }

    /// <summary>COS-144: merges the item with the highest-level others of its kind; the result takes its slot.</summary>
    public bool MenuMergeClicked()
    {
        var key = MenuSlot;
        CloseMenu();
        if (key == null) return false;
        bool ok = Book.BookMerge.QuickMerge(key) is { Ok: true };
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }

    /// <summary>First click arms the button ("Click again to destroy"); the second click deletes the item.</summary>
    public bool MenuDestroyClicked()
    {
        var key = MenuSlot;
        if (key == null) return false;
        if (!_destroyArmed)
        {
            _destroyArmed = true;
            _menuDestroy.Text(UiText.T("ui.inventory.menu.destroy_confirm", "Click again to destroy"));
            return false;
        }
        CloseMenu();
        bool ok = InventoryActions.Destroy(key);
        if (ok) { _dirty = false; RefreshAll(); }
        return ok;
    }
}
