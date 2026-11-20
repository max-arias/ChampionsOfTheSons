using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;
using ChampionsOfTheSons.Mod.HostRules;
using ChampionsOfTheSons.Mod.Perks;
using ChampionsOfTheSons.Mod.UI;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Pad;

/// <summary>COS-30 checks: pad input is simulated through <see cref="PadInput.Inject"/>, so the whole path (bindings, focus, confirm) runs; real controller feel is on the hands-on list.</summary>
internal static class GamepadSelfTest
{
    private static InventoryPanel Inv => CotsUi.Inventory;
    private static PlayerCharacter Pc => PlayerState.Character;
    private static GameObject? _before;
    private static string _tab = "";

    public static void Register()
    {
        SelfTest.PhaseReset += CloseAll;
        float t = 0;
        // These checks drive the screen panels; the inventory binding opens the guide book by default (COS-59).
        SelfTest.Add(t += 0.2f, "pad: unpause, screen inventory", () =>
        {
            TestKit.Unpause();
            _bookSetting = Config.InventoryInBook.Value;
            Config.InventoryInBook.Value = false;
        });
        SelfTest.Add(t += 0.5f, "pad: binding opens the inventory", () =>
        {
            var def = ModContent.Db.Items.Values.First(d => d.Type == "ring" && d.Rarity >= 1 && d.Rarity <= 3);
            TestKit.Give(def.Id, def.Rarity, 5);
            SelfTest.Check("pad: ring is in bag.0", Pc.Inventory.Get("bag.0") != null);
            SelfTest.Check("pad: panels closed to start", GamepadFeature.OpenPanel == null);
            PadInput.Inject(PadButton.LeftShoulder, PadButton.DpadUp);
        });
        SelfTest.Add(t += 0.5f, "pad: inventory open and focused", () =>
        {
            SelfTest.Check("pad: inventory opened by LeftShoulder+DpadUp", Inv.IsOpen);
            SelfTest.Check("pad: a control has focus", PadFocus.Current != null);
            _before = PadFocus.Current;
            PadInput.Inject(PadButton.DpadRight);
        });
        SelfTest.Add(t += 0.5f, "pad: d-pad moves focus", () =>
        {
            SelfTest.Check("pad: focus moved right", PadFocus.Current != null && PadFocus.Current != _before);
            var slot = Inv.Slot("bag.0")!;
            SelfTest.Check("pad: bag slot is focusable", PadFocus.FocusOn(Inv.PadRoot!, slot.Button.Root));
            SelfTest.Check("pad: pointer follows focus", PadPointer.Active && Vector2.Distance(PadPointer.Position, Inv.ScreenPointOf("bag.0")) < 6f,
                $"active={PadPointer.Active} {PadPointer.Position} vs {Inv.ScreenPointOf("bag.0")}");
            PadInput.Inject(PadButton.South);
        });
        SelfTest.Add(t += 0.6f, "pad: A equips the focused item", () =>
        {
            SelfTest.Check("pad: item left the bag", Pc.Inventory.Get("bag.0") == null);
            SelfTest.Log("pad: slots now " + string.Join(", ", Pc.Inventory.Slots.Keys));
            SelfTest.Check("pad: item is equipped", Pc.Inventory.Slots.Keys.Any(k => k.StartsWith("equip.ring", StringComparison.Ordinal)));
            string? equipped = Pc.Inventory.Slots.Keys.FirstOrDefault(k => k.StartsWith("equip.ring", StringComparison.Ordinal));
            PadFocus.FocusOn(Inv.PadRoot!, Inv.Slot(equipped ?? "equip.ring1")!.Button.Root);
            PadInput.Inject(PadButton.West);
        });
        SelfTest.Add(t += 0.6f, "pad: X opens the item menu", () =>
        {
            SelfTest.Check("pad: right-click menu open", Inv.MenuVisible);
            PadInput.Inject(PadButton.East);
        });
        SelfTest.Add(t += 0.5f, "pad: B closes the menu first, then the panel", () =>
        {
            SelfTest.Check("pad: menu closed, panel still open", !Inv.MenuVisible && Inv.IsOpen);
            PadInput.Inject(PadButton.East);
        });
        SelfTest.Add(t += 0.5f, "pad: panel closed by B", () =>
        {
            SelfTest.Check("pad: inventory closed", !Inv.IsOpen);
            SelfTest.Check("pad: focus cleared", PadFocus.Current == null);
        });
        SelfTest.Add(t += 1.5f, "pad: wait for the player to be controllable again", () =>
        {
            SelfTest.Log($"pad: controllable now = {HostRules.WorldRulesFeature.PlayerControllable}");
            PadInput.Inject(PadButton.LeftShoulder, PadButton.DpadRight);
        });

        SelfTest.Add(t += 0.5f, "pad: perk tree by binding", () =>
        {
            var panel = PerksFeature.Panel;
            SelfTest.Check("pad: perk tree opened", panel.IsOpen);
            SelfTest.Check("pad: perk tree has focusable controls", panel.PadRoot != null && PadFocus.Count(panel.PadRoot) > 10, panel.PadRoot == null ? "no root" : PadFocus.Count(panel.PadRoot).ToString());
            _tab = panel.CurrentTab;
            // Start from the current tab's button (the first focus goes to whatever is nearest the pointer, often a perk node),
            // then the d-pad moves along the tab row to the next tab.
            var tabButton = panel.TabButton(_tab);
            SelfTest.Check("pad: current tab is focusable", tabButton != null && panel.PadRoot != null && PadFocus.FocusOn(panel.PadRoot, tabButton));
            PadInput.Inject(PadButton.DpadRight);
        });
        SelfTest.Add(t += 0.5f, "pad: focus a tab and confirm", () =>
        {
            PadInput.Inject(PadButton.South);
        });
        SelfTest.Add(t += 0.5f, "pad: A on a tab switches it", () =>
        {
            SelfTest.Log($"pad: tab {_tab} -> {PerksFeature.Panel.CurrentTab}");
            SelfTest.Check("pad: perk tab changed by pad", PerksFeature.Panel.CurrentTab != _tab);
            PadInput.Inject(PadButton.LeftShoulder, PadButton.DpadRight);
        });
        SelfTest.Add(t += 0.5f, "pad: same binding closes the perk tree", () =>
        {
            SelfTest.Check("pad: perk tree closed by its binding", !PerksFeature.Panel.IsOpen);
        });
        SelfTest.Add(t += 1.5f, "pad: wait 2", () =>
        {
            PadInput.Inject(PadButton.LeftShoulder, PadButton.DpadDown);
        });
        SelfTest.Add(t += 0.5f, "pad: crafting by binding", () =>
        {
            var panel = Crafting.CraftingFeature.Panel;
            SelfTest.Check("pad: crafting opened", panel.IsOpen);
            SelfTest.Check("pad: crafting has focusable controls", panel.PadRoot != null && PadFocus.Count(panel.PadRoot) > 5);
            PadInput.Inject(PadButton.East);
        });
        SelfTest.Add(t += 0.5f, "pad: B closes crafting; spell binding casts", () =>
        {
            SelfTest.Check("pad: crafting closed by B", !Crafting.CraftingFeature.Panel.IsOpen);
            GamepadFeature.LastPadCast = -1;
        });
        SelfTest.Add(t += 1.5f, "pad: cast by binding", () =>
        {
            SelfTest.Log($"pad: controllable now = {HostRules.WorldRulesFeature.PlayerControllable}");
            PadInput.Inject(PadButton.RightShoulder, PadButton.East);
        });
        SelfTest.Add(t += 0.5f, "pad: spell binding fired", () =>
        {
            SelfTest.Check("pad: RightShoulder+East casts slot 2", GamepadFeature.LastPadCast == 1, GamepadFeature.LastPadCast.ToString());
            CloseAll();
        });
    }

    private static bool? _bookSetting;

    private static void CloseAll()
    {
        if (_bookSetting.HasValue) { Config.InventoryInBook.Value = _bookSetting.Value; _bookSetting = null; }
        if (Inv.IsOpen) { Inv.CloseMenu(); Inv.Close(); }
        if (PerksFeature.Panel.IsOpen) PerksFeature.Panel.Close();
        if (Crafting.CraftingFeature.Panel.IsOpen) Crafting.CraftingFeature.Panel.Close();
        if (WorldRulesFeature.Settings.IsOpen) WorldRulesFeature.Settings.Close();
        PadFocus.Clear();
        PadPointer.Release();
    }
}
