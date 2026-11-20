using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

public enum CraftMode
{
    Reroll,
    Reforge,
    Polish,
    Empower,
    IndividualReroll,
    /// <summary>Destroys the item for crafting materials; takes no ingredients (see <see cref="Crafting.Scrap"/>).</summary>
    Scrap,
}

public enum CraftError
{
    None,
    WrongIngredientCount,
    IngredientRarityTooLow,
    NotEquippable,
    BadStatIndex,
    NothingToReroll,
    NoAlternativeBase,
    NoMaterial,
    /// <summary>Polish on an empty socket: nothing to change, so nothing is consumed.</summary>
    EmptySocket,
}

/// <summary>
/// Which perk unlocks each crafting mode (COTF: each mode's free perk; the crafting column shows once any is owned).
/// A mode whose perk is not in content is ungated, so deleting a perk never locks a mode for good.
/// </summary>
public static class CraftingUnlocks
{
    public static string? PerkFor(CraftMode mode) => mode switch
    {
        CraftMode.Reroll => "perk.rerolling",
        CraftMode.Reforge => "perk.reforging",
        CraftMode.Polish => "perk.polishing",
        CraftMode.Empower => "perk.empowering",
        CraftMode.IndividualReroll => "perk.rerolling_individual_stats",
        _ => null,
    };

    public static bool IsUnlocked(CraftMode mode, PerkTree perks, ContentDb content)
    {
        var id = PerkFor(mode);
        return id == null || !content.Perks.ContainsKey(id) || perks.TimesBought(id) > 0;
    }
}

/// <summary>What scrapping an item gives: one stack of a material.</summary>
public sealed record ScrapResult(ItemInstance? Material, CraftError Error)
{
    public bool Ok => Error == CraftError.None;
}

public sealed record CraftResult(ItemInstance? Item, CraftError Error)
{
    public bool Ok => Error == CraftError.None;
}

/// <summary>Crafting on mod items. Ingredients: exactly N items, each of rarity ≥ the target's.</summary>
public sealed class Crafting
{
    private readonly ContentDb _content;
    private readonly LootRoller _loot;

    public Crafting(ContentDb content, LootRoller loot)
    {
        _content = content;
        _loot = loot;
    }

    public static int IngredientCount(CraftMode mode) => mode switch
    {
        CraftMode.Reroll => 2,
        CraftMode.Reforge => 3,
        CraftMode.Empower => 9,
        CraftMode.Scrap => 0,
        _ => 1,
    };

    /// <summary>Rarity of the material a scrapped item gives: its own, but at least 3 (materials start at Rare) and at most 7.</summary>
    public static int ScrapMaterialRarity(Rarity rarity) => Math.Clamp((int)rarity, 3, 7);

    /// <summary>Material count: 1 for Common/Uncommon/Magic, then one more per rarity step, plus one per 40 item levels.</summary>
    public static int ScrapYield(Rarity rarity, int level) => Math.Max(1, (int)rarity - 1) + Math.Max(0, level) / 40;

    /// <summary>
    /// Scrap: equippable items only. Gives <see cref="ScrapYield"/> of one random material of
    /// <see cref="ScrapMaterialRarity"/> (a stack of one kind, level 1). Falls back one rarity at a time if that rarity has none.
    /// </summary>
    public ScrapResult Scrap(ItemInstance item, Rng rng)
    {
        if (!_content.Items.TryGetValue(item.BaseId, out var baseDef) || !LootRoller.IsEquippable(baseDef))
            return new ScrapResult(null, CraftError.NotEquippable);
        var material = _loot.PickBase(ScrapMaterialRarity(item.Rarity), int.MaxValue, rng, d => d.Type == "material");
        if (material == null) return new ScrapResult(null, CraftError.NoMaterial);
        var stack = _loot.RollItem(material, 1, rng) with { Amount = Math.Min(material.StackSize, ScrapYield(item.Rarity, item.Level)) };
        return new ScrapResult(stack, CraftError.None);
    }

