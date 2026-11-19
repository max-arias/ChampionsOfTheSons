using System.Globalization;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Icons;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>
/// In-game checks for the panels, registered when <see cref="SelfTest.Enabled"/>. They fill the bag, open the
/// inventory, equip, build tooltips and write a screenshot of each state to <c>%TEMP%\cots-ui\</c>.
/// </summary>
internal static class UiSelfTest
{
    private static InventoryPanel Panel => CotsUi.Inventory;
    private static PlayerCharacter Pc => PlayerState.Character;

    private static readonly List<string> Shots = new();

    public static void Register()
    {
        SelfTest.Add(0.2f, "ui: seed bag", SeedBag);
        SelfTest.Add(0.4f, "ui: capture setup", UiCapture.Prepare);
        SelfTest.Add(1.2f, "ui: screen size", UiCapture.LogScreen);
        SelfTest.Add(1.5f, "ui: open inventory", OpenInventory);
        SelfTest.Add(2.5f, "ui: pointer hit-test", HitTest);
        SelfTest.Add(3f, "ui: shot inventory", () => Shot("01-inventory"));
        SelfTest.Add(3.5f, "ui: equip ring + stat", EquipRing);
        SelfTest.Add(4.5f, "ui: tooltip + compare", TooltipCompare);
        SelfTest.Add(5.5f, "ui: shot tooltip compare", () => Shot("02-tooltip-compare"));
        SelfTest.Add(6f, "ui: set tooltip", SetTooltip);
        SelfTest.Add(7f, "ui: shot tooltip set", () => Shot("03-tooltip-set"));
        SelfTest.Add(7.5f, "ui: equip everything", EquipEverything);
        SelfTest.Add(9f, "ui: character sheet", CharacterSheet);
        SelfTest.Add(10.5f, "ui: shot sheet", () => Shot("04-character-sheet"));
        SelfTest.Add(11f, "ui: equipped tooltip", EquippedTooltip);
        SelfTest.Add(12f, "ui: shot equipped tooltip", () => Shot("05-tooltip-equipped"));
        SelfTest.Add(12.5f, "ui: tooltip at the right edge", EdgeTooltip);
        SelfTest.Add(13.5f, "ui: shot tooltip edge", () => Shot("06-tooltip-right-edge"));
        SelfTest.Add(14f, "ui: verify screenshots", VerifyShots);
        SelfTest.Add(14.5f, "ui: unequip + close", UnequipAndClose);
    }

    // ------------------------------------------------------------------ steps

    private static void SeedBag()
    {
        var db = ModContent.Db;
        var roller = new LootRoller(db);
        var rng = new Rng(20261118);
        const int level = 30;

        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        // A level and some XP so the sheet has something to show.
        var save = PlayerState.ToSave();
        save.Level = 17;
        save.Xp = 2500;
        PlayerState.FromSave(save);

        var wanted = new (string Type, int Rarity)[]
        {
            ("helmet", 1), ("chest_armor", 3), ("pants", 2), ("boot", 4), ("shoulder_armor", 5), ("glove", 0),
            ("amulet", 6), ("bracer", 3), ("ring", 2), ("ring", 4), ("ring", 7), ("weapon", 5), ("weapon", 1),
            ("shield", 3), ("quiver", 2), ("spell_scroll", 4), ("material", 0),
        };
        foreach (var (type, rarity) in wanted)
        {
            var def = roller.PickBase(rarity, level, rng, d => d.Type == type);
            if (def == null) continue;
            var item = roller.RollItem(def, level, rng);
            if (type == "material") item = item with { Amount = 3 };
            Pc.Inventory.AddToBag(item);
        }
        foreach (var id in new[] { "item.yuki_onna_strides", "item.yuki_onna_greaves" })
            if (db.Items.TryGetValue(id, out var def)) Pc.Inventory.AddToBag(roller.RollItem(def, 3, rng));

        // A well-rolled item in the right-most bag column, to see the tooltip flip to the slot's left.
        var rich = roller.PickBase(6, level, rng, d => d.Type == "chest_armor" && d.StatPools.Count >= 5);
        if (rich != null) Pc.Inventory.Restore(Pc.Inventory.Slots.Append(new("bag.47", roller.RollItem(rich, level, rng))).ToList());

        int count = Pc.Inventory.Slots.Count;
        SelfTest.Check("ui: bag seeded", count >= 15, $"{count} items");
        SelfTest.Check("ui: rarity spread", Pc.Inventory.Slots.Values.Select(i => i.Rarity).Distinct().Count() >= 5,
            string.Join(",", Pc.Inventory.Slots.Values.Select(i => (int)i.Rarity).Distinct().OrderBy(x => x)));
    }

