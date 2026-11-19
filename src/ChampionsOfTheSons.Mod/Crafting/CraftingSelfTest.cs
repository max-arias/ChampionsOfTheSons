using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Mod.Crafting;

/// <summary>Crafting panel checks: all five modes reachable and each one run from the panel on bag items.</summary>
internal static class CraftingSelfTest
{
    private static CraftingPanel Panel => CraftingFeature.Panel;
    private static PlayerCharacter Pc => PlayerState.Character;
    private static string _baseType = "";
    private static int _rarity;

    public static void Register()
    {
        SelfTest.Add(32f, "craft: open, all modes reachable", OpenAndModes);
        SelfTest.Add(33f, "craft: reroll", () => Run(CraftMode.Reroll));
        SelfTest.Add(34f, "craft: reforge", () => Run(CraftMode.Reforge));
        SelfTest.Add(35f, "craft: polish", () => Run(CraftMode.Polish));
        SelfTest.Add(36f, "craft: empower", () => Run(CraftMode.Empower));
        SelfTest.Add(37f, "craft: reroll one stat", () => Run(CraftMode.IndividualReroll));
        SelfTest.Add(38f, "craft: refusals", Refusals);
        SelfTest.Add(39f, "craft: stage for screenshot", StageShot);
        SelfTest.Add(40.5f, "craft: shot", () => TestKit.Screenshot("20-crafting"));
        SelfTest.Add(41f, "craft: close", () => { Panel.ReleaseSimulatedHover(); Panel.Close(); });
    }

