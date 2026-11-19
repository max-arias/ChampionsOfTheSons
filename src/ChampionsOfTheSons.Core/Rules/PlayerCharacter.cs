using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Saving;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Bag and equipment. Slot keys: "bag.0".."bag.119" and <see cref="LootRoller.EquipSlots"/>.</summary>
public sealed class Inventory
{
    private readonly Dictionary<string, ItemInstance> _slots = new();

    public event Action? Changed;

    public IReadOnlyDictionary<string, ItemInstance> Slots => _slots;

    public static string Bag(int i) => "bag." + i;

    public ItemInstance? Get(string slot) => _slots.TryGetValue(slot, out var item) ? item : null;

    public IEnumerable<KeyValuePair<string, ItemInstance>> Equipped => _slots.Where(kv => kv.Key.StartsWith("equip.", StringComparison.Ordinal));

    /// <summary>Puts an item in the first free bag slot. Stacks onto an equal stackable item first. False if the bag is full.</summary>
    public bool AddToBag(ItemInstance item, int stackSize = 1)
    {
        if (stackSize > 1)
        {
            foreach (var (slot, existing) in _slots.ToList())
            {
                if (!slot.StartsWith("bag.", StringComparison.Ordinal) || existing.BaseId != item.BaseId || existing.Amount >= stackSize) continue;
                if (!existing.Stats.SequenceEqual(item.Stats) || existing.Level != item.Level) continue;
                int moved = Math.Min(stackSize - existing.Amount, item.Amount);
                _slots[slot] = existing with { Amount = existing.Amount + moved };
                item = item with { Amount = item.Amount - moved };
                if (item.Amount <= 0) { Changed?.Invoke(); return true; }
            }
        }
        for (int i = 0; i < LootRoller.BagSlots; i++)
        {
            if (_slots.ContainsKey(Bag(i))) continue;
            _slots[Bag(i)] = item;
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    public ItemInstance? Remove(string slot)
    {
        if (!_slots.Remove(slot, out var item)) return null;
        Changed?.Invoke();
        return item;
    }

    /// <summary>Swaps two slots. Moving into an equipment slot requires an item type that fits it.</summary>
    public bool Move(string from, string to, ContentDb content)
    {
        var a = Get(from);
        var b = Get(to);
        if (a == null || from == to) return false;
        if (!Fits(a, to, content) || (b != null && !Fits(b, from, content))) return false;
        _slots[to] = a;
        if (b != null) _slots[from] = b;
        else _slots.Remove(from);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Equips a bag item into its slot (second ring slot if the first is taken), swapping out what was there.</summary>
    public bool Equip(string bagSlot, ContentDb content)
    {
        var item = Get(bagSlot);
        if (item == null || !content.Items.TryGetValue(item.BaseId, out var def)) return false;
        var slots = LootRoller.SlotsFor(def.Type);
        if (slots.Count == 0) return false;
        var target = slots.FirstOrDefault(s => !_slots.ContainsKey(s)) ?? slots[0];
        return Move(bagSlot, target, content);
    }

    public static bool Fits(ItemInstance item, string slot, ContentDb content)
    {
        if (slot.StartsWith("bag.", StringComparison.Ordinal))
            return int.TryParse(slot.AsSpan(4), out var i) && i >= 0 && i < LootRoller.BagSlots;
        return content.Items.TryGetValue(item.BaseId, out var def) && LootRoller.SlotsFor(def.Type).Contains(slot);
    }

    public void Restore(IEnumerable<KeyValuePair<string, ItemInstance>> slots)
    {
        _slots.Clear();
        foreach (var (k, v) in slots) _slots[k] = v;
        Changed?.Invoke();
    }
}

/// <summary>
/// Everything about one player's character, game-agnostic: progress, inventory, perks, spells, kill streak
/// and the resolved stat sheet. The mod drives it from game events and reads the sheet to change the game.
/// </summary>
public sealed class PlayerCharacter
{
    public ContentDb Content { get; }
    public Progress Progress { get; private set; } = new();
    public Inventory Inventory { get; } = new();
    public PerkTree Perks { get; }
    public SpellBook Spells { get; }
    public StatSheet Stats { get; }
    public Massacre Massacre { get; } = new();

    /// <summary>Raised by <see cref="AddXp"/> when it leveled the character up (HUD notice).</summary>
    public event Action<LevelUpResult>? LeveledUp;

    public PlayerCharacter(ContentDb content)
    {
        Content = content;
        Perks = new PerkTree(content);
        Spells = new SpellBook(content);
        Stats = new StatSheet(content);
        Inventory.Changed += RebuildEquipment;
        RebuildAll();
    }

    /// <summary>XP from a kill: banked into the massacre streak (paid when it ends).</summary>
    public void OnKill(double xp) =>
        Massacre.OnKill(xp, Stats.Get(StatIds.MaxMassacreTime), Stats.Get(StatIds.TimeBonusPerKill));

    /// <summary>XP that skips the streak (trees, bushes, consumables). Returns what changed.</summary>
    public LevelUpResult AddXp(double xp)
    {
        var result = Progress.AddXp(xp, Stats.Get(StatIds.ExpGain));
        if (result.LeveledUp)
        {
            RebuildLevel();
            LeveledUp?.Invoke(result);
        }
        return result;
    }

    /// <summary>Advances timers; returns a level-up result when a streak paid out.</summary>
    public LevelUpResult? Tick(double dt)
    {
        Spells.Tick(dt, Stats);
        double payout = Massacre.Tick(dt);
        return payout > 0 ? AddXp(payout) : null;
    }

    public void OnDeath()
    {
        Progress.OnDeath();
        Massacre.Reset();
    }

    public BuyResult BuyPerk(string perkId)
    {
        var r = Perks.Buy(perkId, Progress);
        if (r == BuyResult.Ok) Stats.SetSource("perks", Perks.Modifiers());
        return r;
    }

    public void Respec()
    {
        Perks.Respec();
        Spells.Restore(Array.Empty<string?>());
        Stats.SetSource("perks", Perks.Modifiers());
    }

    public void RebuildAll()
    {
        RebuildLevel();
        RebuildEquipment();
        Stats.SetSource("perks", Perks.Modifiers());
    }

    private void RebuildLevel()
    {
        double gain = XpCurve.AttributeBase(Progress.Level) - 2; // Σ round(√x) for x = 2..level: 0 at level 1
        Stats.SetSource("level", new[]
        {
            new StatModifier(StatIds.Strength, EffectOp.Add, gain),
            new StatModifier(StatIds.Agility, EffectOp.Add, gain),
            new StatModifier(StatIds.Vitality, EffectOp.Add, gain),
            new StatModifier(StatIds.Intelligence, EffectOp.Add, gain),
        });
    }

    private void RebuildEquipment()
    {
        foreach (var source in Stats.Sources.Where(s => s.StartsWith("equip.", StringComparison.Ordinal)).ToList())
            Stats.RemoveSource(source);
        foreach (var (slot, item) in Inventory.Equipped)
            Stats.SetSource(slot, ItemEffects.For(item, Content));
        Stats.SetSource("sets", ItemEffects.SetBonuses(Inventory.Equipped.Select(kv => kv.Value), Content));
    }

    public PlayerSave ToSave() => new()
    {
        Xp = Progress.Xp,
        Level = Progress.Level,
        BonusPerkPoints = Progress.BonusPerkPoints,
        Slots = Inventory.Slots.ToDictionary(kv => kv.Key, kv => SavedItem.From(kv.Value)),
        Perks = Perks.Bought.ToDictionary(kv => kv.Key, kv => kv.Value),
        UnlockedSpells = Perks.UnlockedSpells.ToList(),
        SpellLoadout = Spells.Loadout.ToList(),
    };

    public void Load(PlayerSave save)
    {
        Progress = new Progress(save.Level, save.Xp, save.BonusPerkPoints);
        Perks.Restore(save.Perks, save.UnlockedSpells);
        Spells.Restore(save.SpellLoadout);
        Inventory.Restore(save.Slots.Select(kv => new KeyValuePair<string, ItemInstance>(kv.Key, kv.Value.ToItem())));
        Massacre.Reset();
        RebuildAll();
    }
}
