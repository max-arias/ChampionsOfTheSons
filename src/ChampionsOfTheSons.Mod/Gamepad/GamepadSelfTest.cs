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
        SelfTest.Add(t += 0.5f, "pad: binding opens the perks page", () =>
        {
            SelfTest.Check("pad: panels closed to start", GamepadFeature.OpenPanel == null);
            PadInput.Inject(PadButton.LeftShoulder, PadButton.DpadRight);
        });
        // COS-168: the perk tree is a page of the guide book; the binding raises the book on it.
        SelfTest.Add(t += 3f, "pad: perks page by binding", () =>
        {
            SelfTest.Check("pad: the binding opened the book on the Perks page", Book.BookHost.IsOpen && Book.BookHost.Tab == Book.BookTab.Perks,
                $"book {Book.BookHost.IsOpen}, tab {Book.BookHost.Tab}, {Book.BookHost.LastFailure}");
            CloseAll();
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
        if (WorldRulesFeature.Settings.IsOpen) WorldRulesFeature.Settings.Close();
        PadFocus.Clear();
        PadPointer.Release();
    }
}
