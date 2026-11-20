namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// One of the 11 level bands (COTF's difficulty tiers). Since COS-130 nobody picks one: an enemy's band is the highest one whose
/// <see cref="BaseLevelMin"/> its level reaches (<see cref="Difficulties.ForLevel"/>), and the band gives the per-level tables
/// (armor factor, shielding cooldown, Meteor and Laser damage, elite ability count, ability gating, loot rarity).
/// <see cref="Index"/> is d in the formulas (0 = Easy, 10 = Hell).
/// </summary>
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

    /// <summary>The band of an enemy of this level: the highest band whose minimum base level it reaches.</summary>
    public static DifficultyTier ForLevel(int level)
    {
        var band = Easy;
        foreach (var d in All)
            if (level >= d.BaseLevelMin) band = d;
        return band;
    }
}

/// <summary>The game's own difficulty (Sons of the Forest's game mode). Values match the game's <c>Sons.Gameplay.GameSetup.Mode</c>.</summary>
public enum GameMode : byte
{
    Normal = 0,
    Hard = 1,
    HardSurvival = 2,
    Peaceful = 3,
    Creative = 4,
    Custom = 5,
}

/// <summary>A Low / Normal / High slider of the game's custom setup.</summary>
public enum GameSlider : byte
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>
/// COS-130: what the game's difficulty adds to enemies. Enemy levels follow the party (<see cref="EnemyLevels"/>); the game's mode
/// adds <see cref="LevelOffset"/> levels and raises the elite chance. Read by the host from the game and sent to clients.
/// </summary>
public sealed record GameDifficulty(GameMode Mode, int LevelOffset)
{
    public static readonly GameDifficulty Normal = For(GameMode.Normal);

    /// <summary>Chance that a normal enemy kind rolls as an elite: 10% plus 1% per offset level, 5% to 25%.</summary>
    public double EliteChance => Math.Clamp(0.10 + 0.01 * LevelOffset, 0.05, 0.25);

    /// <summary>Peaceful −2, Normal and Creative 0, Hard +5, Hard Survival +10, Custom from its enemy sliders.</summary>
    public static GameDifficulty For(GameMode mode, GameSlider health = GameSlider.Normal, GameSlider damage = GameSlider.Normal,
        GameSlider armour = GameSlider.Normal, GameSlider aggression = GameSlider.Normal) => new(mode, mode switch
    {
        GameMode.Peaceful => -2,
        GameMode.Hard => 5,
        GameMode.HardSurvival => 10,
        GameMode.Custom => CustomOffset(health, damage, armour, aggression),
        _ => 0,
    });

    /// <summary>Custom game: each of enemy health, damage, armour and aggression adds −1 at Low and +2.5 at High (all High = Hard Survival's +10).</summary>
    public static int CustomOffset(GameSlider health, GameSlider damage, GameSlider armour, GameSlider aggression)
    {
        static double Of(GameSlider s) => s switch { GameSlider.Low => -1, GameSlider.High => 2.5, _ => 0 };
        return (int)Math.Round(Of(health) + Of(damage) + Of(armour) + Of(aggression), MidpointRounding.AwayFromZero);
    }
}

/// <summary>COS-130: enemies scale with the players around them.</summary>
public static class EnemyLevels
{
    /// <summary>Players this close to an enemy when it is rolled make up its party.</summary>
    public const float PartyRange = 150f;

    /// <summary>
    /// The party level an enemy is rolled against: the average level of the players within <see cref="PartyRange"/>, rounded up;
    /// with nobody in range, the average of all players; with no players at all (a dedicated server before anyone joined), 1.
    /// </summary>
    public static int PartyLevel(IReadOnlyCollection<(int Level, float Distance)> players, float range = PartyRange)
    {
        if (players.Count == 0) return 1;
        var near = players.Where(p => p.Distance <= range).ToList();
        var from = near.Count > 0 ? near : players.ToList();
        return Math.Max(1, (int)Math.Ceiling(from.Average(p => (double)Math.Max(1, p.Level)) - 1e-9));
    }
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
    /// <summary>The game's difficulty, read from the game by the host (not saved: the game saves its own).</summary>
    public GameDifficulty GameDifficulty { get; init; } = GameDifficulty.Normal;
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
    /// <summary>Admin spawn only (never saved or synced): every enemy rolled under these rules gets this tier.</summary>
    public EnemyTier? ForcedTier { get; init; }
    /// <summary>Admin and self-tests only (never saved or synced): enemies roll against this party level instead of the players'.</summary>
    public int? PartyLevelOverride { get; init; }
}
