using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using System.Globalization;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>
/// In-game checks for item text and the character sheet the guide book shows (its pages and card have their own checks in
/// BookFeature). The screen panel these once drove is gone (Max 2026-11-21): the book is the only inventory.
/// </summary>
internal static class UiSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;

    public static void Register()
    {
        SelfTest.Add(0.2f, "ui: seed bag", SeedBag);
        SelfTest.Add(0.4f, "ui: capture setup", UiCapture.Prepare);
        SelfTest.Add(1.2f, "ui: screen size", UiCapture.LogScreen);
        SelfTest.Add(1.5f, "ui: equip ring + stat", EquipRing);
        SelfTest.Add(2f, "ui: item text + compare", TooltipCompare);
        SelfTest.Add(2.5f, "ui: set text", SetTooltip);
        SelfTest.Add(3f, "ui: equip everything", EquipEverything);
        SelfTest.Add(3.5f, "ui: character sheet", CharacterSheet);
        SelfTest.Add(4f, "ui: unequip", () =>
            SelfTest.Check("ui: unequip moves the ring back", InventoryActions.ToggleEquip("equip.ring1") && Pc.Inventory.Get("equip.ring1") == null));
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
            ("helmet", 1), ("chest_armor", 2), ("pants", 1), ("boot", 2), ("shoulder_armor", 3), ("glove", 0),
            ("amulet", 3), ("bracer", 2), ("ring", 1), ("ring", 2), ("ring", 3), ("weapon", 3), ("weapon", 1),
            ("shield", 2), ("quiver", 1), ("spell_scroll", 2),
        };
        foreach (var (type, rarity) in wanted)
        {
            var def = roller.PickBase(rarity, level, rng, d => d.Type == type);
            if (def == null) continue;
            var item = roller.RollItem(def, level, rng);
            Pc.Inventory.AddToBag(item);
        }
        foreach (var id in new[] { "item.yuki_onna_strides", "item.yuki_onna_greaves" })
            if (db.Items.TryGetValue(id, out var def)) Pc.Inventory.AddToBag(roller.RollItem(def, 3, rng));

        // A well-rolled item in the right-most bag column, to see the tooltip flip to the slot's left.
        var rich = roller.PickBase(6, level, rng, d => d.Type == "chest_armor" && d.StatPools.Count >= 5);
        if (rich != null) Pc.Inventory.Restore(Pc.Inventory.Slots.Append(new("bag.47", roller.RollItem(rich, level, rng))).ToList());

        int count = Pc.Inventory.Slots.Count;
        SelfTest.Check("ui: bag seeded", count >= 15, $"{count} items");
        SelfTest.Check("ui: rarity spread", Pc.Inventory.Slots.Values.Select(i => i.Rarity).Distinct().Count() >= 3,
            string.Join(",", Pc.Inventory.Slots.Values.Select(i => (int)i.Rarity).Distinct().OrderBy(x => x)));
    }

    private static void EquipRing()
    {
        var ring = FindStatRing();
        if (ring == null) { SelfTest.Check("ui: ring with stat in bag", false); return; }
        var (slot, stat) = ring.Value;
        double before = Pc.Stats.Get(stat);
        bool ok = InventoryActions.ToggleEquip(slot);
        double after = Pc.Stats.Get(stat);
        SelfTest.Check("ui: equip puts the ring on", ok && Pc.Inventory.Get("equip.ring1") != null);
        SelfTest.Check("ui: equipping changed a stat", Math.Abs(after - before) > 1e-9, $"{stat}: {before:0.###} -> {after:0.###}");
    }

    private static void TooltipCompare()
    {
        var other = Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag.") && IsType(kv.Value, "ring"))
            .OrderBy(kv => int.Parse(kv.Key.Substring(4))).Select(kv => kv.Key).FirstOrDefault();
        SelfTest.Check("ui: second ring in bag", other != null);
        if (other == null) return;
        var item = Pc.Inventory.Get(other)!;
        var d = ItemDescriber.Describe(item, Pc, other);
        SelfTest.Check("ui: item text built", d.Title.Length > 0 && d.Body.Length > 0, d.Body.Replace('\n', '|'));
        SelfTest.Check("ui: a bag item compares with the equipped one", d.Compare != null, d.Compare?.Replace('\n', '|') ?? "none");
        bool formatted = item.Stats.Where(r => !LootRoller.IsSocketStat(r.StatId))
            .All(r => d.Body.Contains(UiText.StatValue(r.StatId, r.Value) + " " + UiText.StatName(r.StatId)));
        SelfTest.Check("ui: rolled stats formatted per definition", formatted, d.Body.Replace('\n', '|'));
        var worn = ItemDescriber.Describe(Pc.Inventory.Get("equip.ring1")!, Pc, "equip.ring1");
        SelfTest.Check("ui: an equipped item has no compare", worn.Compare == null, worn.Title);
    }

    private static void SetTooltip()
    {
        string? strides = SlotOf("item.yuki_onna_strides"), greaves = SlotOf("item.yuki_onna_greaves");
        if (strides == null || greaves == null) { SelfTest.Check("ui: set pieces in bag", false); return; }
        SelfTest.Check("ui: equip set piece", InventoryActions.ToggleEquip(strides));
        greaves = SlotOf("item.yuki_onna_greaves")!;
        var d = ItemDescriber.Describe(Pc.Inventory.Get(greaves)!, Pc, greaves);
        SelfTest.Check("ui: set text counts the worn piece", d.Body.Contains("(1/4)") && d.Body.Contains(UiTheme.Good), d.Body.Replace('\n', '|'));
    }

    private static void EquipEverything()
    {
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
            InventoryActions.ToggleEquip(slot);
        }
        int equipped = Pc.Inventory.Equipped.Count();
        SelfTest.Check("ui: equipped many slots", equipped >= 9, $"{equipped}/12");
    }

    private static void CharacterSheet()
    {
        var all = SheetData.Build(Pc);
        var rows = all.Where(r => !r.Header).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var s = Pc.Stats;
        SelfTest.Check("ui: sheet strength", rows["strength"].Value == s.Get(StatIds.Strength).ToString("N0", CultureInfo.InvariantCulture), rows["strength"].Value);
        SelfTest.Check("ui: sheet max health", rows["max_health"].Value == Derived.MaxHealth(s).ToString("N0", CultureInfo.InvariantCulture), rows["max_health"].Value);
        SelfTest.Check("ui: sheet melee", rows["melee_damage"].Value.StartsWith("×"), rows["melee_damage"].Value);
        SelfTest.Check("ui: sheet armor + reduction", rows.ContainsKey("armor") && rows.ContainsKey("physical_reduction"));
        SelfTest.Log("sheet: " + string.Join("; ", all.Where(r => !r.Header).Select(r => $"{r.Label}={r.Value}")));
    }

    // ------------------------------------------------------------------ helpers

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

    private static bool IsType(ItemInstance item, string type) =>
        ModContent.Db.Items.TryGetValue(item.BaseId, out var def) && def.Type == type;

    private static string? SlotOf(string baseId) =>
        Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag.") && kv.Value.BaseId == baseId).Select(kv => kv.Key).FirstOrDefault();

}
