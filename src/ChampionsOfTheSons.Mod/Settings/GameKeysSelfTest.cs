using ChampionsOfTheSons.Mod.Book;
using TheForest.Utils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ChampionsOfTheSons.Mod.Settings;

/// <summary>The game's own keys still work with the mod on: real B and I key events, and what the game and the mod did with them.</summary>
public sealed class GameKeysFeature : IModFeature
{
    public void RegisterSelfTests()
    {
        GameKeysSelfTest.Register();
        try
        {
            var h = new HarmonyLib.Harmony("cots.game-keys-probe");
            foreach (var name in new[] { "ToggleBook", "ToggleInventoryCallback" })
            {
                var m = HarmonyLib.AccessTools.Method(typeof(TheForest.Items.Inventory.PlayerInventory), name);
                if (m == null) { RedLoader.RLog.Msg($"[COTS-SELFTEST] game keys: no PlayerInventory.{name}"); continue; }
                h.Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(GameKeysFeature), nameof(Called)));
                RedLoader.RLog.Msg($"[COTS-SELFTEST] game keys: probing PlayerInventory.{name}");
            }
        }
        catch (Exception e)
        {
            RedLoader.RLog.Msg("[COTS-SELFTEST] game keys: probe patch failed " + e.Message);
        }
    }

    private static void Called(System.Reflection.MethodBase __originalMethod) =>
        RedLoader.RLog.Msg($"[COTS-SELFTEST] game keys: game called PlayerInventory.{__originalMethod.Name}");

    public void Update(float dt, bool inWorld) { if (SelfTest.Enabled) GameKeysSelfTest.Poll(); }
}

internal static class GameKeysSelfTest
{
    private static Keyboard? _kb;

    public static void Register()
    {
        SelfTest.Add(2f, "game keys: dump bindings", Dump);
        SelfTest.Add(2.5f, "game keys: press B", () => Send(Key.B, true));
        SelfTest.Add(2.6f, "game keys: release B", () => Send(Key.B, false));
        SelfTest.Add(4.5f, "game keys: B opened the game's book", AfterB);
        // Holding X (0.4 s, GoToTutorials) on the blueprint book switches to the tutorial book; our tab must be on whichever
        // book is up (2026-11-22).
        SelfTest.Add(5f, "game keys: hold the blueprint book", () =>
        {
            LocalPlayer.Inventory?.StashHeldItems(false, false);
            LocalPlayer.Inventory?.TryEquip(BookHost.GuideBookId, false, true, false);
        });
        SelfTest.Add(7f, "game keys: press X", () => { _firstBook = BookHost.Which; Send(Key.X, true); });
        SelfTest.Add(7.7f, "game keys: release X", () => Send(Key.X, false));
        SelfTest.Add(9.5f, "game keys: X switched books", AfterX);
        SelfTest.Add(10f, "game keys: open our page on this book", () => BookHost.ShowModPage(BookTab.Gear));
        SelfTest.Add(10.5f, "game keys: our page shows on this book", () =>
            SelfTest.Check("game keys: our page shows on the second book", BookHost.Tab == BookTab.Gear && BookPatches.Block, State()));
        SelfTest.Add(11f, "game keys: put the book away", () => BookHost.Close());
        SelfTest.Add(12.5f, "game keys: press I", () => Send(Key.I, true));
        SelfTest.Add(12.6f, "game keys: release I", () => Send(Key.I, false));
        SelfTest.Add(14.5f, "game keys: I opened the game's inventory", AfterI);
        // Close it again: an open inventory turns the game's play maps off, which later phases need.
        SelfTest.Add(15f, "game keys: close the inventory", () => LocalPlayer.Inventory?.Close(false));
        SelfTest.Add(17f, "game keys: I closed the game's inventory", () =>
            SelfTest.Check("game keys: the game's inventory closed again", !LocalPlayer.IsInInventory, State()));
    }

    private static void Dump()
    {
        bool pausedBefore = Sons.Gui.PauseMenu.IsActive;
        TestKit.Unpause();
        SelfTest.Log($"game keys: pause menu was {pausedBefore}, now {Sons.Gui.PauseMenu.IsActive}, time scale {UnityEngine.Time.timeScale}");
        _kb = Keyboard.current;
        if (_kb == null)
        {
            try { _kb = InputSystem.AddDevice("Keyboard").TryCast<Keyboard>(); _kb?.MakeCurrent(); } catch (Exception e) { SelfTest.Log("game keys: no keyboard " + e.Message); }
        }
        SelfTest.Log($"game keys: controllable {SonsSdk.GameState.IsPlayerControllable}, guide books {LocalPlayer.Inventory?.AmountOf(BookHost.GuideBookId, false, false)}, tutorial books {LocalPlayer.Inventory?.AmountOf(BookHost.TutorialBookId, false, false)}");
        try
        {
            var asset = Sons.Input.InputSystem.InputMapping?.asset;
            if (asset == null) { SelfTest.Log("game keys: no input asset"); return; }
            foreach (var m in asset.actionMaps)
                foreach (var a in m.actions)
                    foreach (var b in a.bindings)
                    {
                        var p = b.effectivePath ?? "";
                        if (p.Equals("<Keyboard>/b", StringComparison.OrdinalIgnoreCase) || p.Equals("<Keyboard>/i", StringComparison.OrdinalIgnoreCase)
                            || p.Equals("<Keyboard>/x", StringComparison.OrdinalIgnoreCase))
                        {
                            SelfTest.Log($"game keys: {p} -> {m.name}/{a.name} (map on {m.enabled}, action on {a.enabled}, path {b.path}, " +
                                $"interactions '{a.interactions}'/'{b.interactions}', type {a.type})");
                            Probe(a);
                        }
                    }
        }
        catch (Exception e)
        {
            SelfTest.Log("game keys: dump failed " + e.Message);
        }
        foreach (var k in ModKeys.All) SelfTest.Log($"game keys: mod {k.Id} = {ModKeys.Value(k.Id)} bound {ModKeys.BoundPath(k.Id)}");
        SelfTest.Log($"game keys: before: {State()}, focused {UnityEngine.Application.isFocused}");
    }

