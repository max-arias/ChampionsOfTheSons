using System.Reflection;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;
using RedLoader;

namespace ChampionsOfTheSons.Mod;

/// <summary>Content embedded in the mod DLL (content/*.json), loaded once.</summary>
public static class ModContent
{
    public static ContentDb Db { get; private set; } = null!;

    public static void Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var files = new Dictionary<string, string>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("content/", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files[name.Substring("content/".Length)] = reader.ReadToEnd();
        }
        Db = ContentDb.Load(files);
        Text = TextDb.Load(files);
        foreach (var error in Db.Errors.Concat(Text.Errors)) RLog.Warning($"Champions of the Sons content: {error}");
    }

    public static TextDb Text { get; private set; } = null!;
}

/// <summary>The local player's character.</summary>
public static class PlayerState
{
    private static PlayerCharacter? _character;

    public static PlayerCharacter Character => _character ??= new PlayerCharacter(ModContent.Db);

    public static PlayerSave ToSave() => Character.ToSave();

    public static void FromSave(PlayerSave save) => Character.Load(save);
}

/// <summary>World rules owned by the host (difficulty and multipliers).</summary>
public static class WorldState
{
    public static WorldRules Rules { get; private set; } = new();

    /// <summary>False until the host has picked a difficulty for this world.</summary>
    public static bool DifficultyChosen { get; private set; }

    public static bool FriendlyFire { get; private set; }

    public static void SetDifficulty(DifficultyTier tier)
    {
        Rules = Rules with { Difficulty = tier };
        DifficultyChosen = true;
    }

    public static WorldSave ToSave() => new()
    {
        Difficulty = DifficultyChosen ? Rules.Difficulty.Id : null,
        FriendlyFire = FriendlyFire,
        DropQuantity = Rules.DropQuantity,
        DropChance = Rules.DropChance,
        Xp = Rules.Xp,
        ExtraEnemyLevels = Rules.ExtraEnemyLevels,
        EnemyDamage = Rules.EnemyDamage,
        EnemyHealth = Rules.EnemyHealth,
        EnemyArmor = Rules.EnemyArmor,
        EnemySpeed = Rules.EnemySpeed,
        AllowElites = Rules.AllowElites,
        DisabledAbilities = Rules.DisabledAbilities.Select(a => a.ToString()).ToList(),
        MinLootRarity = Rules.MinLootRarity,
    };

    public static void FromSave(WorldSave save)
    {
        var tier = Difficulties.ById(save.Difficulty);
        DifficultyChosen = tier != null;
        Rules = new WorldRules
        {
            Difficulty = tier ?? Difficulties.Easy,
            DropQuantity = save.DropQuantity,
            DropChance = save.DropChance,
            Xp = save.Xp,
            ExtraEnemyLevels = save.ExtraEnemyLevels,
            EnemyDamage = save.EnemyDamage,
            EnemyHealth = save.EnemyHealth,
            EnemyArmor = save.EnemyArmor,
            EnemySpeed = save.EnemySpeed,
            AllowElites = save.AllowElites,
            DisabledAbilities = save.DisabledAbilities
                .Select(n => Enum.TryParse<EnemyAbility>(n, out var a) ? (EnemyAbility?)a : null)
                .Where(a => a.HasValue).Select(a => a!.Value).ToArray(),
            MinLootRarity = save.MinLootRarity,
        };
        FriendlyFire = save.FriendlyFire;
    }
}
