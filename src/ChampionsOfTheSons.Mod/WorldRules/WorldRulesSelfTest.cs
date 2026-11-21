using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>
/// COS-11 / COS-130 in-game checks: the game's difficulty is read from the game, enemies roll at the party level plus its offset,
/// a client takes the host's settings, and host settings persist.
/// </summary>
internal static class WorldRulesSelfTest
{
    private const float T0 = 76;
    private static VailActor? _enemy;
    private static int _playerLevel;

    public static void Register()
    {
        SelfTest.Add(T0, "game difficulty: read from the game", () =>
        {
            var read = GameDifficultyReader.Read();
            SelfTest.Log($"game difficulty: {read.Mode}, offset {read.LevelOffset}, elites {read.EliteChance:P0}");
            SelfTest.Check("the game's mode is a known one", Enum.IsDefined(read.Mode), read.Mode.ToString());
            WorldRulesFeature.ReadGameDifficulty();
            SelfTest.Check("the host's rules carry the game's difficulty", WorldState.Rules.GameDifficulty == read);
            _playerLevel = PlayerState.Character.Progress.Level;
            _enemy = TestKit.Spawn(VailActorTypeId.Carl, 8, 0, WorldState.Rules with { AllowElites = false, ExtraEnemyLevels = 2 });
            SelfTest.Check("spawned a cannibal", _enemy != null);
        });

        SelfTest.Add(T0 + 2, "game difficulty: enemy level follows the party", () =>
        {
            var e = EnemyRegistry.Get(_enemy);
            SelfTest.Check("the cannibal has a profile", e != null);
            if (e == null) return;
            int center = _playerLevel + e.Kind.LevelExtra + WorldState.Rules.GameDifficulty.LevelOffset + 2;
            SelfTest.Check("level = player level ± 1 + kind + game offset + extra levels", Math.Abs(e.Profile.Level - Math.Max(1, center)) <= 1,
                $"player {_playerLevel}, enemy {e.Profile.Level}, expected {center} ± 1");
            SelfTest.Check("its band follows its level", e.Combat.Difficulty == Difficulties.ForLevel(e.Profile.Level));
            if (_enemy != null) TestKit.Kill(_enemy);
        });

        SelfTest.Add(T0 + 3, "game difficulty: a client takes the host's settings", () =>
        {
            var saved = WorldState.Rules;
            WorldRulesFeature.Apply(new WorldSettings(GameMode.HardSurvival, 10, 7, false));
            var r = WorldState.Rules;
            SelfTest.Check("mode, offset and extra levels arrive", r.GameDifficulty.Mode == GameMode.HardSurvival && r.GameDifficulty.LevelOffset == 10 && r.ExtraEnemyLevels == 7);
            WorldState.SetRulesForTest(saved);
        });

        SelfTest.Add(T0 + 4, "host settings: edit", () =>
        {
            WorldState.Update(r => r with { EnemyHealth = 2, AllowElites = false, ExtraEnemyLevels = 5, LootLevel = LootLevelPolicy.Closest, MinLootRarity = 2 });
            WorldRulesFeature.Settings.ToggleAbility(EnemyAbility.Meteor);
            WorldRulesFeature.Settings.Open();
            SelfTest.Check("host settings open", WorldRulesFeature.Settings.IsOpen);
        });

        SelfTest.Add(T0 + 5.5f, "host settings: screenshot", () => TestKit.Screenshot("wr-02-host-settings"));

        SelfTest.Add(T0 + 7, "host settings: saved and restored", () =>
        {
            WorldRulesFeature.Settings.Close();
            var game = WorldState.Rules.GameDifficulty;
            var json = SaveCodec.Write(WorldState.ToSave());
            WorldState.FromSave(new WorldSave());
            WorldState.FromSave(SaveCodec.ReadWorld(json));
            var r = WorldState.Rules;
            SelfTest.Check("multiplier, extra levels, elites switch and disabled ability survive a save and load",
                r.EnemyHealth == 2 && r.ExtraEnemyLevels == 5 && !r.AllowElites && r.DisabledAbilities.Contains(EnemyAbility.Meteor));
            SelfTest.Check("the game's difficulty is kept across a world load", r.GameDifficulty == game);
            SelfTest.Check("loot level and lowest drop rarity survive a save and load (COS-77)", r.LootLevel == LootLevelPolicy.Closest && r.MinLootRarity == 2,
                $"{r.LootLevel}, {r.MinLootRarity}");
            WorldState.FromSave(new WorldSave());
        });

        SelfTest.Add(T0 + 8, "host settings: friendly fire drives the game's PvP damage (COS-78)", () =>
        {
            bool before = WorldState.FriendlyFire;
            WorldState.SetFriendlyFire(false);
            FriendlyFireRule.Apply(false);
            SelfTest.Check("friendly fire off: the game's PvP damage is Off", FriendlyFireRule.Applied == "Off", FriendlyFireRule.Applied ?? "not applied");
            WorldState.SetFriendlyFire(true);
            FriendlyFireRule.Apply(true);
            SelfTest.Check("friendly fire on: the game's PvP damage is on", FriendlyFireRule.Applied is "Normal" or "Reduced", FriendlyFireRule.Applied ?? "not applied");
            WorldState.SetFriendlyFire(before);
            FriendlyFireRule.Apply(before);
        });

        SelfTest.Add(T0 + 9, "host settings: reset to defaults (COS-77)", () =>
        {
            var game = WorldState.Rules.GameDifficulty;
            WorldState.Update(r => r with { Xp = 3, LootLevel = LootLevelPolicy.Lowest, AllowElites = false });
            WorldRulesFeature.Settings.ResetDefaults();
            var r = WorldState.Rules;
            SelfTest.Check("reset brings every host setting back to its default and keeps the game's difficulty",
                r.Xp == 1 && r.LootLevel == LootLevelPolicy.Highest && r.AllowElites && !WorldState.FriendlyFire && r.GameDifficulty == game);
        });
    }
}