    private static void OpenInventory()
    {
        Panel.Open();
        SelfTest.Check("ui: panel open", Panel.IsOpen);
        int expected = LootRoller.BagSlots + LootRoller.EquipSlots.Count;
        SelfTest.Check("ui: slot widgets", Panel.Slots.Count == expected, $"{Panel.Slots.Count}/{expected}");

        int shown = Panel.Slots.Count(w => w.Shown != null);
        SelfTest.Check("ui: slots show items", shown == Pc.Inventory.Slots.Count, $"{shown}/{Pc.Inventory.Slots.Count}");
        var withIcon = Panel.Slots.Where(w => w.Shown != null && IconCache.GetTexture(w.Shown.BaseId) != null).ToList();
        SelfTest.Check("ui: icon sprite loads", IconCache.GetSprite("item.broken_flip_flops") != null && withIcon.Count > 0, $"{withIcon.Count} slots have an icon file");
        SelfTest.Check("ui: slots with an icon file show the image", withIcon.All(w => IconView.IsShown(w.Icon)),
            string.Join(",", withIcon.Where(w => !IconView.IsShown(w.Icon)).Select(w => w.Key)));
        SelfTest.Check("ui: sheet rows", Panel.SheetRows.Count >= 20, $"{Panel.SheetRows.Count} rows");
    }

