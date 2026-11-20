using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>Small in-memory content for rules tests, so tests don't depend on shipped content values.</summary>
public static class TestContent
{
    public const string PlayerStats = """
    { "playerStats": [
      { "id": "max_health", "kind": "add", "default": 0, "integer": true },
      { "id": "crit_chance", "kind": "add", "default": 0.05 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
      { "id": "max_health_mult", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "i_infinity_loop", "kind": "bool", "default": 0 }
    ] }
    """;

    public const string ItemStats = """
    { "itemStats": [
      { "id": "stat.max_life", "legacyId": 5, "name": "Maximum Life", "min": 4, "max": 6, "levelPow": 1.2, "combine": "sum", "rarity": 0,
        "effects": [ { "stat": "max_health", "op": "add", "transform": "round" } ] },
      { "id": "stat.crit", "legacyId": 12, "name": "Crit Chance", "min": 0.01, "max": 0.02, "levelPow": 0.25, "combine": "sum", "rarity": 3, "cap": 0.5,
        "effects": [ { "stat": "crit_chance", "op": "add", "transform": "x" } ] },
      { "id": "stat.damage_reduction", "legacyId": 11, "name": "Damage Reduction", "min": 0.005, "max": 0.0075, "levelPow": 0.5, "combine": "one_minus_product", "rarity": 2,
        "effects": [ { "stat": "all_damage_taken", "op": "mult", "transform": "one_minus" } ] }
    ] }
    """;

    public const string Items = """
    { "items": [
      { "id": "item.ring", "name": "Test Ring", "type": "ring", "rarity": 2, "minLevel": 1, "maxLevel": 50,
        "statPools": [ ["stat.max_life"], ["stat.crit", null], ["*"] ],
        "uniqueEffects": [ { "stat": "i_infinity_loop", "op": "enable" } ] },
      { "id": "item.boots", "name": "Test Boots", "type": "boot", "rarity": 0, "minLevel": 1, "maxLevel": 5,
        "statPools": [ ["stat.max_life"] ] }
    ] }
    """;

    public static ContentDb Create(Dictionary<string, string>? overrides = null)
    {
        var files = new Dictionary<string, string>
        {
            ["player_stats.json"] = PlayerStats,
            ["item_stats.json"] = ItemStats,
            ["items.json"] = Items,
        };
        if (overrides != null)
            foreach (var (k, v) in overrides) files[k] = v;
        var db = ContentDb.Load(files);
        Assert.True(db.Errors.Count == 0, string.Join("\n", db.Errors));
        return db;
    }
}