    private static readonly Dictionary<string, int> Fired = new();

    private static readonly List<InputAction> Probed = new();

    private static void Probe(InputAction a) { if (!Probed.Contains(a)) Probed.Add(a); }

    /// <summary>Per frame while probing: which game actions saw a press.</summary>
    public static void Poll()
    {
        foreach (var a in Probed)
        {
            if (a.WasPressedThisFrame()) Count(a.actionMap?.name + "/" + a.name + ".pressed");
            if (a.WasPerformedThisFrame()) Count(a.actionMap?.name + "/" + a.name + ".performed");
        }
        if (_kb != null && (_kb.bKey.wasPressedThisFrame || _kb.iKey.wasPressedThisFrame)) Count("keyboard.press");
    }

    private static void Count(string k) => Fired[k] = Fired.TryGetValue(k, out var c) ? c + 1 : 1;

    private static string FiredText() => string.Join(", ", Fired.Select(kv => $"{kv.Key} {kv.Value}"));

    private static void AfterB()
    {
        SelfTest.Log($"game keys: actions fired after B: {FiredText()}");
        SelfTest.Log($"game keys: after B: {State()}");
        try
        {
            var id = LocalPlayer.Inventory?.RightHandItem?._itemID ?? 0;
            SelfTest.Log($"game keys: held item {id} = {Sons.Items.Core.ItemDatabaseManager.ItemById(id)?.Name}");
        }
        catch (Exception e) { SelfTest.Log("game keys: no item name " + e.Message); }
        SelfTest.Check("game keys: B opens the game's guide book", State().StartsWith("book up True") && !BookHost.ModPageActive, State());
        SelfTest.Check("game keys: our tab is on the book B raised", BookHost.IsOpen && BookTabs.Tabs.Count == 1, State());
    }

    private static string _firstBook = "";

    private static void AfterX()
    {
        SelfTest.Log($"game keys: actions fired after X: {FiredText()}");
        SelfTest.Log($"game keys: after X: {State()}");
        SelfTest.Check("game keys: X switched to the other game book", BookHost.IsOpen && BookHost.Which != _firstBook, $"was {_firstBook}, {State()}");
        SelfTest.Check("game keys: our tab is on the other book too", BookHost.IsOpen && BookTabs.Tabs.Count == 1, State());
    }

    private static void AfterI()
    {
        SelfTest.Log($"game keys: actions fired after I: {FiredText()}");
        SelfTest.Log($"game keys: after I: {State()}");
        SelfTest.Check("game keys: I opens the game's inventory", LocalPlayer.IsInInventory, State());
    }

    private static string State()
    {
        string right;
        try { right = LocalPlayer.Inventory?.RightHandItem?._itemID.ToString() ?? "nothing"; } catch { right = "?"; }
        bool gameBook = false;
        try
        {
            // B opens whichever book the player holds: the blueprint book, or in a new world the tutorial book from the starting package.
            foreach (var b in UnityEngine.Object.FindObjectsOfType<Sons.Weapon.BlueprintBookController>()) gameBook |= b.gameObject.activeInHierarchy;
            foreach (var b in UnityEngine.Object.FindObjectsOfType<Sons.Weapon.TutorialBookController>()) gameBook |= b.gameObject.activeInHierarchy;
        }
        catch { /* none */ }
        return $"book up {gameBook}, book open {BookHost.IsOpen} ({BookHost.Which}, {BookTabs.Tabs.Count} tabs), mod page {BookHost.Tab}, right hand {right}, in inventory {LocalPlayer.IsInInventory}";
    }

    private static void Send(Key key, bool down)
    {
        if (_kb == null) return;
        try
        {
            var buffer = StateEvent.From(_kb, out var ev, Unity.Collections.Allocator.Temp);
            _kb[key].WriteValueIntoEvent(down ? 1f : 0f, ev);
            InputSystem.QueueEvent(ev);
            buffer.Dispose();
        }
        catch (Exception e)
        {
            SelfTest.Log("game keys: key event failed " + e.Message);
        }
    }
}
