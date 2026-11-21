using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using Sons.Ai.Vail;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Loot;

/// <summary>In-game checks for drops, pickup and the inventory actions (run with --cots-selftest).</summary>
internal static class LootSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static GroundLoot Ground => LootFeature.Ground;

    private static WorldRules? _savedRules;
    private static Vector3 _home;
    private static VailActor? _victim;
    private static int _target;
    private static int _bagBefore;

    private const float T0 = 20;

    public static void Register()
    {
        SelfTest.Add(T0, "loot: setup", Setup);
        SelfTest.Add(T0 + 0.5f, "loot: kill drops", KillForDrops);
        SelfTest.Add(T0 + 4, "loot: kill result", KillResult);
        SelfTest.Add(T0 + 5, "loot: boss drops", BossDrops);
        SelfTest.Add(T0 + 6.5f, "loot: shot drops", () => TestKit.Screenshot("loot-01-drops"));
        SelfTest.Add(T0 + 8, "loot: walk over", WalkOver);
        SelfTest.Add(T0 + 10, "loot: walk over result", WalkOverResult);
        SelfTest.Add(T0 + 11, "loot: full bag", FullBag);
        SelfTest.Add(T0 + 13, "loot: full bag result", FullBagResult);
        SelfTest.Add(T0 + 14, "loot: free slot", FreeSlot);
        SelfTest.Add(T0 + 16, "loot: free slot result", FreeSlotResult);
        SelfTest.Add(T0 + 17, "loot: inventory drop", InventoryDrop);
        SelfTest.Add(T0 + 18.5f, "loot: dropped item waits", DroppedWaits);
        SelfTest.Add(T0 + 19, "loot: step away", StepAway);
        SelfTest.Add(T0 + 20, "loot: step back", StepBack);
        SelfTest.Add(T0 + 21.5f, "loot: pick up again", PickUpAgain);
        SelfTest.Add(T0 + 27f, "loot: level 25 heart, Use on the book card", UseHeart);
        SelfTest.Add(T0 + 27.6f, "loot: stack actions: Split, Drop 1, Drop all", StackMenu);
        SelfTest.Add(T0 + 27.8f, "loot: stat lines by stat rarity", StatOrder);
        SelfTest.Add(T0 + 28.4f, "loot: cleanup", Cleanup);
    }

    private static void Teleport(Vector3 p)
    {
        LocalPlayer.Transform.position = p;
        Physics.SyncTransforms();
    }

    private static Vector3 Player => LocalPlayer.Transform.position;

    private static Rarity? ColorRarity(Color c)
    {
        foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
        {
            var rc = LootVisuals.ColorOf(r);
            if (Mathf.Abs(rc.r - c.r) + Mathf.Abs(rc.g - c.g) + Mathf.Abs(rc.b - c.b) < 0.01f) return r;
        }
        return null;
    }

    private static void Setup()
    {
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        LootFeature.ClearAll();
        _home = Player;
        _savedRules = WorldState.Rules;
        SelfTest.Check("loot: ground empty at start", Ground.Count == 0 && LootVisuals.All.Count == 0);
    }

    private static void KillForDrops()
    {
        // Force the drop roll: the host's drop chance multiplier is part of the world rules.
        WorldState.SetRulesForTest(_savedRules! with { DropChance = 1000, DropQuantity = 3, AllowElites = false });
        _victim = TestKit.Spawn(distance: 7);
        SelfTest.Check("loot: victim spawned", _victim != null);
        if (_victim == null) return;
        TestKit.Kill(_victim);
    }

    private static void KillResult()
    {
        WorldState.SetRulesForTest(_savedRules!);
        SelfTest.Check("loot: victim died", _victim == null || _victim.IsDead());
        SelfTest.Check("loot: kill dropped items", Ground.Count >= 2, $"{Ground.Count} drops");
        CheckVisuals("kill");
        var near = Ground.All.Select(d => Vector3.Distance(Player, new Vector3(d.X, d.Y, d.Z))).DefaultIfEmpty(0).Min();
        SelfTest.Check("loot: kill drops lie away from the player", near > LootFeature.PickupRadius + 1, $"nearest {near:0.0} m");
        SelfTest.Log("loot: drops " + string.Join(", ", Ground.All.Select(d => $"{d.Item.BaseId}/{d.Item.Rarity}")));
    }

    private static void CheckVisuals(string what)
    {
        SelfTest.Check($"loot: {what}: one world object per drop", LootVisuals.All.Count == Ground.Count, $"{LootVisuals.All.Count}/{Ground.Count}");
        var bad = Ground.All.Where(d => !LootVisuals.All.TryGetValue(d.Id, out var v) || ColorRarity(v.Color) != d.Item.Rarity).Select(d => d.Item.Rarity.ToString()).ToList();
        SelfTest.Check($"loot: {what}: ring color matches rarity", bad.Count == 0, string.Join(",", bad));
        var look = Ground.All.Where(d => !LootVisuals.All.TryGetValue(d.Id, out var v) || v.Beam == null || v.HasRing != (d.Item.Rarity >= Rarity.Rare))
            .Select(d => d.Item.Rarity.ToString()).ToList();
        SelfTest.Check($"loot: {what}: every drop has a beam, only Rare and Legendary a ring", look.Count == 0, string.Join(",", look));
        bool blink = LootVisuals.BlinkOn(LootVisuals.BlinkSeconds + 1, 0.9f) && !LootVisuals.BlinkOn(1, 0.1f) && LootVisuals.BlinkOn(1, 0.03f);
        SelfTest.Check($"loot: {what}: shown steady until the blink, then blinking fast near the end", blink);
    }

    private static void BossDrops()
    {
        // A synthetic level-300 boss on Hell: many drops, high rarities.
        var rules = _savedRules! with { PartyLevelOverride = Difficulties.Hell.BaseLevelMin, DropChance = 1000 };
        var boss = new EnemyProfile
        {
            KindId = "test", Level = 300, Tier = EnemyTier.Boss, Bounty = 5e7,
            Abilities = new[] { EnemyAbility.Gargantuan, EnemyAbility.ExtraHealth, EnemyAbility.ExtraDamage, EnemyAbility.Blizzard },
        };
        var items = LootFeature.RollForKill(boss, rules);
        SelfTest.Check("loot: a boss drops its ten items and the XP heart", items.Count >= 11, $"{items.Count}");
        SelfTest.Check("loot: a boss drops an XP heart (COS-123)", items.Any(i => i.BaseId == LootRoller.XpHeart));
        int maxRarity = items.Select(i => (int)i.Rarity).DefaultIfEmpty(0).Max();
        SelfTest.Check("loot: boss drops reach high rarity", maxRarity >= (int)Rarity.Rare, $"max rarity {maxRarity}");
        SelfTest.Check("loot: item level follows the player", items.All(i => ModContent.Db.Items.TryGetValue(i.BaseId, out var d) && LootRoller.IsMaterialOrOther(d) || i.Level == Pc.Progress.Level),
            $"player level {Pc.Progress.Level}");

        // One of each rarity in a row in front of the player, for the screenshot and the color check.
        var t = LocalPlayer.Transform;
        var roller = new LootRoller(ModContent.Db);
        var rng = new Rng(77);
        int n = 0;
        for (int r = 0; r <= Rarities.Max; r++)
        {
            var def = roller.PickBase(r, 100, rng, d => !LootRoller.IsMaterialOrOther(d) && d.Rarity == r) ?? roller.PickBase(r, 100, rng);
            if (def == null) continue;
            var pos = t.position + t.forward * 5 + t.right * (-3f + 2f * r);
            LootFeature.SpawnAt(roller.RollItem(def, 30, rng), pos);
            n++;
        }
        SelfTest.Check("loot: spawned a row of rarities", n == Rarities.Max + 1, $"{n}");
        CheckVisuals("row");
        var colors = LootVisuals.All.Values.Select(v => ColorRarity(v.Color)).Distinct().Count();
        SelfTest.Check("loot: several rarity colors present", colors >= 3, $"{colors}");
    }

    private static void WalkOver()
    {
        var d = Ground.All.OrderBy(x => x.Id).First();
        _target = d.Id;
        _bagBefore = Pc.Inventory.Slots.Count;
        Teleport(new Vector3(d.X, d.Y, d.Z));
    }

    private static void WalkOverResult()
    {
        SelfTest.Check("loot: walking onto a drop picks it up", Ground.Get(_target) == null && Pc.Inventory.Slots.Count > _bagBefore,
            $"bag {_bagBefore} -> {Pc.Inventory.Slots.Count}, drop left: {Ground.Get(_target) != null}");
        Teleport(_home);
    }

    private static string _targetBase = "";

    private static void FullBag()
    {
        Teleport(_home);
        int i = 0;
        while (Pc.Inventory.AddToBag(new ItemInstance { BaseId = "item.filler" + i++, Level = 1 })) { }
        SelfTest.Check("loot: bag filled", Pc.Inventory.Slots.Count(kv => kv.Key.StartsWith("bag.")) == LootRoller.BagSlots);
        var item = new ItemInstance { BaseId = ModContent.Db.Items.Keys.First(), Level = 5 };
        var drop = LootFeature.SpawnAt(item, Player);
        _target = drop.Id;
        _targetBase = item.BaseId;
    }

    private static void FullBagResult()
    {
        SelfTest.Check("loot: full bag leaves the drop on the ground", Ground.Get(_target) != null);
        SelfTest.Check("loot: full bag shows a message", LootFeature.LastMessage == UiText.T("ui.loot.bag_full", "Your bag is full."), LootFeature.LastMessage ?? "none");
    }

    private static void FreeSlot()
    {
        Pc.Inventory.Remove("bag.0");
    }

    private static void FreeSlotResult()
    {
        // The item may go to a free equipment slot (a quiver) instead of the freed bag slot.
        SelfTest.Check("loot: drop is picked up once a slot is free", Ground.Get(_target) == null && Pc.Inventory.Slots.Values.Any(i => i.BaseId == _targetBase), _targetBase);
        LootFeature.ClearAll();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
    }

    private static void InventoryDrop()
    {
        var item = TestKit.Give(rarity: 4);
        var slot = Pc.Inventory.Slots.First(kv => kv.Value.BaseId == item.BaseId).Key;
        int before = Ground.Count;
        var drop = LootFeature.DropFromInventory(slot);
        SelfTest.Check("loot: inventory drop creates a world drop", drop != null && Ground.Count == before + 1 && Pc.Inventory.Get(slot) == null);
        SelfTest.Check("loot: dropped item has a world object", drop != null && LootVisuals.All.ContainsKey(drop.Id));
        _target = drop?.Id ?? -1;
    }

    private static void DroppedWaits()
    {
        SelfTest.Check("loot: dropped item is not grabbed again at once", Ground.Get(_target) != null);
    }

    private static void StepAway()
    {
        Teleport(_home + new Vector3(0, 0, -5));
    }

    private static void StepBack()
    {
        var d = Ground.Get(_target);
        if (d == null) { SelfTest.Check("loot: dropped item still there", false); return; }
        Teleport(new Vector3(d.X, d.Y, d.Z));
    }

    private static void PickUpAgain()
    {
        SelfTest.Check("loot: dropped item can be picked up again", Ground.Get(_target) == null && Pc.Inventory.Slots.Count == 1);
        Teleport(_home);
        LootFeature.ClearAll();
    }

    // ---------------------------------------------------------------- inventory actions

    /// <summary>COS-72: level 24 -> 25 gives the bonus-point heart; the menu offers Use on it and using it adds one permanent point.</summary>
    private static void UseHeart()
    {
        var original = Pc.ToSave();
        try
        {
            var start = Pc.ToSave();
            start.Level = 24;
            start.Xp = 0;
            start.Slots.Clear();
            Pc.Load(start);
            int bonus = Pc.Progress.BonusPerkPoints, points = Pc.Perks.AvailablePoints(Pc.Progress);
            Pc.AddXp(XpCurve.Goal(24) / Math.Max(1e-9, Pc.Stats.Get(StatIds.ExpGain)) + 1);
            var heart = Pc.Inventory.Slots.FirstOrDefault(kv => kv.Value.BaseId == Progress.GreaterMutatedHeart).Key;
            SelfTest.Check("loot: level 25 gives the bonus-point heart", Pc.Progress.Level == 25 && heart != null, $"level {Pc.Progress.Level}");
            if (heart == null) return;
            SelfTest.Check("loot: the card offers Use on the heart", InventoryActions.IsUsable(heart));
            bool used = Book.BookCard.Act("equip", heart) == "used";
            SelfTest.Check("loot: using the heart adds one permanent perk point",
                used && Pc.Progress.BonusPerkPoints == bonus + 1 && Pc.Perks.AvailablePoints(Pc.Progress) == points + 2,
                $"bonus {bonus} -> {Pc.Progress.BonusPerkPoints}, points {points} -> {Pc.Perks.AvailablePoints(Pc.Progress)}");
            SelfTest.Check("loot: the used heart is gone", Pc.Inventory.Get(heart) == null);
        }
        finally { Pc.Load(original); }
    }

    /// <summary>COS-96: a stack gets Drop 1 and Split stack on the book card, and its Drop reads "Drop all".</summary>
    private static void StackMenu()
    {
        var heart = new ItemInstance { BaseId = "item.heart_of_purity", Level = 1, Amount = 6 };
        Pc.Inventory.Restore(new[] { new KeyValuePair<string, ItemInstance>("bag.0", heart) });
        SelfTest.Check("loot: a stack gets the stack actions, and Drop all", InventoryActions.IsStack("bag.0")
            && InventoryActions.DropLabel("bag.0") == UiText.T("ui.inventory.menu.drop_all", "Drop all"), InventoryActions.DropLabel("bag.0"));
        string split = Book.BookCard.Act("split", "bag.0");
        var halves = Pc.Inventory.Slots.Where(kv => kv.Value.BaseId == heart.BaseId).Select(kv => kv.Value.Amount).OrderBy(a => a).ToList();
        SelfTest.Check("loot: Split stack puts half in a free slot", split == "split" && halves.SequenceEqual(new[] { 3, 3 }), $"{split}: {string.Join("+", halves)}");
        int drops = Ground.Count;
        Book.BookCard.Act("drop_one", "bag.0");
        var dropped = Ground.All.OrderByDescending(d => d.Id).FirstOrDefault();
        SelfTest.Check("loot: Drop 1 drops one and keeps the rest", Ground.Count == drops + 1 && dropped?.Item.Amount == 1 && Pc.Inventory.Get("bag.0")?.Amount == 2,
            $"ground {drops} -> {Ground.Count}, dropped {dropped?.Item.Amount}, left {Pc.Inventory.Get("bag.0")?.Amount}");
        Pc.Inventory.Restore(new[] { new KeyValuePair<string, ItemInstance>("bag.0", heart with { Amount = 1 }) });
        SelfTest.Check("loot: a single item has no stack actions", !InventoryActions.IsStack("bag.0")
            && InventoryActions.DropLabel("bag.0") == UiText.T("ui.inventory.menu.drop", "Drop"), InventoryActions.DropLabel("bag.0"));
    }

    /// <summary>COS-96: stat lines sort by the stat's own rarity, highest first.</summary>
    private static void StatOrder()
    {
        var stats = ModContent.Db.ItemStats.Values.Where(d => !LootRoller.IsSocketStat(d.Id)).ToList();
        var low = stats.OrderBy(d => d.Rarity).First();
        var high = stats.OrderByDescending(d => d.Rarity).First();
        var baseId = ModContent.Db.Items.Values.First(d => LootRoller.IsEquippable(d)).Id;
        var item = new ItemInstance { BaseId = baseId, Level = 10, Stats = new[] { new RolledStat(low.Id, 5), new RolledStat(high.Id, 5) } };
        var body = ItemDescriber.Describe(item, Pc, null).Body;
        int lo = body.IndexOf(UiText.StatName(low.Id), StringComparison.Ordinal), hi = body.IndexOf(UiText.StatName(high.Id), StringComparison.Ordinal);
        SelfTest.Check("loot: the rarer stat is listed first, in its rarity colour", hi >= 0 && lo > hi
            && body.Contains(UiTheme.StatHex((Rarity)Rarities.Clamp(high.Rarity)), StringComparison.Ordinal), $"{high.Id} at {hi}, {low.Id} at {lo}");
    }

    private static void Cleanup()
    {
        LootFeature.ClearAll();
        WorldState.SetRulesForTest(_savedRules!);
        Teleport(_home);
        SelfTest.Check("loot: cleanup left no world objects", LootVisuals.All.Count == 0);
    }
}
