namespace ChampionsOfTheSons.Core.Saving;

/// <summary>
/// Per-player progress, stored in the player's save (so each co-op client keeps their own character).
/// Plain DTO: everything is by stable content ID. Bump <see cref="CurrentVersion"/> and add a
/// migration in <see cref="SaveCodec"/> for any format change.
/// </summary>
public sealed class PlayerSave
{
    /// <summary>2: rolled stats carry their stat slot (COS-73). 3: deleted perks removed (COS-86/COS-87). 4: four rarities (COS-109).
    /// 5: crafting perks, materials and socket stats removed (COS-145). 6: the last daily-grant day (COS-100); the island
    /// foods removed (COS-147). 7: health fraction, carried game items above their limit, save time (COS-100).
    /// 8: spent attribute points (COS-174).</summary>
    public const int CurrentVersion = 8;

    public int Version { get; set; } = CurrentVersion;
    public double Xp { get; set; }
    public int Level { get; set; } = 1;
    public int BonusPerkPoints { get; set; }

    /// <summary>Inventory and equipment slots. Slot keys are stable names ("bag.0".."bag.n", "equip.ring1", ...).</summary>
    public Dictionary<string, SavedItem> Slots { get; set; } = new();

    /// <summary>Perk ID → times bought (1 for non-stackable perks).</summary>
    public Dictionary<string, int> Perks { get; set; } = new();

    public List<string> UnlockedSpells { get; set; } = new();

    /// <summary>Spell bar, one entry per slot; null = empty slot.</summary>
    public List<string?> SpellLoadout { get; set; } = new();

    /// <summary>The game day on which daily-grant perks last paid out; -1 = none yet (COS-100).</summary>
    public int LastGrantDay { get; set; } = -1;

    /// <summary>Health as a share of the modded max health at save time; -1 = not saved. Restored once stats are rebuilt after a load,
    /// so a max health raised by the mod does not come back capped at the game's own (COS-100).</summary>
    public double HealthFraction { get; set; } = -1;

    /// <summary>Game item id → count carried, for items whose carry limit the mod raises (perks, worn carry stats). The game trims
    /// anything over its own limit while loading; the missing part is given back after load (COS-100, COTF issues #12, #23).</summary>
    public Dictionary<int, int> CarriedItems { get; set; } = new();

    /// <summary>Unix seconds when this save was written; 0 = unknown. A co-op client keeps a local copy and loads whichever is newer.</summary>
    public long SavedAt { get; set; }

    /// <summary>Attribute id → points spent on it (COS-174). Missing = none spent: an older save gets all its points to spend.</summary>
    public Dictionary<string, int> Attributes { get; set; } = new();
}

public sealed class SavedItem
{
    public string BaseId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Rarity { get; set; }
    public int Amount { get; set; } = 1;
    public int Upgrades { get; set; }
    public List<SavedStat> Stats { get; set; } = new();

    public static SavedItem From(ItemInstance item) => new()
    {
        BaseId = item.BaseId,
        Level = item.Level,
        Rarity = (int)item.Rarity,
        Amount = item.Amount,
        Upgrades = item.Upgrades,
        Stats = item.Stats.Select(s => new SavedStat { Id = s.StatId, Value = s.Value, Slot = s.Slot }).ToList(),
    };

    public ItemInstance ToItem() => new()
    {
        BaseId = BaseId,
        Level = Level,
        Rarity = (Rarity)Rarity,
        Amount = Amount,
        Upgrades = Upgrades,
        Stats = Stats.Select(s => new RolledStat(s.Id, s.Value, s.Slot)).ToArray(),
    };
}

public sealed class SavedStat
{
    public string Id { get; set; } = "";
    public float Value { get; set; }
    /// <summary>Index of the base item's stat pool the stat was rolled from; -1 for sockets or not known (inferred on load).</summary>
    public int Slot { get; set; } = -1;
}

/// <summary>World-wide settings, stored in the world save and owned by the host.</summary>
public sealed class WorldSave
{
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    public bool FriendlyFire { get; set; }

    /// <summary>Which player's level drops roll at (<see cref="Rules.LootLevelPolicy"/> by name). Saved since version 4 (COS-77).</summary>
    public string LootLevel { get; set; } = nameof(Rules.LootLevelPolicy.Highest);

    // Host multipliers and switches, saved so they survive a reload (the original's issue #29).
    public double DropQuantity { get; set; } = 1;
    public double DropChance { get; set; } = 1;
    public double Xp { get; set; } = 1;
    public int ExtraEnemyLevels { get; set; }
    public double EnemyDamage { get; set; } = 1;
    public double EnemyHealth { get; set; } = 1;
    public double EnemyArmor { get; set; } = 1;
    public double EnemySpeed { get; set; } = 1;
    public bool AllowElites { get; set; } = true;
    public List<string> DisabledAbilities { get; set; } = new();
    public int MinLootRarity { get; set; }
}
