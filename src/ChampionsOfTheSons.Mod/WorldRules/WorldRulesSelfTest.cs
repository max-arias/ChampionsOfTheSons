using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;
using TheForest.Utils;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>COS-11 in-game checks: the picker opens when there is no difficulty and the player is controllable, its choice is saved, host settings persist.</summary>
internal static class WorldRulesSelfTest
{
    private const float T0 = 76;

    public static void Register()
    {
        SelfTest.Add(T0, "picker: appears for a world without a difficulty", () =>
        {
            WorldState.FromSave(new WorldSave());
            SelfTest.Check("a fresh world has no chosen difficulty", !WorldState.DifficultyChosen);
            SelfTest.Log($"GameState.IsPlayerControllable = {WorldRulesFeature.PlayerControllable}");
            SelfTest.Check("controllable host, no difficulty -> opens", WorldRulesFeature.ShouldAutoOpen(true, true, false, false, true));
            SelfTest.Check("not while a cutscene has the player (original #13)", !WorldRulesFeature.ShouldAutoOpen(true, true, false, false, false));
            SelfTest.Check("not once chosen, not twice, not for clients", !WorldRulesFeature.ShouldAutoOpen(true, true, true, false, true) &&
                !WorldRulesFeature.ShouldAutoOpen(true, true, false, true, true) && !WorldRulesFeature.ShouldAutoOpen(true, false, false, false, true));
            SelfTest.Check("in this world the real conditions agree with the player being controllable",
                WorldRulesFeature.ShouldAutoOpen(LocalPlayer.IsInWorld, ChampionsOfTheSons.Mod.Bridge.EnemyRegistry.IsAuthority, WorldState.DifficultyChosen, false, WorldRulesFeature.PlayerControllable)
                == WorldRulesFeature.PlayerControllable);
            WorldRulesFeature.Picker.Open();
            SelfTest.Check("picker is open", WorldRulesFeature.Picker.IsOpen);
        });

        SelfTest.Add(T0 + 1.5f, "picker: screenshot", () => TestKit.Screenshot("wr-01-picker"));

        SelfTest.Add(T0 + 3, "picker: clicking a tier saves the choice", () =>
        {
            var master = WorldRulesFeature.Picker.Buttons.First(b => b.Tier == Difficulties.All[3]).Button;
            var button = master.Root.GetComponent<Button>();
            SelfTest.Check("the tier button exists", button != null);
            button?.onClick.Invoke();
            SelfTest.Check("picker closed", !WorldRulesFeature.Picker.IsOpen);
            SelfTest.Check("difficulty is Master", WorldState.DifficultyChosen && WorldState.Rules.Difficulty == Difficulties.All[3]);
            var json = SaveCodec.Write(WorldState.ToSave());
            WorldState.FromSave(new WorldSave());
            SelfTest.Check("(reset to no difficulty)", !WorldState.DifficultyChosen);
            WorldState.FromSave(SaveCodec.ReadWorld(json));
            SelfTest.Check("the choice survives a save and load", WorldState.DifficultyChosen && WorldState.Rules.Difficulty.Id == "difficulty.master");
        });

        SelfTest.Add(T0 + 4, "host settings: edit", () =>
        {
            WorldState.Update(r => r with { EnemyHealth = 2, AllowElites = false });
            WorldRulesFeature.Settings.ToggleAbility(EnemyAbility.Meteor);
            WorldRulesFeature.Settings.Open();
            SelfTest.Check("host settings open", WorldRulesFeature.Settings.IsOpen);
        });

        SelfTest.Add(T0 + 5.5f, "host settings: screenshot", () => TestKit.Screenshot("wr-02-host-settings"));

        SelfTest.Add(T0 + 7, "host settings: saved and restored", () =>
        {
            WorldRulesFeature.Settings.Close();
            var json = SaveCodec.Write(WorldState.ToSave());
            WorldState.FromSave(new WorldSave());
            WorldState.FromSave(SaveCodec.ReadWorld(json));
            var r = WorldState.Rules;
            SelfTest.Check("multiplier, elites switch and disabled ability survive a save and load",
                r.EnemyHealth == 2 && !r.AllowElites && r.DisabledAbilities.Contains(EnemyAbility.Meteor) && r.Difficulty.Id == "difficulty.master");
            WorldState.FromSave(new WorldSave());
            WorldState.SetDifficulty(Difficulties.Easy);
        });
    }
}