    /// <param name="statIndex">For Polish and IndividualReroll: index into the target's stats.</param>
    /// <param name="playerLevel">For Empower.</param>
    public CraftResult Craft(CraftMode mode, ItemInstance target, IReadOnlyList<ItemInstance> ingredients, Rng rng, int statIndex = -1, int playerLevel = 1)
    {
        if (mode == CraftMode.Scrap) return Fail(CraftError.NotEquippable); // use Scrap()
        if (ingredients.Count != IngredientCount(mode)) return Fail(CraftError.WrongIngredientCount);
        if (ingredients.Any(i => i.Rarity < target.Rarity)) return Fail(CraftError.IngredientRarityTooLow);
        if (!_content.Items.TryGetValue(target.BaseId, out var baseDef)) return Fail(CraftError.NothingToReroll);
        target = _loot.InferSlots(target);

        switch (mode)
        {
            case CraftMode.Reroll:
                return Done(_loot.RollItem(baseDef, target.Level, rng) with { Upgrades = target.Upgrades });

            case CraftMode.Reforge:
            {
                // Same type, same rarity first; with fewer items per rarity, fall back to the nearest rarity of the same type.
                var candidates = _content.Items.Values
                    .Where(d => d.Id != baseDef.Id && d.Type == baseDef.Type && d.MinLevel <= Math.Max(1, target.Level))
                    .ToList();
                if (candidates.Count == 0) return Fail(CraftError.NoAlternativeBase);
                int nearest = candidates.Min(d => Math.Abs(d.Rarity - baseDef.Rarity));
                var pool = candidates.Where(d => Math.Abs(d.Rarity - baseDef.Rarity) == nearest).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
                var other = rng.Pick(pool);
                return Done(_loot.RollItem(other, target.Level, rng));
            }

            case CraftMode.Polish:
            {
                if (statIndex < 0 || statIndex >= target.Stats.Count) return Fail(CraftError.BadStatIndex);
                var stat = target.Stats[statIndex];
                if (stat.StatId == LootRoller.EmptySocketStat) return Fail(CraftError.EmptySocket);
                var stats = target.Stats.ToList();
                if (IsSocket(stat.StatId))
                {
                    stats[statIndex] = new RolledStat(LootRoller.EmptySocketStat, 0);
                }
                else
                {
                    if (!_content.ItemStats.TryGetValue(stat.StatId, out var def)) return Fail(CraftError.NothingToReroll);
                    stats[statIndex] = stat with { Value = Reroll(baseDef, stat, def, target.Level, target.Rarity, rng) };
                }
                return Done(target with { Stats = stats });
            }

            case CraftMode.Empower:
            {
                if (!LootRoller.IsEquippable(baseDef)) return Fail(CraftError.NotEquippable);
                int level = Math.Max(1, playerLevel);
                var stats = target.Stats.Select(s =>
                    IsSocket(s.StatId) || !_content.ItemStats.TryGetValue(s.StatId, out var def)
                        ? s
                        : s with { Value = Reroll(baseDef, s, def, level, target.Rarity, rng) }).ToList();
                return Done(target with { Level = level, Stats = stats, Upgrades = target.Upgrades + 1 });
            }

            case CraftMode.IndividualReroll:
            {
                if (statIndex < 0 || statIndex >= target.Stats.Count) return Fail(CraftError.BadStatIndex);
                var item = target;
                var current = item.Stats[statIndex];
                var options = RerollOptions(item, statIndex, distinct: false);
                if (options.Count == 0) return Fail(CraftError.NothingToReroll);
                var def = _content.ItemStats[rng.Pick(options)];
                var stats = item.Stats.ToList();
                stats[statIndex] = LootRoller.RollStat(baseDef, current.Slot, def, item.Level, (int)item.Rarity, rng);
                return Done(item with { Stats = stats });
            }
        }
        return Fail(CraftError.NothingToReroll);
    }

    /// <summary>
    /// What Individual reroll can turn stat <paramref name="statIndex"/> into: the other options of the slot it was rolled in
    /// ("*" counts as every common stat, so with <paramref name="distinct"/> false each entry is one equal-weight pick).
    /// Empty options and the current stat are left out ✱. Empty for sockets and stats with no known slot.
    /// </summary>
    public IReadOnlyList<string> RerollOptions(ItemInstance item, int statIndex, bool distinct = true)
    {
        if (statIndex < 0 || statIndex >= item.Stats.Count) return Array.Empty<string>();
        if (!_content.Items.TryGetValue(item.BaseId, out var baseDef)) return Array.Empty<string>();
        var stat = _loot.InferSlots(item).Stats[statIndex];
        if (stat.Slot < 0 || IsSocket(stat.StatId)) return Array.Empty<string>();
        var options = _loot.SlotOptions(baseDef, stat.Slot)
            .Where(o => o != null && o != stat.StatId && _content.ItemStats.ContainsKey(o)).Select(o => o!);
        return (distinct ? options.Distinct() : options).ToList();
    }

    /// <summary>The value range of a rolled stat at the item's level and rarity, with its slot multiplier.</summary>
    public (double Min, double Max)? Range(ItemInstance item, RolledStat stat)
    {
        if (IsSocket(stat.StatId) || !_content.ItemStats.TryGetValue(stat.StatId, out var def)) return null;
        double? mult = _content.Items.TryGetValue(item.BaseId, out var baseDef) ? baseDef.MultiplierFor(stat.Slot, stat.StatId) : null;
        return LootRoller.ValueRange(def, item.Level, (int)item.Rarity, mult);
    }

    private static float Reroll(ItemBaseDef baseDef, RolledStat stat, ItemStatDef def, int level, Rarity rarity, Rng rng) =>
        (float)LootRoller.RollValue(def, level, (int)rarity, rng, baseDef.MultiplierFor(stat.Slot, stat.StatId));

    private static bool IsSocket(string statId) => LootRoller.IsSocketStat(statId);

    private static CraftResult Done(ItemInstance item) => new(item, CraftError.None);

    private static CraftResult Fail(CraftError e) => new(null, e);
}
