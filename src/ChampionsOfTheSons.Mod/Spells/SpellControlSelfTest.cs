using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>COS-82 in-game checks through the key path: Blink aims while held and casts on release, Sustain Shield runs while held, silence blocks.</summary>
internal static class SpellControlSelfTest
{
    private const float T0 = 70;
    private static Vector3 _start;
    private static PlayerCharacter Pc => PlayerState.Character;

    private static void Ready(int slot, string id)
    {
        Pc.Spells.Equip(slot, id);
        Pc.Spells.Tick(1e6, Pc.Stats);
        var st = LocalPlayer.Vitals.Stamina;
        st.SetCurrentValue(st.GetMax());
    }

    public static void Register()
    {
        SelfTest.Add(T0, "cast keys: hold Blink", () =>
        {
            TestKit.Unpause();
            Pc.Stats.SetSource("test.energy", new[] { new StatModifier(StatIds.MaxEnergy, EffectOp.Add, 3000) });
            Ready(0, "spell.blink");
            _start = LocalPlayer.Transform.position;
            SpellControl.TestHeld[0] = true;
        });
        SelfTest.Add(T0 + 1, "cast keys: Blink aims while held", () =>
        {
            SelfTest.Check("cast keys: Blink is aiming, not cast", SpellControl.Input.IsAiming(0) && Pc.Spells.IsReady(0), $"aiming {SpellControl.Input.IsAiming(0)}, ready {Pc.Spells.IsReady(0)}");
            SelfTest.Check("cast keys: aim aid shown", SpellControl.Shown != null, SpellControl.Shown?.ToString() ?? "none");
            SelfTest.Check("cast keys: player has not moved yet", Vector3.Distance(_start, LocalPlayer.Transform.position) < 0.5f);
            TestKit.Screenshot("cast-keys-01-blink-aim");
            SpellControl.TestHeld[0] = false;
        });
        SelfTest.Add(T0 + 2, "cast keys: Blink casts on release", () =>
        {
            SelfTest.Check("cast keys: release cast Blink", SpellControl.LastOutcome == SpellCastOutcome.Cast && !Pc.Spells.IsReady(0), SpellControl.LastOutcome.ToString());
            SelfTest.Check("cast keys: aim aid gone after the cast", SpellControl.Shown == null);
            Ready(1, "spell.sustain_shield");
            SpellControl.TestHeld[1] = true;
        });
        SelfTest.Add(T0 + 3, "cast keys: channel runs while held", () =>
        {
            SelfTest.Check("cast keys: Sustain Shield runs while held", Pc.Spells.IsActive(1) && SpellWorld.Shield.Channeling);
            SpellControl.TestHeld[1] = false;
        });
        SelfTest.Add(T0 + 4, "cast keys: channel stops on release", () =>
        {
            SelfTest.Check("cast keys: Sustain Shield stops on release", !Pc.Spells.IsActive(1) && !SpellWorld.Shield.Channeling);
            Ready(2, "spell.war_cry");
            Pc.Stats.SetSource("test.silence", new[] { new StatModifier(StatIds.Silenced, EffectOp.Enable, 1) });
            SpellControl.TestHeld[2] = true;
        });
        SelfTest.Add(T0 + 5, "cast keys: silence", () =>
        {
            SelfTest.Check("cast keys: silence blocks the cast", SpellControl.LastOutcome == SpellCastOutcome.Silenced && Pc.Spells.IsReady(2), SpellControl.LastOutcome.ToString());
            SpellControl.TestHeld[2] = false;
            Pc.Stats.RemoveSource("test.silence");
            Pc.Stats.RemoveSource("test.energy");
        });
    }
}
