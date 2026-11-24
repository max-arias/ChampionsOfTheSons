using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Spells;
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
            // COS-166 playtest commands.
            int points = Pc.Perks.AvailablePoints(Pc.Progress);
            SelfTest.Check("admin: learnall unlocks every spell for free",
                Run("learnall") && Pc.Perks.UnlockedSpells.Count == ModContent.Db.Spells.Count && Pc.Perks.AvailablePoints(Pc.Progress) == points,
                $"{Pc.Perks.UnlockedSpells.Count}/{ModContent.Db.Spells.Count} spells, points {points} -> {Pc.Perks.AvailablePoints(Pc.Progress)}; {AdminFeature.LastReply}");
            SelfTest.Check("admin: learnall fills empty spell slots", Pc.Spells.Loadout.All(s => s != null), string.Join(",", Pc.Spells.Loadout));
            int rings = ModContent.Db.Items.Values.Count(d => d.Type == "ring");
            bag = Pc.Inventory.Slots.Count;
            SelfTest.Check("admin: giveall gives one of every item of a type", Run("giveall ring") && Pc.Inventory.Slots.Count == bag + rings,
                $"bag {bag} -> {Pc.Inventory.Slots.Count}, {rings} rings; {AdminFeature.LastReply}");
            SelfTest.Check("admin: giveall unknown type fails", !Run("giveall nothing"));
            SelfTest.Check("admin: playtest sets everything up",
                Run("playtest") && Pc.Progress.Level >= 30 && AdminState.God && AdminState.NoCooldowns && AdminState.InfiniteEnergy, AdminFeature.LastReply);
            int arrow = AdminState.ArrowItemId();
            int arrows = arrow >= 0 ? TheForest.Utils.LocalPlayer.Inventory.AmountOf(arrow, true, false) : 0;
            SelfTest.Check("admin: playtest gives arrows for the bows", arrows > 0, $"arrow item {arrow} ({(arrow >= 0 ? Bridge.HotkeyBridge.Name(arrow) : "none")}), {arrows} owned");
            // COS-173: the dearest spell costs more than max energy; infinite energy casts it anyway, twice.
            var dearest = ModContent.Db.Spells.Values.Where(sp => SpellCaster.IsImplemented(sp.Id) && !sp.Channeled && !sp.Toggle)
                .OrderByDescending(sp => Pc.Spells.CostOf(sp.Id, Pc.Stats).Total).First();
            Pc.Spells.Equip(0, dearest.Id);
            Pc.Spells.ReadyAll(); // slotting starts the cooldown; no-cooldowns refills it next frame, the test casts now
            var first = SpellCaster.Cast(0);
            Pc.Spells.ReadyAll();
            var second = SpellCaster.Cast(0);
            SelfTest.Check("admin: infinite energy casts the dearest spell twice", first == SpellCastOutcome.Cast && second == SpellCastOutcome.Cast,
                $"{dearest.Id} cost {Pc.Spells.CostOf(dearest.Id, Pc.Stats).Total:0} vs max {SpellCaster.MaxEnergy:0}: {first}, {second}");
            Run("god off");
            Run("nocooldowns off");
            Run("energy off");
            Run("respec");
            Pc.Spells.Restore(new[] { "spell.blink" });
            Pc.Spells.Equip(0, "spell.blink");
            SelfTest.Check("admin: nocooldowns readies spells", Run("nocooldowns on") && Pc.Spells.IsReady(0), AdminFeature.LastReply);
            Run("nocooldowns off");
            SelfTest.Check("admin: list items with a filter", Run("list items ring") && AdminFeature.LastReply.Contains("ring"));
            TestKit.Unpause();
            SelfTest.Check("admin: god on", Run("god on") && AdminState.God, AdminFeature.LastReply);
            var health = LocalPlayer.Vitals.Health;
            health.SetCurrentValue(health.GetMax());
            _hp = health._currentValue;
            LocalPlayer.Stats.Hit(40f);
            SelfTest.Check("admin: god mode takes no damage", health._currentValue >= _hp - 0.01f && PlayerBridge.LastIncoming is { After: 0 },
                $"{_hp:0.0} -> {health._currentValue:0.0}, last incoming {PlayerBridge.LastIncoming}");
        });

        SelfTest.Add(T0 + 1, "admin: god mode, heal, spawn, difficulty, console", () =>
        {
            var health = LocalPlayer.Vitals.Health;
            SelfTest.Check("admin: god mode still at full health 1 s later", health._currentValue >= health.GetMax() - 0.01f,
                $"health {health._currentValue:0.0} / max {health.GetMax():0.0}, last incoming {PlayerBridge.LastIncoming}");
            Run("god off");
            _hp = health._currentValue;
            LocalPlayer.Stats.Hit(20f);
            SelfTest.Check("admin: hits land again with god off", health._currentValue < _hp, $"{_hp:0.0} -> {health._currentValue:0.0}");
            SelfTest.Check("admin: heal", Run("heal") && health._currentValue >= health.GetMax() - 0.5f, AdminFeature.LastReply);

            _bosses = EnemyRegistry.Snapshot().Count(e => e.Profile.Tier == EnemyTier.Boss);
            SelfTest.Check("admin: spawn boss", Run("spawn boss Carl 1"), AdminFeature.LastReply);

            int before = WorldState.Rules.ExtraEnemyLevels;
            SelfTest.Check("admin: extra levels", Run("extralevels 4") && WorldState.Rules.ExtraEnemyLevels == 4, AdminFeature.LastReply);
            SelfTest.Check("admin: extra levels out of range refused", !Run("extralevels 500") && WorldState.Rules.ExtraEnemyLevels == 4, AdminFeature.LastReply);
            WorldState.SetRulesForTest(WorldState.Rules with { ExtraEnemyLevels = before });

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
