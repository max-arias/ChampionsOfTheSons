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
    private static InventoryPanel Panel => CotsUi.Inventory;
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
        SelfTest.Add(T0 + 22.5f, "loot: right-click menu", MenuOpen);
        SelfTest.Add(T0 + 24, "loot: shot menu", () => TestKit.Screenshot("loot-02-menu"));
        SelfTest.Add(T0 + 25, "loot: menu equip", MenuEquip);
        SelfTest.Add(T0 + 25.5f, "loot: menu destroy", MenuDestroy);
        SelfTest.Add(T0 + 26, "loot: menu drop", MenuDrop);
        SelfTest.Add(T0 + 26.5f, "loot: drag and drop", DragAndDrop);
        SelfTest.Add(T0 + 27f, "loot: level 25 heart, menu Use", UseHeart);
        SelfTest.Add(T0 + 27.5f, "loot: cleanup", Cleanup);
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
        UI.CotsUi.Inventory.Close();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        LootFeature.ClearAll();
        _home = Player;
        _savedRules = WorldState.Rules;
        SelfTest.Check("loot: ground empty at start", Ground.Count == 0 && LootVisuals.All.Count == 0);
    }

    private static void KillForDrops()
    {
        // Force the drop roll: the host's drop chance multiplier is part of the world rules.
        WorldState.SetRulesForTest(_savedRules! with { DropChance = 1000, AllowElites = false });
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
        SelfTest.Check("loot: boss rolls many drops", items.Count >= 9, $"{items.Count}");
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

    private static void MenuOpen()
    {
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        TestKit.Give("item.yuki_onna_strides", level: 5);
        // bag.1 must be equippable for the menu-equip check (a random base can be a material or consumable).
        TestKit.Give(ModContent.Db.Items.Values.Where(d => LootRoller.IsEquippable(d) && (int)d.Rarity == (int)Rarity.Rare && d.MinLevel <= 20).OrderBy(d => d.Id, StringComparer.Ordinal).First().Id, level: 20);
        TestKit.Give(rarity: 5, level: 20);
        Panel.Open();
        var bounds = Panel.SlotBounds("bag.1");
        Panel.OpenMenu("bag.1", new Vector2(bounds.center.x, bounds.center.y));
        SelfTest.Check("loot: right-click menu opens on an item", Panel.MenuVisible && Panel.MenuSlot == "bag.1");
        SelfTest.Check("loot: menu offers equip for a bag item", Panel.MenuEquipText == UiText.T("ui.inventory.menu.equip", "Equip"), Panel.MenuEquipText);
        var b = Panel.MenuBounds;
        var canvas = Panel.CanvasSize;
        SelfTest.Check("loot: menu stays on the canvas", b.xMin >= 0 && b.yMin >= 0 && b.xMax <= canvas.x && b.yMax <= canvas.y, b.ToString());
    }

    private static void MenuEquip()
    {
        var item = Pc.Inventory.Get("bag.1");
        bool ok = Panel.MenuEquipClicked();
        SelfTest.Check("loot: menu equip equips the item", ok && Pc.Inventory.Get("bag.1") == null && Pc.Inventory.Equipped.Any(kv => kv.Value == item));
        SelfTest.Check("loot: menu closed after equip", !Panel.MenuVisible);

        var slot = Pc.Inventory.Equipped.First().Key;
        var sb = Panel.SlotBounds(slot);
        Panel.OpenMenu(slot, new Vector2(sb.center.x, sb.center.y));
        SelfTest.Check("loot: menu offers unequip on an equipped item", Panel.MenuEquipText == UiText.T("ui.inventory.menu.unequip", "Unequip"), Panel.MenuEquipText);
        SelfTest.Check("loot: menu unequip moves it to the bag", Panel.MenuEquipClicked() && Pc.Inventory.Equipped.Count() == 0);
    }

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
            Panel.Open();
            Panel.OpenMenu(heart, new Vector2(300, 300));
            SelfTest.Check("loot: menu offers Use on the heart", Panel.MenuEquipText == UiText.T("ui.catalog.use", "Use"), Panel.MenuEquipText);
            bool used = Panel.MenuEquipClicked();
            SelfTest.Check("loot: using the heart adds one permanent perk point",
                used && Pc.Progress.BonusPerkPoints == bonus + 1 && Pc.Perks.AvailablePoints(Pc.Progress) == points + 2,
                $"bonus {bonus} -> {Pc.Progress.BonusPerkPoints}, points {points} -> {Pc.Perks.AvailablePoints(Pc.Progress)}");
            SelfTest.Check("loot: the used heart is gone", Pc.Inventory.Get(heart) == null);
            Panel.Close();
        }
        finally { Pc.Load(original); }
    }

    private static void MenuDestroy()
    {
        var slot = Pc.Inventory.Slots.Keys.First(k => k.StartsWith("bag."));
        int count = Pc.Inventory.Slots.Count;
        Panel.OpenMenu(slot, new Vector2(300, 300));
        bool first = Panel.MenuDestroyClicked();
        SelfTest.Check("loot: first destroy click only asks to confirm", !first && Pc.Inventory.Slots.Count == count && Panel.MenuVisible);
        SelfTest.Check("loot: destroy button shows the confirm text", Panel.MenuDestroyText == UiText.T("ui.inventory.menu.destroy_confirm", "Click again to destroy"), Panel.MenuDestroyText);
        // Closing the menu and reopening it must disarm the button.
        Panel.CloseMenu();
        Panel.OpenMenu(slot, new Vector2(300, 300));
        SelfTest.Check("loot: reopening the menu disarms destroy", !Panel.MenuDestroyClicked() && Pc.Inventory.Slots.Count == count);
        bool second = Panel.MenuDestroyClicked();
        SelfTest.Check("loot: second destroy click deletes the item", second && Pc.Inventory.Slots.Count == count - 1 && !Panel.MenuVisible);
    }

    private static void MenuDrop()
    {
        var slot = Pc.Inventory.Slots.Keys.First(k => k.StartsWith("bag."));
        int drops = Ground.Count, items = Pc.Inventory.Slots.Count;
        Panel.OpenMenu(slot, new Vector2(300, 300));
        bool ok = Panel.MenuDropClicked();
        SelfTest.Check("loot: menu drop puts the item in the world", ok && Ground.Count == drops + 1 && Pc.Inventory.Slots.Count == items - 1);
    }

    private static void DragAndDrop()
    {
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        var db = ModContent.Db;
        // Find items by type so the checks do not depend on what was rolled.
        var roller = new LootRoller(db);
        var rng = new Rng(5);
        ItemInstance Make(string type) => roller.RollItem(roller.PickBase(2, 30, rng, d => d.Type == type)!, 30, rng);
        Pc.Inventory.Restore(new[]
        {
            new KeyValuePair<string, ItemInstance>("bag.0", Make("helmet")),
            new KeyValuePair<string, ItemInstance>("bag.1", Make("weapon")),
            new KeyValuePair<string, ItemInstance>("bag.5", Make("ring")),
        });
        var helm = Pc.Inventory.Get("bag.0");
        SelfTest.Check("loot: drag refuses an item that does not fit the slot", !Panel.DragDrop("bag.0", "equip.weapon") && Pc.Inventory.Get("bag.0") == helm);
        SelfTest.Check("loot: drag moves a fitting item into an equipment slot", Panel.DragDrop("bag.0", "equip.helmet") && Pc.Inventory.Get("equip.helmet") == helm);
        SelfTest.Check("loot: drag onto a free bag slot moves the item", Panel.DragDrop("bag.5", "bag.40") && Pc.Inventory.Get("bag.5") == null && Pc.Inventory.Get("bag.40") != null);
        var a = Pc.Inventory.Get("bag.1");
        var b = Pc.Inventory.Get("bag.40");
        SelfTest.Check("loot: drag onto an item swaps them", Panel.DragDrop("bag.1", "bag.40") && Pc.Inventory.Get("bag.1") == b && Pc.Inventory.Get("bag.40") == a);
        SelfTest.Check("loot: dragging a non-fitting item onto an occupied slot is refused", !Panel.DragDrop("bag.40", "equip.helmet"));
        SelfTest.Check("loot: dragging an empty slot does nothing", !Panel.DragDrop("bag.99", "bag.98"));
    }

    private static void Cleanup()
    {
        Panel.CloseMenu();
        Panel.Close();
        LootFeature.ClearAll();
        WorldState.SetRulesForTest(_savedRules!);
        Teleport(_home);
        SelfTest.Check("loot: cleanup left no world objects", LootVisuals.All.Count == 0);
    }
}
