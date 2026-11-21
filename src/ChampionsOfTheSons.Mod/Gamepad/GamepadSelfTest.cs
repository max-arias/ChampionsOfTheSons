using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.HostRules;
using ChampionsOfTheSons.Mod.Perks;

namespace ChampionsOfTheSons.Mod.Pad;

/// <summary>COS-30 checks: pad input is simulated through <see cref="PadInput.Inject"/>, so the whole path (bindings, focus, confirm) runs; real controller feel is on the hands-on list.</summary>
internal static class GamepadSelfTest
{
    private static string _tab = "";

    public static void Register()
    {
        SelfTest.PhaseReset += CloseAll;
        float t = 0;
        SelfTest.Add(t += 0.2f, "pad: unpause", TestKit.Unpause);
        SelfTest.Add(t += 0.5f, "pad: binding opens the book", () =>
        {
            SelfTest.Check("pad: panels closed to start", GamepadFeature.OpenPanel == null && !Book.BookHost.ModPageActive);
            PadInput.Inject(PadButton.LeftShoulder, PadButton.DpadUp);
        });
        SelfTest.Add(t += 2.5f, "pad: book open", () =>
        {
            SelfTest.Check("pad: LeftShoulder+DpadUp opened the guide book on Gear", Book.BookHost.ModPageActive && Book.BookHost.Tab == Book.BookTab.Gear,
                $"open {Book.BookHost.IsOpen}, tab {Book.BookHost.Tab}, last failure '{Book.BookHost.LastFailure}'");
            Book.BookHost.Close();
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
            SelfTest.Check("pad: pointer follows focus", PadPointer.Active);
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

    private static void CloseAll()
    {
        if (Book.BookHost.IsOpen) Book.BookHost.Close();
        if (PerksFeature.Panel.IsOpen) PerksFeature.Panel.Close();
        if (WorldRulesFeature.Settings.IsOpen) WorldRulesFeature.Settings.Close();
        PadFocus.Clear();
        PadPointer.Release();
    }
}
