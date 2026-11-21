using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;

namespace ChampionsOfTheSons.Mod.Perks;

/// <summary>Perk panel checks: level up for points, buy perks, stats change, hit-test, tabs, spell book, respec with a Untainted Heart.</summary>
internal static class PerksSelfTest
{
    private static PerkPanel Panel => PerksFeature.Panel;
    private static PlayerCharacter Pc => PlayerState.Character;
    private static readonly List<string> Bought = new();

    public static void Register()
    {
        SelfTest.Add(20f, "perks: fresh character + level up", LevelUp);
        SelfTest.Add(20.5f, "perks: open panel", OpenPanel);
        SelfTest.Add(21f, "perks: buy perks, stats change", BuyPerks);
        SelfTest.Add(21.5f, "perks: hold to buy", HoldToBuy);
        SelfTest.Add(22f, "perks: states, hit-test, detail", StatesAndHover);
        SelfTest.Add(23f, "perks: shot melee", () => TestKit.Screenshot("10-perks-melee"));
        SelfTest.Add(23.5f, "perks: tabs", Tabs);
        SelfTest.Add(25f, "perks: shot magic", () => TestKit.Screenshot("11-perks-magic"));
        SelfTest.Add(25.5f, "perks: shot utility", () => { Panel.ShowTab(PerkCategory.Utility.ToString()); });
        SelfTest.Add(26.5f, "perks: shot utility 2", () => TestKit.Screenshot("12-perks-utility"));
        SelfTest.Add(27f, "perks: spell book", SpellBookTab);
        SelfTest.Add(27.8f, "perks: spell row hit-test", () =>
        {
            var hit = Panel.RaycastHover(Panel.ScreenPointOfSpellRow("spell.blink"));
            SelfTest.Check("perks: spell row hit-test", hit == "spell.blink", hit ?? "null");
        });
        SelfTest.Add(28.5f, "perks: shot spell book", () => TestKit.Screenshot("13-perks-spellbook"));
        SelfTest.Add(29f, "perks: respec with Untainted Heart", Respec);
        SelfTest.Add(30f, "perks: close", () => Panel.Close());
    }

    private static void LevelUp()
    {
        var save = new Core.Saving.PlayerSave { Level = 1, Xp = 0 };
        PlayerState.FromSave(save);
        Panel.ShowTab(PerkCategory.MeleeOffense.ToString());
        SelfTest.Check("perks: starts with no perks", Pc.Perks.SpentPoints == 0 && Pc.Perks.Bought.Count == 0);
        int guard = 0;
        while (Pc.Progress.Level < 30 && guard++ < 100) Pc.AddXp(XpCurve.Goal(Pc.Progress.Level));
        int avail = Pc.Perks.AvailablePoints(Pc.Progress);
        SelfTest.Check("perks: level up gives points", Pc.Progress.Level >= 30 && avail >= 30, $"level {Pc.Progress.Level}, points {avail}");
    }

    private static void OpenPanel()
    {
        Panel.Open();
        SelfTest.Check("perks: panel open", Panel.IsOpen);
        SelfTest.Check("perks: all perks laid out", Panel.NodeIds.Count == ModContent.Db.Perks.Count, $"{Panel.NodeIds.Count} nodes, {ModContent.Db.Perks.Count} perks");
        SelfTest.Check("perks: points counter", Panel.PointsText.Contains("30"), Panel.PointsText);
    }

