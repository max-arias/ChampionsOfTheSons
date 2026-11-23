using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Book;

namespace ChampionsOfTheSons.Mod.Perks;

/// <summary>
/// The book's Perks, Spells and attribute pages (COS-168, COS-170, COS-174): buying through page clicks, the stat change, free
/// refunds of a tree, all perks and the spells, learning and slotting spells, spending and resetting attributes, the sub-tabs.
/// </summary>
internal static class PerksSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static PerkDef? _root;
    private static double _before;

    private static string Click(BookTab tab, string key, bool right = false) => BookPages.Click(tab, key, right);

    public static void Register()
    {
        SelfTest.Add(20f, "perks: fresh level 30 character", () =>
        {
            PlayerState.FromSave(new Core.Saving.PlayerSave { Level = 30 });
            TestKit.Unpause();
        });
        SelfTest.Add(20.5f, "perks: open the book on Perks", () =>
            SelfTest.Check("perks: the perk key's page opens", BookHost.Open(BookTab.Perks), BookHost.LastFailure));
        SelfTest.Add(23.5f, "perks: buy by page clicks", BuyByClicks);
        SelfTest.Add(24f, "perks: shot perks", () => TestKit.Screenshot("perks-01-melee"));
        SelfTest.Add(24.5f, "perks: refunds", Refunds);
        SelfTest.Add(25f, "perks: spells page", SpellsPage);
        SelfTest.Add(25.6f, "perks: shot spells", () => TestKit.Screenshot("perks-02-spells"));
        SelfTest.Add(26f, "perks: attributes", Attributes);
        SelfTest.Add(26.6f, "perks: shot attributes", () => TestKit.Screenshot("perks-03-attributes"));
        SelfTest.Add(27f, "perks: sub-tabs", SubTabs);
        // COS-169: one screenshot per tree shape, for a look by eye.
        for (int i = 0; i < PerkInfo.Trees.Length; i++)
        {
            var cat = PerkInfo.Trees[i];
            SelfTest.Add(28f + i * 0.8f, "perks: tree " + cat, () => { BookHost.ShowModPage(BookTab.Perks); BookPerksPage.ShowTree(cat); BookPages.Refresh(BookTab.Perks); });
            SelfTest.Add(28.4f + i * 0.8f, "perks: shot tree " + cat, () => TestKit.Screenshot("perks-tree-" + cat));
        }
        SelfTest.Add(33.5f, "perks: close", () => { if (BookHost.IsOpen) BookHost.Close(); });
    }

    private static void BuyByClicks()
    {
        SelfTest.Check("perks: the Perks page shows", BookHost.Tab == BookTab.Perks, BookHost.Tab.ToString());
        BookPerksPage.ShowTree(PerkCategory.MeleeOffense);
        BookPages.Refresh(BookTab.Perks);
        _root = ModContent.Db.Perks.Values.FirstOrDefault(p => p.Category == PerkCategory.MeleeOffense && p.UnlockPath.Count == 0 && p.Requires.Count == 0
            && p.LevelReq <= 30 && p.Cost <= Pc.Perks.AvailablePoints(Pc.Progress) && !p.Manual && p.Effects.Count > 0);
        if (_root == null) { SelfTest.Check("perks: a buyable melee root perk exists", false); return; }
        var stat = _root.Effects[0].Stat;
        _before = Pc.Stats.Get(stat);
        SelfTest.Check("perks: the perk is on the page", BookPages.CurrentHits.Any(h => h.Key == _root.Id));
        SelfTest.Check("perks: the first click selects", Click(BookTab.Perks, _root.Id) == "select" && Pc.Perks.TimesBought(_root.Id) == 0);
        string second = Click(BookTab.Perks, _root.Id);
        SelfTest.Check("perks: the second click buys and the stat changes", second == "bought" && Pc.Perks.TimesBought(_root.Id) == 1 && Pc.Stats.Get(stat) != _before,
            $"{second}; {stat} {_before} -> {Pc.Stats.Get(stat)}");
        var locked = ModContent.Db.Perks.Values.FirstOrDefault(p => p.Category == PerkCategory.MeleeOffense && p.UnlockPath.Count > 0
            && p.UnlockPath.All(x => Pc.Perks.TimesBought(x) == 0));
        if (locked != null)
            SelfTest.Check("perks: a perk behind its path is refused", Click(BookTab.Perks, locked.Id, right: true).StartsWith("refused", StringComparison.Ordinal)
                && Pc.Perks.TimesBought(locked.Id) == 0);
        BookPages.Refresh(BookTab.Perks);
    }

    private static void Refunds()
    {
        if (_root == null) return;
        BookPerksPage.ShowTree(PerkCategory.MeleeOffense);
        SelfTest.Check("perks: the first refund click only arms it", Click(BookTab.Perks, "perks.refund_tree") == "armed" && Pc.Perks.TimesBought(_root.Id) == 1);
        string done = Click(BookTab.Perks, "perks.refund_tree");
        SelfTest.Check("perks: the second click refunds the tree for free", done.StartsWith("refunded", StringComparison.Ordinal) && Pc.Perks.TimesBought(_root.Id) == 0
            && Math.Abs(Pc.Stats.Get(_root.Effects[0].Stat) - _before) < 1e-9, done);
        Click(BookTab.Perks, _root.Id, right: true);
        Click(BookTab.Perks, "perks.refund_all");
        Click(BookTab.Perks, "perks.refund_all");
        SelfTest.Check("perks: refund all clears every perk", Pc.Perks.Bought.Count == 0);
    }

    private static void SpellsPage()
    {
        BookHost.ShowModPage(BookTab.Spells);
        BookPages.Refresh(BookTab.Spells);
        SelfTest.Check("perks: the Spells page shows", BookHost.Tab == BookTab.Spells);
        SelfTest.Check("perks: learn a spell", Click(BookTab.Spells, "spell.learn.spell.blink") == "learned" && Pc.Perks.UnlockedSpells.Contains("spell.blink"));
        SelfTest.Check("perks: slot it", Click(BookTab.Spells, "spell.slot.2.spell.blink") == "slotted" && Pc.Spells.Loadout[2] == "spell.blink");
        SelfTest.Check("perks: the same slot takes it off", Click(BookTab.Spells, "spell.slot.2.spell.blink") == "unslotted" && Pc.Spells.Loadout[2] == null);
        Click(BookTab.Spells, "spell.slot.0.spell.blink");
        var dear = ModContent.Db.Spells.Values.OrderByDescending(s => s.EnergyCost).First();
        SelfTest.Check("perks: a spell above max energy is flagged (COS-172)", PerkInfo.TooDear(dear), $"{dear.Id} {dear.EnergyCost} vs {Spells.SpellCaster.MaxEnergy:0}");
        Click(BookTab.Spells, "spells.refund");
        string done = Click(BookTab.Spells, "spells.refund");
        SelfTest.Check("perks: refund spells unlearns and clears the bar", done == "refunded 1" && Pc.Perks.UnlockedSpells.Count == 0 && Pc.Spells.Loadout.All(s => s == null), done);
        Click(BookTab.Spells, "spell.learn.spell.blink");
        // COS-176: spell mastery. Casting Black Hole five times reaches mastery 1 and grants its first upgrade, for free.
        Pc.SetMastery(null, 0);
        int points = Pc.Perks.AvailablePoints(Pc.Progress);
        var hole = ModContent.Db.Spells["spell.black_hole"];
        int? up = null;
        for (int i = 0; i < SpellMastery.Thresholds[0]; i++) up = Pc.RecordCast(hole.Id) ?? up;
        SelfTest.Check("perks: five casts reach mastery 1", up == 1 && Pc.Mastery.Level(hole.Id) == 1, $"{up}, level {Pc.Mastery.Level(hole.Id)}");
        SelfTest.Check("perks: mastery 1 grants the spell's first upgrade for free", hole.Mastery.Count > 0 && Pc.Perks.TimesBought(hole.Mastery[0]) == 1
            && Pc.Perks.AvailablePoints(Pc.Progress) == points, string.Join(",", hole.Mastery));
        SelfTest.Check("perks: the mastery message names the upgrade", PerkInfo.MasteryUpText(hole.Id, 1).Contains(PerkInfo.Name(hole.Mastery[0], "")));
        Pc.SetMastery("spell.blink", 3);
        BookPages.Refresh(BookTab.Spells);
    }

    private static void Attributes()
    {
        BookHost.ShowModPage(BookTab.Character);
        while (BookPages.CharPage != 0) BookPages.Page(BookTab.Character, 1);
        BookPages.Refresh(BookTab.Character);
        int left = Pc.AttributePointsLeft;
        double hp = Derived.MaxHealth(Pc.Stats);
        SelfTest.Check("perks: attribute points to spend at level 30", left == AttributeRules.TotalPoints(30), left.ToString());
        SelfTest.Check("perks: + spends one", Click(BookTab.Character, "attr.plus." + StatIds.Vitality) == "spent 1" && Pc.Attributes.Get(StatIds.Vitality) == 1);
        SelfTest.Check("perks: right-click + spends ten", Click(BookTab.Character, "attr.plus." + StatIds.Vitality, right: true) == "spent 10");
        SelfTest.Check("perks: Vitality raises max health", Derived.MaxHealth(Pc.Stats) > hp, $"{hp:0.#} -> {Derived.MaxHealth(Pc.Stats):0.#}");
        SelfTest.Check("perks: - takes one back", Click(BookTab.Character, "attr.minus." + StatIds.Vitality) == "refunded 1" && Pc.Attributes.Get(StatIds.Vitality) == 10);
        Click(BookTab.Character, "attr.reset");
        SelfTest.Check("perks: reset is free and gives every point back", Click(BookTab.Character, "attr.reset") == "reset" && Pc.AttributePointsLeft == left);
        Pc.SpendAttribute(StatIds.Strength, 5);
        BookPages.Refresh(BookTab.Character);
    }

    private static void SubTabs()
    {
        foreach (var tab in new[] { BookTab.Character, BookTab.Bag, BookTab.Spells, BookTab.Perks, BookTab.Help })
        {
            var at = BookTabs.SubScreenCenter(tab);
            var hit = BookTabs.HitTest(at);
            SelfTest.Check($"perks: the {tab} sub-tab is under its own center", hit == tab, $"{at} -> {hit}");
            BookFeature.Handle(BookInput.MouseAt(at, down: true));
            BookFeature.Handle(BookInput.MouseAt(at, up: true));
            SelfTest.Check($"perks: clicking the {tab} sub-tab opens its page", BookHost.Tab == (tab == BookTab.Bag ? BookTab.Bag : tab), BookHost.Tab.ToString());
        }
    }
}
