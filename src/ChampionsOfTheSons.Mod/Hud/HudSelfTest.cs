using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>HUD checks: nameplates, damage numbers, massacre counter and payout, level-up notice. Screenshots need the windowed run.</summary>
internal static class HudSelfTest
{
    private static HudFeature Hud => HudFeature.Instance!;
    private static PlayerCharacter Pc => PlayerState.Character;

    private static VailActor? _a, _b, _elite;
    private static readonly List<VailActor> Victims = new();
    private static int _numbersBefore;

    private const float T0 = 20f;

    public static void Register()
    {
        SelfTest.Add(T0, "hud: spawn", Spawn);
        SelfTest.Add(T0 + 2.5f, "hud: nameplates", Plates);
        SelfTest.Add(T0 + 3f, "hud: hit", Hit);
        SelfTest.Add(T0 + 4f, "hud: numbers + health", Numbers);
        SelfTest.Add(T0 + 4.1f, "hud: shot nameplates", () => TestKit.Screenshot("hud-01-nameplates"));
        SelfTest.Add(T0 + 5f, "hud: toggles", Toggles);
        SelfTest.Add(T0 + 6f, "hud: massacre kills", KillStreak);
        SelfTest.Add(T0 + 8f, "hud: massacre counter", MassacreCounter);
        SelfTest.Add(T0 + 8.1f, "hud: shot massacre", () => TestKit.Screenshot("hud-02-massacre"));
        SelfTest.Add(T0 + 9f, "hud: level up + payout", LevelUpAndPayout);
        SelfTest.Add(T0 + 10f, "hud: notices", Notices);
        SelfTest.Add(T0 + 10.05f, "hud: lift xp bar", () => Hud.Progress.LiftXpBar(200));
        SelfTest.Add(T0 + 10.1f, "hud: shot notices", () => TestKit.Screenshot("hud-03-notices"));
        SelfTest.Add(T0 + 11f, "hud: cleanup", () =>
        {
            Hud.Progress.LiftXpBar(8);
            Cleanup();
        });
        SelfTest.Add(T0 + 12f, "hud: open sheet", () => UI.CotsUi.Inventory.Open());
        SelfTest.Add(T0 + 13f, "hud: sheet row tooltips", SheetTips);
        SelfTest.Add(T0 + 13.5f, "hud: shot sheet tooltip", () => TestKit.Screenshot("hud-04-sheet-tooltip"));
        SelfTest.Add(T0 + 14f, "hud: close sheet", () =>
        {
            UI.CotsUi.Inventory.ReleaseSimulatedHover();
            UI.CotsUi.Inventory.Close();
        });
    }

    private static void Spawn()
    {
        var hell = WorldState.Rules with { Difficulty = Difficulties.Hell };
        _a = TestKit.Spawn(VailActorTypeId.Carl, 7, -2.5f);
        _b = TestKit.Spawn(VailActorTypeId.Carl, 9, 2.5f);
        _elite = TestKit.Spawn(VailActorTypeId.Carl, 11, 0, hell);
        SelfTest.Check("hud: spawned three enemies", _a != null && _b != null && _elite != null);
    }

    private static void Plates()
    {
        var a = Hud.Plates.Info(_a!);
        var e = Hud.Plates.Info(_elite!);
        var prof = TestKit.Profile(_a);
        SelfTest.Log($"hud: tracked {Hud.Plates.Tracked}, plates shown {Hud.Plates.Visible}; a={a}");
        SelfTest.Check("hud: normal enemy has a visible plate", a is { Visible: true }, a?.ToString() ?? "untracked");
        SelfTest.Check("hud: plate level matches profile", a != null && prof != null && a.Value.Level == prof.Profile.Level, $"{a?.Level} vs {prof?.Profile.Level}");
        SelfTest.Check("hud: plate has the kind name and level", a != null && a.Value.Name.Contains("Cannibal") && a.Value.Name.Contains(prof!.Profile.Level.ToString()), a?.Name);
        SelfTest.Check("hud: elite plate has a tier and affixes", e is { Visible: true, Tier: not EnemyTier.Normal } && e.Value.Affixes.Length > 0, e?.ToString() ?? "untracked");
        SelfTest.Check("hud: full health bar", a is { Fraction: > 0.99f });
    }

    private static void Hit()
    {
        _numbersBefore = Hud.Numbers.Spawned;
        TestKit.Hit(_a!, 20);
        TestKit.Hit(_a!, 20);
        TestKit.Hit(_elite!, 5000);
        TestKit.Hit(_b!, 9);
    }

    private static void Numbers()
    {
        int spawned = Hud.Numbers.Spawned - _numbersBefore;
        SelfTest.Check("hud: damage numbers spawned per hit", spawned == 4, $"{spawned} (last '{Hud.Numbers.LastText}', crit {Hud.Numbers.LastCrit}), {Hud.Numbers.Active} on screen");
        var a = Hud.Plates.Info(_a!);
        var rt = TestKit.Profile(_a);
        var hp = EnemyRegistry.Health(_a!.GetStatsManager().GetStatsManager());
        double expected = rt == null || hp == null ? 1 : Math.Max(0, hp._currentValue / rt.Profile.MaxHealth);
        SelfTest.Check("hud: health bar follows health", a is { Fraction: < 0.999f } && Math.Abs(a.Value.Fraction - expected) < 0.02, $"{a?.Fraction} vs {expected:0.###}");
    }