    /// <summary>The first bag ring with a rolled stat that changes a player stat, and that player stat.</summary>
    private static (string Slot, string PlayerStat)? FindStatRing()
    {
        foreach (var (slot, item) in Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag.")).OrderBy(kv => int.Parse(kv.Key.Substring(4))))
        {
            if (!ModContent.Db.Items.TryGetValue(item.BaseId, out var def) || def.Type != "ring") continue;
            foreach (var rolled in item.Stats)
            {
                if (!ModContent.Db.ItemStats.TryGetValue(rolled.StatId, out var sd)) continue;
                var effect = sd.Effects.FirstOrDefault(e => e.Op is EffectOp.Add or EffectOp.Mult && Math.Abs(e.Resolve(rolled.Value)) > 1e-9);
                if (effect == null) continue;
                return (slot, effect.Stat);
            }
        }
        return null;
    }

    private static void EquipRing()
    {
        var ring = FindStatRing();
        if (ring == null) { SelfTest.Check("ui: ring with stat in bag", false); return; }
        var (slot, stat) = ring.Value;
        double before = Pc.Stats.Get(stat);
        bool ok = Panel.ClickSlot(slot);
        double after = Pc.Stats.Get(stat);
        SelfTest.Check("ui: click equips ring", ok && Pc.Inventory.Get("equip.ring1") != null);
        SelfTest.Check("ui: equipping changed a stat", Math.Abs(after - before) > 1e-9, $"{stat}: {before:0.###} -> {after:0.###}");
        var shown = Panel.Slot("equip.ring1")?.Shown;
        SelfTest.Check("ui: equip slot shows ring", shown != null);
    }

    private static void TooltipCompare()
    {
        var other = Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag.") && IsType(kv.Value, "ring"))
            .OrderBy(kv => int.Parse(kv.Key.Substring(4))).Select(kv => kv.Key).FirstOrDefault();
        SelfTest.Check("ui: second ring in bag", other != null);
        if (other == null) return;

        Panel.SimulateHover(other);
        var d = Panel.TooltipShown;
        SelfTest.Check("ui: tooltip built", d != null && d.Title.Length > 0 && d.Body.Length > 0, d?.Body.Replace('\n', '|') ?? "");
        SelfTest.Check("ui: tooltip has compare block", d?.Compare != null, d?.Compare?.Replace('\n', '|') ?? "none");
        var shownItem = Pc.Inventory.Get(other)!;
        bool formatted = shownItem.Stats.Where(r => r.StatId != LootRoller.EmptySocketStat)
            .All(r => d != null && d.Body.Contains(UiText.StatValue(r.StatId, r.Value) + " " + UiText.StatName(r.StatId)));
        SelfTest.Check("ui: rolled stats formatted per definition", formatted, d?.Body.Replace('\n', '|') ?? "");
        Panel.SimulateHover(Inventory.Bag(119));
        SelfTest.Check("ui: empty slot hides tooltip", Panel.TooltipShown == null);
        Panel.SimulateHover(other);
    }

    private static void SetTooltip()
    {
        string? strides = SlotOf("item.yuki_onna_strides"), greaves = SlotOf("item.yuki_onna_greaves");
        if (strides == null || greaves == null) { SelfTest.Check("ui: set pieces in bag", false); return; }
        Panel.ReleaseSimulatedHover();
        SelfTest.Check("ui: equip set piece", Panel.ClickSlot(strides));
        greaves = SlotOf("item.yuki_onna_greaves")!;
        Panel.SimulateHover(greaves);
        var d = Panel.TooltipShown;
        SelfTest.Check("ui: set tooltip", d != null && d.Body.Contains("(1/4)") && d.Body.Contains(UiTheme.Good), d?.Body.Replace('\n', '|') ?? "none");
    }

    private static void EquipEverything()
    {
        Panel.ReleaseSimulatedHover();
        // Fill every free slot from the bag, set pieces first so the set bonus shows up on the sheet.
        var bag = Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag."))
            .OrderByDescending(kv => ModContent.Db.Items.TryGetValue(kv.Value.BaseId, out var d) && d.Set != null)
            .ThenBy(kv => int.Parse(kv.Key.Substring(4))).Select(kv => kv.Key).ToList();
        foreach (var slot in bag)
        {
            var item = Pc.Inventory.Get(slot);
            if (item == null || !ModContent.Db.Items.TryGetValue(item.BaseId, out var def)) continue;
            var targets = LootRoller.SlotsFor(def.Type);
            if (targets.Count == 0 || targets.All(t => Pc.Inventory.Get(t) != null)) continue;
            Panel.ClickSlot(slot);
        }
        int equipped = Pc.Inventory.Equipped.Count();
        SelfTest.Check("ui: equipped many slots", equipped >= 9, $"{equipped}/12");
    }

    private static void CharacterSheet()
    {
        Panel.RefreshAll();
        var rows = Panel.SheetRows.ToDictionary(r => r.Id);
        var s = Pc.Stats;
        SelfTest.Check("ui: sheet strength", rows["strength"].Value == $"{s.Get(StatIds.Strength).ToString("N0", CultureInfo.InvariantCulture)}", rows["strength"].Value);
        SelfTest.Check("ui: sheet max health", rows["max_health"].Value == $"{Derived.MaxHealth(s).ToString("N0", CultureInfo.InvariantCulture)}", rows["max_health"].Value);
        SelfTest.Check("ui: sheet melee", rows["melee_damage"].Value.StartsWith("×"), rows["melee_damage"].Value);
        SelfTest.Check("ui: sheet armor + reduction", rows.ContainsKey("armor") && rows.ContainsKey("physical_reduction"),
            $"{rows["armor"].Value} / {rows["physical_reduction"].Value}");
        SelfTest.Log("sheet: " + string.Join("; ", Panel.SheetRows.Where(r => !r.Header).Select(r => $"{r.Label}={r.Value}")));
    }

    private static void EquippedTooltip()
    {
        Panel.SimulateHover("equip.weapon");
        var d = Panel.TooltipShown;
        SelfTest.Check("ui: equipped item tooltip, no compare", d != null && d.Compare == null, d?.Title ?? "none");
    }

    private static void UnequipAndClose()
    {
        Panel.ReleaseSimulatedHover();
        SelfTest.Check("ui: click unequips", Panel.ClickSlot("equip.ring1") && Pc.Inventory.Get("equip.ring1") == null);
        Panel.Close();
        SelfTest.Check("ui: panel closed", !Panel.IsOpen);
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsType(ItemInstance item, string type) =>
        ModContent.Db.Items.TryGetValue(item.BaseId, out var def) && def.Type == type;

    private static string? SlotOf(string baseId) =>
        Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag.") && kv.Value.BaseId == baseId).Select(kv => kv.Key).FirstOrDefault();

    /// <summary>A UI raycast at a slot's screen position must find that slot (this is what hover and click rely on).</summary>
    private static void HitTest()
    {
        var failures = new List<string>();
        foreach (var key in new[] { "bag.0", "bag.11", "bag.47", "bag.100", "bag.119", "equip.helmet", "equip.ring2", "equip.offhand" })
        {
            var at = Panel.ScreenPointOf(key);
            var found = Panel.RaycastSlotAt(at);
            if (found != key) failures.Add($"{key}@{at.x:0},{at.y:0} -> {found ?? "none"}");
        }
        SelfTest.Check("ui: raycast finds the slot under a point", failures.Count == 0, string.Join("; ", failures));
        SelfTest.Check("ui: raycast outside the window finds nothing", Panel.RaycastSlotAt(new Vector2(2, 2)) == null);
    }

    private static void EdgeTooltip()
    {
        Panel.SimulateHover("bag.47");
        SelfTest.Check("ui: right-edge item has a tooltip", Panel.TooltipShown != null);
        var tip = Panel.TooltipBounds;
        var slot = Panel.SlotBounds("bag.47");
        var canvas = Panel.CanvasSize;
        SelfTest.Check("ui: tooltip stays on the canvas", tip.xMin >= 0 && tip.yMin >= 0 && tip.xMax <= canvas.x && tip.yMax <= canvas.y,
            $"tooltip {tip}, canvas {canvas}");
        SelfTest.Check("ui: tooltip does not cover its slot", !tip.Overlaps(slot), $"tooltip {tip}, slot {slot}, onLeft={Panel.TooltipOnLeft}");
    }

    private static void Shot(string name)
    {
        if (Application.isBatchMode)
        {
            SelfTest.Log($"screenshot {name} skipped: batchmode has no back buffer (run tools/run-selftest.ps1 without -Headless)");
            return;
        }
        var path = UiCapture.SaveScreen(name);
        if (path != null) Shots.Add(path);
        SelfTest.Check($"ui: screenshot {name} requested", path != null, path ?? UiCapture.LastError);
    }

    /// <summary>Screen captures are written at the end of the frame, so they are checked here, a while after they were taken.</summary>
    private static void VerifyShots()
    {
        foreach (var path in Shots)
        {
            var (ok, detail) = UiCapture.Verify(path);
            SelfTest.Check($"ui: screenshot {Path.GetFileName(path)} has content", ok, detail);
        }
    }
}
