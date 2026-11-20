namespace ChampionsOfTheSons.Core.Content;

// Content definitions, deserialized from content/*.json. Property names match the JSON (camelCase).
// IDs are stable strings; legacy* fields only point back at the original mod for reference.

public enum PlayerStatKind
{
    /// <summary>default + Σ adds (multipliers still apply on top).</summary>
    Add,
    /// <summary>default × Π mults (adds still apply before multiplying).</summary>
    Mult,
    /// <summary>On when default is true or at least one source enables it.</summary>
    Bool,
    /// <summary>(default + Σ adds) × (defaultMult × Π mults).</summary>
    Multi,
}

/// <summary>A value on the player's stat sheet (max health, crit chance, spell_blink_range...).</summary>
public sealed class PlayerStatDef
{
    public string Id { get; set; } = "";
    public string? LegacyName { get; set; }
    public PlayerStatKind Kind { get; set; }
    public double Default { get; set; }
    public double DefaultMult { get; set; } = 1;
    public bool Integer { get; set; }
    public bool Percent { get; set; }
    /// <summary>Clients need this value on the host (magic find, explosion damage...).</summary>
    public bool Synced { get; set; }
}

public enum EffectOp
{
    Add,
    Mult,
    Div,
    Enable,
    Disable,
}

/// <summary>How a rolled item-stat value x turns into the effect value.</summary>
public enum EffectTransform
{
    X,
    Round,
    Neg,
    NegRound,
    OneMinus,
    OnePlus,
}

/// <summary>One change to a player stat. Either a fixed <see cref="Value"/> or a <see cref="Transform"/> of the rolled value.</summary>
public sealed class StatEffect
{
    public string Stat { get; set; } = "";
    public EffectOp Op { get; set; }
    public double? Value { get; set; }
    public EffectTransform? Transform { get; set; }
    public double Scale { get; set; } = 1;

    /// <summary>The effect value for a rolled amount <paramref name="x"/> (ignored for fixed values).</summary>
    public double Resolve(double x)
    {
        if (Value is double v) return v;
        x *= Scale;
        return Transform switch
        {
            EffectTransform.Round => Math.Round(x, MidpointRounding.AwayFromZero),
            EffectTransform.Neg => -x,
            EffectTransform.NegRound => -Math.Round(x, MidpointRounding.AwayFromZero),
            EffectTransform.OneMinus => 1 - x,
            EffectTransform.OnePlus => 1 + x,
            _ => x,
        };
    }
}

public enum StatCombine
{
    /// <summary>Several rolls of this stat add up.</summary>
    Sum,
    /// <summary>Π(x) - 1.</summary>
    Product,
    /// <summary>Π(1 + x).</summary>
    ProductPlusOne,
    /// <summary>1 - Π(1 - x): diminishing returns (dodge, damage reduction).</summary>
    OneMinusProduct,
}

/// <summary>A stat that can roll on an item (Strength, Maximum Life, Socket: Crit Chance...).</summary>
public sealed class ItemStatDef
{
    public string Id { get; set; } = "";
    public int LegacyId { get; set; }
    public string Name { get; set; } = "";
    public double Min { get; set; }
    public double Max { get; set; }
    /// <summary>Exponent applied to item level when scaling the roll.</summary>
    public double LevelPow { get; set; } = 1;
    public StatCombine Combine { get; set; }
    /// <summary>COTF's rarity field for the stat. Kept as data only: COTF's "*" slots offer every common stat whatever the item's
    /// rarity, and no roll here gates on it.</summary>
    public int Rarity { get; set; }
    public bool Percent { get; set; }
    public int Rounding { get; set; }
    public double? Cap { get; set; }
    /// <summary>Per-stat roll multiplier (sockets use it).</summary>
    public double Multiplier { get; set; } = 1;
    public List<StatEffect> Effects { get; set; } = new();
    /// <summary>Needs hand-written behavior (see COS-17); has no data effects yet.</summary>
    public bool Manual { get; set; }
    public string? ConsumeAction { get; set; }
}

/// <summary>A base item. Each entry in <see cref="StatPools"/> is one stat slot: the slot rolls one of its options.
/// null = the slot may stay empty, "*" = any stat allowed for the item's rarity.</summary>
public sealed class ItemBaseDef
{
    public string Id { get; set; } = "";
    public int LegacyIndex { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Subtype { get; set; }
    public int Rarity { get; set; }
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 1;
    public int StackSize { get; set; } = 1;
    public bool Consumable { get; set; }
    public List<List<string?>> StatPools { get; set; } = new();
    /// <summary>Per-slot roll multipliers (COTF's `PossibleStats[slot][i].Multipier`): when <see cref="SlotMultiplier.Slot"/>
    /// rolls <see cref="SlotMultiplier.Stat"/>, this value replaces the stat's own multiplier. Often negative (heavy weapons
    /// roll attack speed as a penalty).</summary>
    public List<SlotMultiplier> StatMultipliers { get; set; } = new();
    public string? WeaponModel { get; set; }
    public string? Set { get; set; }
    public List<StatEffect> UniqueEffects { get; set; } = new();
    public bool Manual { get; set; }
    public string? ConsumeAction { get; set; }

    public const string AnyStat = "*";

    /// <summary>The multiplier that replaces the stat's own when slot <paramref name="slot"/> rolls <paramref name="statId"/>, or null.</summary>
    public double? MultiplierFor(int slot, string statId)
    {
        foreach (var m in StatMultipliers)
            if (m.Slot == slot && m.Stat == statId) return m.Multiplier;
        return null;
    }
}

public sealed class SlotMultiplier
{
    public int Slot { get; set; }
    public string Stat { get; set; } = "";
    public double Multiplier { get; set; } = 1;
}

public enum PerkCategory
{
    MeleeOffense,
    RangedOffense,
    MagicOffense,
    Defense,
    Support,
    Utility,
}

/// <summary>A perk in the tree. <see cref="UnlockPath"/>: any one bought (empty = root). <see cref="Requires"/>: all bought.</summary>
public sealed class PerkDef
{
    public string Id { get; set; } = "";
    public int LegacyIndex { get; set; }
    public string Name { get; set; } = "";
    public PerkCategory Category { get; set; }
    public int Cost { get; set; } = 1;
    public int LevelReq { get; set; } = 1;
    public bool Stackable { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public List<string> UnlockPath { get; set; } = new();
    public List<string> Requires { get; set; } = new();
    public List<StatEffect> Effects { get; set; } = new();
    public bool Manual { get; set; }
    public bool CanBuyManual { get; set; }
}

public sealed class SpellDef
{
    public string Id { get; set; } = "";
    public int LegacyId { get; set; }
    public string Name { get; set; } = "";
    public int LevelReq { get; set; } = 1;
    public double EnergyCost { get; set; }
    public double Cooldown { get; set; }
    /// <summary>Costs energy per second while held.</summary>
    public bool Channeled { get; set; }
    /// <summary>On/off spell with an upkeep instead of a cooldown cast.</summary>
    public bool Toggle { get; set; }
}

/// <summary>An item set: equipping several pieces (items whose <see cref="ItemBaseDef.Set"/> is this id) unlocks bonuses.</summary>
public sealed class SetDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<SetBonus> Bonuses { get; set; } = new();
}

public sealed class SetBonus
{
    /// <summary>Pieces needed; every bonus with Pieces ≤ equipped count applies.</summary>
    public int Pieces { get; set; }
    /// <summary>The bonus applies once for every equipped piece of the set, not once (COTF's per-piece set bonuses, COS-88).</summary>
    public bool PerPiece { get; set; }
    public List<StatEffect> Effects { get; set; } = new();
}