    private static void Toggles()
    {
        int n = Hud.Numbers.Spawned;
        Config.ShowDamageNumbers.Value = false;
        TestKit.Hit(_b!, 3);
        SelfTest.Check("hud: damage numbers can be turned off", Hud.Numbers.Spawned == n);
        Config.ShowDamageNumbers.Value = true;

        Config.ShowNameplates.Value = false;
        // Plates drop on the next frame; the check runs in the next step.
    }

    private static void KillStreak()
    {
        // The nameplate toggle is still off here: confirm, then restore.
        SelfTest.Check("hud: nameplates can be turned off", Hud.Plates.Info(_a!) is { Visible: false });
        Config.ShowNameplates.Value = true;

        Pc.Massacre.Reset();
        Victims.Clear();
        for (int i = 0; i < 4; i++) Victims.Add(TestKit.Spawn(VailActorTypeId.Carl, 6 + i, -3 + i * 2)!);
        foreach (var v in Victims) TestKit.Kill(v);
    }

    private static void MassacreCounter()
    {
        SelfTest.Log($"hud: massacre kills={Pc.Massacre.Kills} bank={Pc.Massacre.PendingXp:N0} left={Pc.Massacre.TimeLeft:0.0}; widget '{Hud.Progress.MassacreText}'");
        SelfTest.Check("hud: massacre counts kills", Pc.Massacre.Kills >= 3, Pc.Massacre.Kills.ToString());
        SelfTest.Check("hud: massacre counter shown", Hud.Progress.MassacreVisible && Hud.Progress.MassacreText.Contains(Pc.Massacre.Kills.ToString()), Hud.Progress.MassacreText);
        SelfTest.Check("hud: xp bar shows the level", Hud.Progress.XpText.Contains(Pc.Progress.Level.ToString()), Hud.Progress.XpText);
    }

    private static void LevelUpAndPayout()
    {
        SelfTest.Check("hud: no payout banner before the streak ends", !Hud.Progress.PayoutBanner.Visible);
        double before = Pc.Progress.Xp;
        int level = Pc.Progress.Level;
        // Streak ends: pays out through AddXp, and enough extra XP to cross level 20 (a Untainted Heart reward).
        Pc.Tick(1000);
        double need = 0;
        for (int l = Pc.Progress.Level; l < 21; l++) need += XpCurve.Goal(l);
        Pc.AddXp(need / Math.Max(0.01, Pc.Stats.Get(StatIds.ExpGain)));
        SelfTest.Log($"hud: xp {before:N0} -> level {level} -> {Pc.Progress.Level}");
    }

    private static void Notices()
    {
        var p = Hud.Progress;
        SelfTest.Check("hud: massacre payout popup shown", p.PayoutBanner.Visible && p.PayoutBanner.Body.Contains("XP"), p.PayoutBanner.Body);
        SelfTest.Check("hud: massacre counter hidden after payout", !p.MassacreVisible);
        SelfTest.Check("hud: level-up notice shown", p.LevelBanner.Visible && p.LevelBanner.Body.Contains(Pc.Progress.Level.ToString()), p.LevelBanner.Body);
        SelfTest.Check("hud: level-up notice lists the reward item", p.LevelBanner.Body.Contains(HudText.ItemName(Progress.HeartOfPurity)), p.LevelBanner.Body);
    }

    private static void Cleanup()
    {
        foreach (var v in new[] { _a, _b, _elite })
            if (v != null && !v.IsDead()) TestKit.Kill(v);
        Pc.Massacre.Reset();
        SelfTest.Log($"hud: tracked after cleanup {Hud.Plates.Tracked}");
    }

    private static void SheetTips()
    {
        var panel = UI.CotsUi.Inventory;
        panel.RefreshAll();
        var rows = panel.SheetRows.ToDictionary(r => r.Id);
        foreach (var id in new[] { "thorns", "health_on_hit", "armor_piercing", "massacre_time" })
            SelfTest.Check($"hud: sheet has row {id}", rows.ContainsKey(id));
        var noTip = panel.SheetRows.Where(r => !r.Header && r.Tip.Length == 0).Select(r => r.Id).ToList();
        SelfTest.Check("hud: every sheet row has a tooltip", noTip.Count == 0, string.Join(",", noTip));
        SelfTest.Check("hud: armor tooltip shows the live reduction", rows["armor"].Tip.Contains("%"), rows["armor"].Tip);

        int points = Pc.Perks.AvailablePoints(Pc.Progress);
        SelfTest.Check("hud: perk points shown next to the level", points > 0 && panel.PerkPointsText.StartsWith(points.ToString()), $"{points} / '{panel.PerkPointsText}'");

        // The same UI raycast the item slots use.
        string key = UI.InventoryPanel.RowPrefix + "physical_reduction";
        var hit = panel.RaycastSlotAt(panel.ScreenPointOfRow("physical_reduction"));
        SelfTest.Check("hud: raycast finds the sheet row", hit == key, hit ?? "null");

        panel.SimulateHover(key);
        SelfTest.Check("hud: row tooltip shown on hover", panel.RowTipShown == "physical_reduction" && panel.RowTipText.Contains("armor"), panel.RowTipText);
        var b = panel.TooltipBounds;
        var c = panel.CanvasSize;
        SelfTest.Check("hud: row tooltip inside the canvas", b.xMin >= 0 && b.yMin >= 0 && b.xMax <= c.x && b.yMax <= c.y, b.ToString());
    }
}
