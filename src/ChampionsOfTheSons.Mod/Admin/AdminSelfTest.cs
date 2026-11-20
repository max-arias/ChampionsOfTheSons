using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Admin;

/// <summary>COS-99 in-game checks: the access rule, the player commands on the local character, spawn by tier, difficulty, the game console.</summary>
internal static class AdminSelfTest
{
    private const float T0 = 2;
    private static int _bosses;
    private static float _hp;

    private static PlayerCharacter Pc => PlayerState.Character;

    private static bool Run(string line) => AdminFeature.Run(line, CommandSource.SinglePlayer, null);

    public static void Register()
    {
        SelfTest.Add(T0, "admin: access and player commands", () =>
        {
            SelfTest.Check("admin: help lists the commands", Run("help") && AdminFeature.LastReply.Contains("cots give"));
            SelfTest.Check("admin: a client is refused", !AdminFeature.Run("cots level 50", CommandSource.Client, null) && Pc.Progress.Level == 1, AdminFeature.LastReply);
            SelfTest.Check("admin: level", Run("cots level 20") && Pc.Progress.Level == 20, AdminFeature.LastReply);
            SelfTest.Check("admin: addlevels", Run("addlevels 2") && Pc.Progress.Level == 22, AdminFeature.LastReply);
            double xp = Pc.Progress.Xp;
            SelfTest.Check("admin: xp", Run("xp 100") && Pc.Progress.Xp > xp, AdminFeature.LastReply);
            SelfTest.Check("admin: perk points", Run("points 3") && Pc.Progress.BonusPerkPoints == 3, AdminFeature.LastReply);
            int bag = Pc.Inventory.Slots.Count;
            SelfTest.Check("admin: give random item", Run("give random:4 15") && Pc.Inventory.Slots.Count == bag + 1, AdminFeature.LastReply);
            SelfTest.Check("admin: give unknown id fails", !Run("give item.does_not_exist"));
            SelfTest.Check("admin: unknown player fails", !Run("heal 99"));
            SelfTest.Check("admin: respec", Run("respec") && Pc.Perks.SpentPoints == 0, AdminFeature.LastReply);
            Pc.Spells.Restore(new[] { "spell.blink" });
            Pc.Spells.Equip(0, "spell.blink");
            SelfTest.Check("admin: nocooldowns readies spells", Run("nocooldowns on") && Pc.Spells.IsReady(0), AdminFeature.LastReply);
            Run("nocooldowns off");
            SelfTest.Check("admin: list items with a filter", Run("list items ring") && AdminFeature.LastReply.Contains("ring"));
            TestKit.Unpause();
            Run("god on");
            var health = LocalPlayer.Vitals.Health;
            health.SetCurrentValue(health.GetMax());
            _hp = health._currentValue;
            LocalPlayer.Stats.Hit(40f);
        });

        SelfTest.Add(T0 + 1, "admin: god mode, heal, spawn, difficulty, console", () =>
        {
            var health = LocalPlayer.Vitals.Health;
            SelfTest.Check("admin: god mode takes no damage", health._currentValue >= _hp - 0.01f, $"{_hp:0.0} -> {health._currentValue:0.0}");
            Run("god off");
            LocalPlayer.Stats.Hit(20f);
            SelfTest.Check("admin: hits land again with god off", health._currentValue < _hp, $"{_hp:0.0} -> {health._currentValue:0.0}");
            SelfTest.Check("admin: heal", Run("heal") && health._currentValue >= health.GetMax() - 0.5f, AdminFeature.LastReply);

            _bosses = EnemyRegistry.Snapshot().Count(e => e.Profile.Tier == EnemyTier.Boss);
            SelfTest.Check("admin: spawn boss", Run("spawn boss Carl 1"), AdminFeature.LastReply);

            var before = WorldState.Rules.Difficulty;
            SelfTest.Check("admin: difficulty by index", Run("difficulty 3") && WorldState.Rules.Difficulty == Difficulties.All[3], AdminFeature.LastReply);
            WorldState.SetRulesForTest(WorldState.Rules with { Difficulty = before });

            var console = TheForest.DebugConsole.GetInstance();
            if (console == null) SelfTest.Log("admin: no debug console instance; game-console path not checked");
            else
            {
                Pc.SetLevel(5);
                console.SendCommand("cots level 7");
                SelfTest.Check("admin: the game console runs 'cots'", Pc.Progress.Level == 7, AdminFeature.LastReply);
            }
        });

        SelfTest.Add(T0 + 3, "admin: spawned enemy has the forced tier", () =>
        {
            int now = EnemyRegistry.Snapshot().Count(e => e.Profile.Tier == EnemyTier.Boss);
            SelfTest.Check("admin: a boss-tier enemy was added", now > _bosses, $"bosses {_bosses} -> {now}");
            TestKit.Screenshot("admin-01-boss");
        });
    }
}
