using RedLoader;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// Equipment and character pages on the game's guide book (COS-59). The book stays the game's; we add three tabs
/// (Gear, Bag, Character), draw their pages, handle clicks with the book cursor and show a detail card beside it.
/// </summary>
public sealed class BookFeature : IModFeature
{
    internal static string LastAction { get; private set; } = "";

    public void Init()
    {
        // The card is built the first time it shows (in the world): built at the main menu, its graphics threw.
        BookHost.TabShown += BookPages.Show;
    }

    public void Update(float dt, bool inWorld)
    {
        try
        {
            Step();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons book: {e}");
            BookHost.ShowGamePage();
        }
    }

    private static void Step()
    {
        BookHost.Tick();
        if (!BookHost.IsOpen)
        {
            BookCard.Hide();
            BookPages.Selected = null;
            if (BookMerge.Active) BookMerge.Cancel();
            CancelDrag();
            return;
        }
        Handle(BookInput.FromDevices(Mouse.current, Gamepad.current, BookHost.CursorScreen));
    }

    // ------------------------------------------------------------------ input

    /// <summary>Screen pixels the cursor must move with the button held before a press becomes a drag.</summary>
    internal const float DragThreshold = 12;

    private static string? _pressKey;
    private static Vector2 _pressAt;
    private static bool _pressed;

    /// <summary>The slot being dragged (null when no drag is under way).</summary>
    internal static string? Dragging { get; private set; }

    private static void CancelDrag()
    {
        _pressed = false;
        _pressKey = null;
        Dragging = null;
        BookPages.SetDragGhost(null, default);
    }

    /// <summary>
    /// One frame of book input, from the mouse or the pad (<see cref="BookInput.FromDevices"/>) or from a self-test.
    /// Press and release on the same slot is a click (select, or equip into the slot with a bag item selected);
    /// press, move and release on another slot drags the item there. Alt (right-click, pad West) equips or unequips.
    /// </summary>
    internal static void Handle(BookInput input)
    {
        var screen = input.Screen;
        BookTabs.Tick(screen);
        // Our tab works from any page, the game's too.
        if (input.Press)
        {
            var tab = BookTabs.HitTest(screen);
            if (tab != BookTab.None) { CancelDrag(); BookHost.ShowModPage(BookPages.LastTab); LastAction = "tab " + BookPages.LastTab; return; }
        }
        if (!BookHost.ModPageActive) { CancelDrag(); BookCard.Hide(); return; }
        var current = BookHost.Tab;

        if (input.Page != 0) BookPages.Page(current, input.Page);

        string? key = BookHost.CursorOnPage(screen, out var px) ? BookPages.HitAt(px) : null;
        if (input.Press)
        {
            var action = BookCard.ActionAt(screen);
            if (action != null && BookPages.Selected != null) LastAction = action + ": " + BookCard.Act(action, BookPages.Selected);
            else { _pressed = true; _pressKey = key; _pressAt = screen; }
        }
        if (_pressed && input.Held && Dragging == null && !BookMerge.Active && _pressKey != null && IsItemSlot(_pressKey)
            && PlayerState.Character.Inventory.Get(_pressKey) != null && (screen - _pressAt).magnitude >= DragThreshold)
            Dragging = _pressKey;
        if (Dragging != null) BookPages.SetDragGhost(PlayerState.Character.Inventory.Get(Dragging), px);
        if (input.Release && _pressed)
        {
            if (Dragging != null) LastAction = Dragging + " -> " + (key ?? "nothing") + ": " + BookPages.Drop(Dragging, key);
            else if (key != null && key == _pressKey) LastAction = key + ": " + BookPages.Click(current, key, false);
            CancelDrag();
        }
        else if (input.Alt && key != null) LastAction = key + ": " + BookPages.Click(current, key, true);

        BookPages.Tick(current);
        UpdateCard(Dragging != null ? null : key);
    }

    private static bool IsItemSlot(string key) => key.StartsWith("bag.", StringComparison.Ordinal) || key.StartsWith("equip.", StringComparison.Ordinal);

    private static void UpdateCard(string? hover)
    {
        var inv = PlayerState.Character.Inventory;
        bool hoverShows = hover != null && (hover.StartsWith("row.", StringComparison.Ordinal) || inv.Get(hover) != null);
        if (hoverShows && hover != BookPages.Selected) BookCard.Show(hover!, pinned: false);
        else if (BookPages.Selected != null && inv.Get(BookPages.Selected) != null) BookCard.Show(BookPages.Selected, pinned: true);
        else BookCard.Hide();
    }

    public void RegisterSelfTests() => BookSelfTest.Register();
}

/// <summary>
/// One frame of book input. Mouse: left press/hold/release, right press, wheel pages. Pad (the game moves the book
/// cursor with the stick): South = left button, West = right-click, shoulders page the bag or character pages.
/// </summary>
internal readonly record struct BookInput(Vector2 Screen, bool Press, bool Held, bool Release, bool Alt, int Page)
{
    public static BookInput FromDevices(Mouse? mouse, Gamepad? pad, Vector2 screen)
    {
        float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0;
        return From(screen,
            mouse != null && mouse.leftButton.wasPressedThisFrame, mouse != null && mouse.leftButton.isPressed, mouse != null && mouse.leftButton.wasReleasedThisFrame,
            mouse != null && mouse.rightButton.wasPressedThisFrame, wheel,
            pad != null && pad.buttonSouth.wasPressedThisFrame, pad != null && pad.buttonSouth.isPressed, pad != null && pad.buttonSouth.wasReleasedThisFrame,
            pad != null && pad.buttonWest.wasPressedThisFrame, pad != null && pad.leftShoulder.wasPressedThisFrame, pad != null && pad.rightShoulder.wasPressedThisFrame);
    }

    /// <summary>The mapping from raw buttons, so self-tests can drive the pad path without a pad.</summary>
    public static BookInput From(Vector2 screen, bool leftDown, bool leftHeld, bool leftUp, bool rightDown, float wheel,
        bool southDown, bool southHeld, bool southUp, bool westDown, bool lb, bool rb)
    {
        int page = wheel > 0.1f ? -1 : wheel < -0.1f ? 1 : 0;
        if (lb) page = -1;
        if (rb) page = 1;
        return new BookInput(screen, leftDown || southDown, leftHeld || southHeld, leftUp || southUp, rightDown || westDown, page);
    }

    public static BookInput Pad(Vector2 screen, bool southDown = false, bool southHeld = false, bool southUp = false, bool west = false, bool lb = false, bool rb = false) =>
        From(screen, false, false, false, false, 0, southDown, southHeld, southUp, west, lb, rb);

    public static BookInput MouseAt(Vector2 screen, bool down = false, bool held = false, bool up = false, bool right = false) =>
        From(screen, down, held, up, right, 0, false, false, false, false, false, false);
}