    private static void BuyPerks()
    {
        // Walk the melee tree: buy whatever is available and has a data effect, 8 perks, checking each one's stat moves.
        int bought = 0, moved = 0;
        for (int round = 0; round < 12 && bought < 8; round++)
        {
            var next = ModContent.Db.Perks.Values
                .Where(p => p.Category == PerkCategory.MeleeOffense && p.Effects.Count > 0 && Pc.Perks.CanBuy(p.Id, Pc.Progress) == BuyResult.Ok)
                .OrderBy(p => p.Y).ThenBy(p => p.X).FirstOrDefault();
            if (next == null) break;
            var stat = next.Effects[0].Stat;
            double before = Pc.Stats.Get(stat);
            var r = Panel.Buy(next.Id);
            double after = Pc.Stats.Get(stat);
            bought++;
            bool ok = r == BuyResult.Ok && Math.Abs(after - before) > 1e-9;
            if (ok) moved++;
            Bought.Add(next.Id);
            SelfTest.Check($"perks: buy {next.Id}", ok, $"{r}; {stat} {before:0.####} -> {after:0.####}");
        }
        SelfTest.Check("perks: bought several", bought >= 5 && moved == bought, $"{moved}/{bought}");
        SelfTest.Check("perks: save has the perks", Pc.ToSave().Perks.Count == Bought.Distinct().Count());

        // A locked perk can't be bought.
        var locked = ModContent.Db.Perks.Values.FirstOrDefault(p => p.UnlockPath.Count > 0 && Pc.Perks.TimesBought(p.Id) == 0 && p.UnlockPath.All(u => Pc.Perks.TimesBought(u) == 0));
        if (locked != null) SelfTest.Check("perks: locked perk refused", Panel.Buy(locked.Id) == BuyResult.PathLocked);
    }

    /// <summary>Holding on an available node fills it and buys at <see cref="PerkPanel.HoldSeconds"/>; a held button
    /// does not buy again until released; locked perks never fill.</summary>
    private static void HoldToBuy()
    {
        var p = ModContent.Db.Perks.Values.Where(x => Pc.Perks.CanBuy(x.Id, Pc.Progress) == BuyResult.Ok)
            .OrderByDescending(x => x.Stackable).ThenBy(x => x.Category).ThenBy(x => x.Y).First();
        Panel.ShowTab(p.Category.ToString());
        int pointsBefore = Pc.Perks.AvailablePoints(Pc.Progress), times = Pc.Perks.TimesBought(p.Id);
        float half = PerkPanel.HoldSeconds / 2;
        var r1 = Panel.HoldStep(p.Id, true, half);
        var hold = Panel.Hold;
        SelfTest.Check("perks: a short hold only fills the node", r1 == null && hold.Id == p.Id && hold.Fraction > 0.4f && Pc.Perks.TimesBought(p.Id) == times, $"{r1}, {hold}");
        var r2 = Panel.HoldStep(p.Id, true, half + 0.01f);
        SelfTest.Check("perks: holding long enough buys", r2 == BuyResult.Ok && Pc.Perks.TimesBought(p.Id) == times + 1 && Pc.Perks.AvailablePoints(Pc.Progress) == pointsBefore - p.Cost,
            $"{p.Id}: {r2}, x{Pc.Perks.TimesBought(p.Id)}");
        var r3 = Panel.HoldStep(p.Id, true, PerkPanel.HoldSeconds * 2);
        SelfTest.Check("perks: keeping the button held does not buy again", r3 == null && Pc.Perks.TimesBought(p.Id) == times + 1, $"{r3}");
        Panel.HoldStep(p.Id, false, 0);
        Bought.Add(p.Id);
        var locked = ModContent.Db.Perks.Values.FirstOrDefault(x => Pc.Perks.CanBuy(x.Id, Pc.Progress) == BuyResult.PathLocked);
        if (locked != null)
        {
            var r4 = Panel.HoldStep(locked.Id, true, PerkPanel.HoldSeconds * 2);
            SelfTest.Check("perks: holding a locked perk does nothing", r4 == null && Panel.Hold.Id == null, $"{r4}");
            Panel.HoldStep(locked.Id, false, 0);
        }
        Panel.ShowTab(PerkCategory.MeleeOffense.ToString());
    }

    private static void StatesAndHover()
    {
        var states = ModContent.Db.Perks.Keys.Select(Panel.StateOf).Distinct().ToList();
        SelfTest.Check("perks: bought, available and locked states exist",
            states.Contains(PerkState.Bought) && states.Contains(PerkState.Available) && states.Contains(PerkState.Locked), string.Join(",", states));

        var id = Bought[0];
        var screen = Panel.ScreenPointOfPerk(id);
        var hit = Panel.RaycastHover(screen);
        SelfTest.Check("perks: pointer hit-test finds the node", hit == id, $"{hit} at {screen}");
        Panel.SimulateHover(id);
        SelfTest.Check("perks: detail shows name, flavor, effect",
            Panel.DetailName.Length > 0 && Panel.DetailFlavor.Length > 4 && Panel.DetailEffect.Length > 0,
            $"{Panel.DetailName} | {Panel.DetailEffect}");
        Panel.ReleaseSimulatedHover();
        if (Bought.Count > 3) Panel.SimulateHover(Bought[3]);
    }

