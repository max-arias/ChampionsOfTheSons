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

    /// <summary>Built on first use. Touching it before <see cref="ModContent.Load"/> throws instead of caching a character with no
    /// content, which would fail on every later stat read (COS-161).</summary>
    public static PlayerCharacter Character => _character ??= new PlayerCharacter(ModContent.Db
        ?? throw new InvalidOperationException("PlayerState.Character used before content was loaded"));

    public static PlayerSave ToSave()
    {
        var save = Character.ToSave();
        Weapons.PolearmThrow.AddToSave(save); // a thrown weapon lying in the world is saved as owned
        Saves.SaveExtrasFeature.Capture(save);
        return save;
    }

    public static void FromSave(PlayerSave save) => Character.Load(save);
}

/// <summary>World rules owned by the host (the game's difficulty as read from the game, and the host's multipliers).</summary>
public static class WorldState
{
    public static WorldRules Rules { get; private set; } = new();

    /// <summary>COS-213: the game world's own PvP damage setting (the host reads it from the game, clients get it from the host).</summary>
    public static PvpMode Pvp { get; private set; }

    /// <summary>Players can hurt each other (the game's PvP damage is not Off).</summary>
    public static bool FriendlyFire => Pvp != PvpMode.Off;

    public static void SetPvp(PvpMode mode)
    {
        if (Pvp == mode) return;
        Pvp = mode;
        Changed?.Invoke();
    }

    /// <summary>COS-130: the game's difficulty (the host reads it from the game, clients get it from the host). Raises <see cref="Changed"/> only when it differs.</summary>
    public static void SetGameDifficulty(GameDifficulty game)
    {
        if (Rules.GameDifficulty == game) return;
        Rules = Rules with { GameDifficulty = game };
        Changed?.Invoke();
    }

    /// <summary>Changes the host's multipliers and switches (host settings panel).</summary>
    public static void Update(Func<WorldRules, WorldRules> change) => Rules = change(Rules);

    /// <summary>Raised when the game's difficulty or any host setting changed.</summary>
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>Self-tests only: swap rules.</summary>
    public static void SetRulesForTest(WorldRules rules) => Rules = rules;

    public static WorldSave ToSave() => new()
    {
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
        LootLevel = Rules.LootLevel.ToString(),
    };

    /// <summary>Loads the host settings; the game's difficulty is not in the save (the game keeps its own) and stays as it is.</summary>
    public static void FromSave(WorldSave save)
    {
        Rules = new WorldRules
        {
            GameDifficulty = Rules.GameDifficulty,
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
            LootLevel = Enum.TryParse<LootLevelPolicy>(save.LootLevel, true, out var policy) ? policy : LootLevelPolicy.Highest,
        };
        // WorldSave.FriendlyFire (the old mod toggle) is no longer read: the game's own PvP setting decides (COS-213).
    }
}