    private static void OpenAndModes()
    {
        Panel.Open();
        SelfTest.Check("craft: panel open", Panel.IsOpen);
        foreach (var m in CraftingPanel.Modes)
            SelfTest.Check($"craft: mode button {m} visible", Panel.ModeButtonVisible(m));
        SelfTest.Check("craft: five modes", CraftingPanel.Modes.Length == Enum.GetValues<CraftMode>().Length);

        // A (type, rarity) with at least two equippable bases that carry stats, so Reforge has an alternative.
        var groups = ModContent.Db.Items.Values
            .Where(d => LootRoller.IsEquippable(d) && d.StatPools.Count >= 2 && d.Rarity >= 3 && d.Rarity <= 6)
            .GroupBy(d => (d.Type, d.Rarity)).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count()).First();
        _baseType = groups.Key.Type;
        _rarity = groups.Key.Rarity;
        SelfTest.Log($"craft: using type {_baseType} rarity {_rarity}");
    }

    private static string? Seed(CraftMode mode, int targetLevel, out string targetKey, out List<string> ingredientKeys)
    {
        var inv = Pc.Inventory;
        inv.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        var baseDef = ModContent.Db.Items.Values.Where(d => d.Type == _baseType && d.Rarity == _rarity && d.StatPools.Count >= 2).First();
        TestKit.Give(baseDef.Id, _rarity, targetLevel);
        targetKey = Inventory.Bag(0);
        int n = CraftMode_Count(mode);
        ingredientKeys = new List<string>();
        for (int i = 0; i < n; i++)
        {
            TestKit.Give(baseDef.Id, _rarity, 10);
            ingredientKeys.Add(Inventory.Bag(i + 1));
        }
        return null;
    }

    private static int CraftMode_Count(CraftMode m) => Core.Rules.Crafting.IngredientCount(m);

    private static void Run(CraftMode mode)
    {
        Seed(mode, 20, out var targetKey, out var ingredients);
        var before = Pc.Inventory.Get(targetKey)!;
        int bagBefore = Pc.Inventory.Slots.Count;

        Panel.SetMode(mode);
        Panel.Clear();
        Panel.ClickBag(targetKey);
        SelfTest.Check($"craft {mode}: target picked", Panel.Target == targetKey);
        foreach (var k in ingredients) Panel.ClickBag(k);
        SelfTest.Check($"craft {mode}: ingredients picked", Panel.Picked.Count == ingredients.Count, $"{Panel.Picked.Count}/{ingredients.Count}");

        var result = mode switch
        {
            CraftMode.Polish => PickAndCraft(before, i => i >= 0),
            CraftMode.IndividualReroll => PickAndCraft(before, _ => true),
            _ => Panel.DoCraft(),
        };
        var after = Pc.Inventory.Get(targetKey);
        SelfTest.Check($"craft {mode}: crafted", result is { Ok: true } && after != null, result == null ? "blocked: " + Panel.Blocker() : result.Error.ToString());
        if (after == null || result is not { Ok: true }) return;

        int consumed = bagBefore - Pc.Inventory.Slots.Count;
        SelfTest.Check($"craft {mode}: ingredients consumed", consumed == ingredients.Count, $"{consumed} of {ingredients.Count}");
        switch (mode)
        {
            case CraftMode.Reroll:
                SelfTest.Check("craft Reroll: same item and level, new stats", after.BaseId == before.BaseId && after.Level == before.Level && after.Rarity == before.Rarity);
                break;
            case CraftMode.Reforge:
            {
                // Same type; same rarity when another item of it exists, otherwise the nearest rarity (Crafting.cs, Reforge).
                var others = ModContent.Db.Items.Values.Where(d => d.Id != before.BaseId && d.Type == _baseType && d.MinLevel <= Math.Max(1, before.Level)).ToList();
                int nearest = others.Count == 0 ? 0 : others.Min(d => Math.Abs(d.Rarity - (int)before.Rarity));
                var got = ModContent.Db.Items[after.BaseId];
                SelfTest.Check("craft Reforge: different item, same type, nearest rarity",
                    after.BaseId != before.BaseId && got.Type == _baseType && Math.Abs(got.Rarity - (int)before.Rarity) == nearest,
                    $"{before.BaseId} -> {after.BaseId} (rarity {(int)before.Rarity} -> {got.Rarity}, nearest gap {nearest})");
            }
                break;
            case CraftMode.Polish:
            {
                int idx = Panel.LastStatIndex;
                bool same = Enumerable.Range(0, before.Stats.Count).Where(i => i != idx).All(i => before.Stats[i] == after.Stats[i]);
                SelfTest.Check("craft Polish: only the picked stat can change", after.Stats.Count == before.Stats.Count && after.Stats[idx].StatId == before.Stats[idx].StatId && same,
                    $"stat {idx}: {before.Stats[idx].Value:0.##} -> {after.Stats[idx].Value:0.##}");
                break;
            }
            case CraftMode.Empower:
                SelfTest.Check("craft Empower: raised to player level", after.Level == Pc.Progress.Level && after.Upgrades == before.Upgrades + 1, $"level {before.Level} -> {after.Level}");
                break;
            case CraftMode.IndividualReroll:
            {
                int idx = Panel.LastStatIndex;
                SelfTest.Check("craft Reroll one stat: that stat is a different stat", after.Stats[idx].StatId != before.Stats[idx].StatId, $"{before.Stats[idx].StatId} -> {after.Stats[idx].StatId}");
                break;
            }
        }
    }

    /// <summary>Picks stat rows one by one until the craft succeeds (some stats have no alternative to roll).</summary>
    private static CraftResult? PickAndCraft(ItemInstance target, Func<int, bool> allowed)
    {
        CraftResult? last = null;
        for (int i = 0; i < target.Stats.Count; i++)
        {
            if (!allowed(i)) continue;
            Panel.PickStat(i);
            last = Panel.DoCraft();
            if (last is { Ok: true }) return last;
        }
        return last;
    }

    private static void Refusals()
    {
        Seed(CraftMode.Reroll, 5, out var targetKey, out var ingredients);
        Panel.SetMode(CraftMode.Reroll);
        Panel.Clear();
        SelfTest.Check("craft: no target is refused", Panel.DoCraft() == null && Panel.Blocker() != null);
        Panel.ClickBag(targetKey);
        Panel.ClickBag(ingredients[0]);
        SelfTest.Check("craft: too few ingredients is refused", Panel.DoCraft() == null);
        // A lower-rarity item can't be an ingredient.
        TestKit.Give(ModContent.Db.Items.Values.First(d => d.Type == "material" && d.Rarity < _rarity).Id, 0, 1);
        string low = Pc.Inventory.Slots.First(kv => (int)kv.Value.Rarity < _rarity).Key;
        Panel.ClickBag(low);
        SelfTest.Check("craft: low rarity ingredient rejected", !Panel.Picked.Contains(low));
    }

    private static void StageShot()
    {
        Seed(CraftMode.Polish, 12, out var targetKey, out var ingredients);
        Panel.SetMode(CraftMode.Polish);
        Panel.Clear();
        Panel.ClickBag(targetKey);
        Panel.ClickBag(ingredients[0]);
        Panel.PickStat(1);
        var rare = TestKit.Give(null, 5, 20);
        Panel.Refresh();
        Panel.SimulateHover(Inventory.Bag(2));
    }
}
