using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

public enum CraftMode
{
    Reroll,
    Reforge,
    Polish,
    Empower,
    IndividualReroll,
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
        _ => 1,
    };

    /// <param name="statIndex">For Polish and IndividualReroll: index into the target's stats.</param>
    /// <param name="playerLevel">For Empower.</param>
    public CraftResult Craft(CraftMode mode, ItemInstance target, IReadOnlyList<ItemInstance> ingredients, Rng rng, int statIndex = -1, int playerLevel = 1)
    {
        if (ingredients.Count != IngredientCount(mode)) return Fail(CraftError.WrongIngredientCount);
        if (ingredients.Any(i => i.Rarity < target.Rarity)) return Fail(CraftError.IngredientRarityTooLow);
        if (!_content.Items.TryGetValue(target.BaseId, out var baseDef)) return Fail(CraftError.NothingToReroll);

        switch (mode)
        {
            case CraftMode.Reroll:
                return Done(_loot.RollItem(baseDef, target.Level, rng) with { Upgrades = target.Upgrades });

            case CraftMode.Reforge:
            {
                var other = _loot.PickBase(baseDef.Rarity, target.Level, rng,
                    d => d.Id != baseDef.Id && d.Rarity == baseDef.Rarity && d.Type == baseDef.Type);
                if (other == null) return Fail(CraftError.NoAlternativeBase);
                return Done(_loot.RollItem(other, target.Level, rng));
            }

            case CraftMode.Polish:
            {
                if (statIndex < 0 || statIndex >= target.Stats.Count) return Fail(CraftError.BadStatIndex);
                var stat = target.Stats[statIndex];
                var stats = target.Stats.ToList();
                if (IsSocket(stat.StatId))
                {
                    stats[statIndex] = new RolledStat(LootRoller.EmptySocketStat, 0);
                }
                else
                {
                    if (!_content.ItemStats.TryGetValue(stat.StatId, out var def)) return Fail(CraftError.NothingToReroll);
                    stats[statIndex] = stat with { Value = (float)LootRoller.RollValue(def, target.Level, (int)target.Rarity, rng) };
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
                        : s with { Value = (float)LootRoller.RollValue(def, level, (int)target.Rarity, rng) }).ToList();
                return Done(target with { Level = level, Stats = stats, Upgrades = target.Upgrades + 1 });
            }

            case CraftMode.IndividualReroll:
            {
                if (statIndex < 0 || statIndex >= target.Stats.Count) return Fail(CraftError.BadStatIndex);
                var current = target.Stats[statIndex].StatId;
                var pool = baseDef.StatPools.FirstOrDefault(p => p.Contains(current));
                var options = pool?.Where(o => o != null && o != ItemBaseDef.AnyStat && o != current && _content.ItemStats.ContainsKey(o)).Select(o => o!).ToList();
                if (options == null || options.Count == 0) return Fail(CraftError.NothingToReroll);
                var def = _content.ItemStats[rng.Pick(options)];
                var stats = target.Stats.ToList();
                stats[statIndex] = new RolledStat(def.Id, (float)LootRoller.RollValue(def, target.Level, (int)target.Rarity, rng));
                return Done(target with { Stats = stats });
            }
        }
        return Fail(CraftError.NothingToReroll);
    }

    private static bool IsSocket(string statId) => statId.StartsWith("stat.socket", StringComparison.Ordinal) || statId == LootRoller.EmptySocketStat;

    private static CraftResult Done(ItemInstance item) => new(item, CraftError.None);

    private static CraftResult Fail(CraftError e) => new(null, e);
}