    private static void Tabs()
    {
        Panel.ReleaseSimulatedHover();
        Panel.ShowTab(PerkCategory.MagicOffense.ToString());
        // Buy a couple of magic perks so the screenshot has all states.
        for (int i = 0; i < 4; i++)
        {
            var next = ModContent.Db.Perks.Values.Where(p => p.Category == PerkCategory.MagicOffense && Pc.Perks.CanBuy(p.Id, Pc.Progress) == BuyResult.Ok)
                .OrderBy(p => p.Y).ThenBy(p => p.X).FirstOrDefault();
            if (next != null) Panel.Buy(next.Id);
        }
        Panel.SimulateHover("perk.stronger_spells");
        foreach (var cat in new[] { PerkCategory.RangedOffense, PerkCategory.Defense, PerkCategory.Support, PerkCategory.Utility, PerkCategory.MagicOffense })
        {
            Panel.ShowTab(cat.ToString());
            SelfTest.Check($"perks: tab {cat} shows", Panel.CurrentTab == cat.ToString());
        }
    }

    private static void SpellBookTab()
    {
        Panel.ReleaseSimulatedHover();
        Panel.ShowTab(PerkPanel.SpellTab);
        var r = Panel.UnlockSpell("spell.blink");
        SelfTest.Check("perks: unlock Blink in the spell book", r == BuyResult.Ok && Pc.Perks.UnlockedSpells.Contains("spell.blink"), r.ToString());
        SelfTest.Check("perks: slot Blink", Panel.SlotSpell("spell.blink", 0) && Pc.Spells.Loadout[0] == "spell.blink");
        Panel.UnlockSpell("spell.healing_dome");
        Panel.SlotSpell("spell.healing_dome", 1);
        Panel.SimulateHover("spell.blink");
        SelfTest.Check("perks: spell detail has description", Panel.DetailEffect.Contains("Teleport"), Panel.DetailEffect);
    }

    private static void Respec()
    {
        Panel.ReleaseSimulatedHover();
        Panel.ShowTab(PerkCategory.MeleeOffense.ToString());
        int spent = Pc.Perks.SpentPoints;
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>()); // leveling now puts the reward hearts (COS-43) in the bag
        SelfTest.Check("perks: respec refused without a heart", !Panel.TryRespec() && Pc.Perks.SpentPoints == spent);
        TestKit.Give(PerkPanel.HeartOfPurity, 4, 1);
        SelfTest.Check("perks: heart in bag", PanelKit.Count(Pc.Inventory, PerkPanel.HeartOfPurity) == 1);
        double before = Pc.Stats.Get(ModContent.Db.Perks[Bought[0]].Effects[0].Stat);
        string first = Panel.RespecClick();
        SelfTest.Check("perks: the first Respec click only asks to confirm", first == "armed" && Panel.RespecArmed && Pc.Perks.SpentPoints == spent, first);
        bool ok = Panel.RespecClick() == "done";
        double after = Pc.Stats.Get(ModContent.Db.Perks[Bought[0]].Effects[0].Stat);
        SelfTest.Check("perks: respec refunds everything", ok && Pc.Perks.SpentPoints == 0 && Pc.Perks.Bought.Count == 0 && Pc.Spells.Loadout.All(s => s == null),
            $"spent {Pc.Perks.SpentPoints}");
        SelfTest.Check("perks: respec consumed the heart and reverted stats", PanelKit.Count(Pc.Inventory, PerkPanel.HeartOfPurity) == 0 && Math.Abs(after - before) > 1e-9,
            $"{before:0.####} -> {after:0.####}");
    }
}
