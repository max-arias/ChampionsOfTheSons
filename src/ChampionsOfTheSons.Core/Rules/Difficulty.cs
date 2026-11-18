namespace ChampionsOfTheSons.Core.Rules;

/// <summary>One of the 11 difficulty tiers. <see cref="Index"/> is d in the formulas (0 = Easy, 10 = Hell).</summary>
public sealed record DifficultyTier(
    int Index,
    string Id,
    string Name,
    int BaseLevelMin,
    int BaseLevelMax,
    float ShieldingCooldown,
    int MeteorDamageMin,
    int MeteorDamageMax,
    int LaserDamageMin,
    int LaserDamageMax)
{
    public int ArmorFactor => Index / 2 + 1;
    public bool IsHell => Index == Difficulties.Hell.Index;
    /// <summary>Elite ability count range (inclusive).</summary>
    public (int Min, int Max) EliteAbilityCount => Index <= 1 ? (2, 2) : (3, 6);
}

public static class Difficulties
{
    public static readonly DifficultyTier Easy = new(0, "difficulty.easy", "Easy", 1, 3, 60, 40, 49, 30, 39);
    public static readonly DifficultyTier Hell = new(10, "difficulty.hell", "Hell", 300, 300, 15, 75000, 75000, 224636, 224636);

    public static readonly IReadOnlyList<DifficultyTier> All = new[]
    {
        Easy,
        new DifficultyTier(1, "difficulty.veteran", "Veteran", 10, 13, 55, 100, 149, 85, 104),
        new DifficultyTier(2, "difficulty.elite", "Elite", 20, 24, 50, 300, 354, 221, 233),
        new DifficultyTier(3, "difficulty.master", "Master", 30, 39, 45, 660, 699, 444, 469),
        new DifficultyTier(4, "difficulty.challenge1", "Challenge I", 50, 59, 40, 3000, 3000, 4124, 4124),
        new DifficultyTier(5, "difficulty.challenge2", "Challenge II", 89, 91, 35, 9000, 9000, 15653, 15653),
        new DifficultyTier(6, "difficulty.challenge3", "Challenge III", 100, 100, 30, 14000, 14000, 72346, 72346),
        new DifficultyTier(7, "difficulty.challenge4", "Challenge IV", 130, 132, 25, 25000, 25000, 85932, 85932),
        new DifficultyTier(8, "difficulty.challenge5", "Challenge V", 160, 164, 20, 50000, 50000, 124636, 124636),
        new DifficultyTier(9, "difficulty.challenge6", "Challenge VI", 200, 200, 15, 65000, 65000, 164636, 164636),
        Hell,
    };

    public static DifficultyTier? ById(string? id) => All.FirstOrDefault(d => d.Id == id);
}

public enum LootLevelPolicy
{
    Highest,
    Average,
    Lowest,
    Closest,
    Host,
}

/// <summary>Host-side multipliers and switches (all multipliers default to 1).</summary>
public sealed record WorldRules
{
    public DifficultyTier Difficulty { get; init; } = Difficulties.Easy;
    public double DropQuantity { get; init; } = 1;
    public double DropChance { get; init; } = 1;
    public double Xp { get; init; } = 1;
    public int ExtraEnemyLevels { get; init; }
    public double EnemyDamage { get; init; } = 1;
    public double EnemyHealth { get; init; } = 1;
    public double EnemyArmor { get; init; } = 1;
    public double EnemySpeed { get; init; } = 1;
    public bool AllowElites { get; init; } = true;
    /// <summary>Abilities the host switched off (the original's issue #27: "crazy elites" toggle).</summary>
    public IReadOnlyCollection<EnemyAbility> DisabledAbilities { get; init; } = Array.Empty<EnemyAbility>();
    public LootLevelPolicy LootLevel { get; init; } = LootLevelPolicy.Highest;
    public int MinLootRarity { get; init; }
}
