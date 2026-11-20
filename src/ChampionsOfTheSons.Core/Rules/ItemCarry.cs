namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Item stats that raise the game's carry limit while the item is worn (COTF stats 1000-1004). They add to the same
/// per-item limit as the carry perks (<see cref="UtilityPerks"/>); items are the game's own names, as there.
/// </summary>
public static class ItemCarry
{
    public static readonly IReadOnlyDictionary<string, string[]> ItemsByStat = new Dictionary<string, string[]>
    {
        ["stat.extracarriedsticks"] = new[] { "Stick" },
        ["stat.extracarriedrocks"] = new[] { "Rock" },
        ["stat.extracarriedropes"] = new[] { "Rope" },
        ["stat.extracarrieddrinks"] = new[] { "VodkaBottle", "EnergyDrink" },
        ["stat.extracarriedfood"] = new[] { "Meat", "Fish", "EnergyBar" },
    };

    /// <summary>Carry-limit change per item keyword from the worn items; each rolled value counts rounded, as in COTF.</summary>
    public static Dictionary<string, int> Totals(IEnumerable<ItemInstance> equipped)
    {
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in equipped)
            foreach (var stat in item.Stats)
            {
                if (!ItemsByStat.TryGetValue(stat.StatId, out var names)) continue;
                int amount = (int)Math.Round(stat.Value, MidpointRounding.AwayFromZero);
                foreach (var name in names) totals[name] = totals.GetValueOrDefault(name) + amount;
            }
        return totals;
    }
}
