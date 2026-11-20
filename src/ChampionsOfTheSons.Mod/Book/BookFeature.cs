using RedLoader;
using SonsSdk;
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
            return;
        }
        var mouse = Mouse.current;
        var pad = Gamepad.current;
        bool click = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        bool alt = (mouse != null && mouse.rightButton.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame);
        var screen = BookHost.CursorScreen;

        // Our tabs work from any page, the game's too.
        if (click)
        {
            var tab = BookTabs.HitTest(screen);
            if (tab != BookTab.None) { BookHost.ShowModPage(tab); LastAction = "tab " + tab; return; }
        }
        if (!BookHost.ModPageActive) { BookCard.Hide(); return; }
        var current = BookHost.Tab;

        if (Config.InventoryKey.GetAction().WasPressedThisFrame()) { BookHost.Close(); return; }
        int page = 0;
        if (mouse != null) { float wheel = mouse.scroll.ReadValue().y; page = wheel > 0.1f ? -1 : wheel < -0.1f ? 1 : 0; }
        if (pad != null) { if (pad.leftShoulder.wasPressedThisFrame) page = -1; if (pad.rightShoulder.wasPressedThisFrame) page = 1; }
        if (page != 0) BookPages.Page(current, page);

        string? key = BookHost.CursorOnPage(out var px) ? BookPages.HitAt(px) : null;
        if (click)
        {
            var action = BookCard.ActionAt(screen);
            if (action != null && BookPages.Selected != null) LastAction = action + ": " + BookCard.Act(action, BookPages.Selected);
            else if (key != null) LastAction = key + ": " + BookPages.Click(current, key, false);
        }
        else if (alt && key != null) LastAction = key + ": " + BookPages.Click(current, key, true);

        BookPages.Tick(current);
        UpdateCard(key);
    }

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
