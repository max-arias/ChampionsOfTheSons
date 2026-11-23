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
    public bool AddToBag(ItemInstance item, int stackSize = 1) => AddToBag(item, stackSize, out _);

    /// <summary>As above, but reports what could not be placed (null when everything fitted).</summary>
    public bool AddToBag(ItemInstance item, int stackSize, out ItemInstance? remainder)
    {
        remainder = null;
        int startAmount = item.Amount;
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
        remainder = item;
        if (item.Amount != startAmount) Changed?.Invoke();
        return false;
    }

    /// <summary>Removes one unit of the stack in a slot, in place (the slot empties at the last unit).</summary>
    public void TakeOne(string slot)
    {
        if (!_slots.TryGetValue(slot, out var item)) return;
        if (item.Amount > 1) _slots[slot] = item with { Amount = item.Amount - 1 };
        else _slots.Remove(slot);
        Changed?.Invoke();
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

    /// <summary>Two equal stackable items (same base, level and stats) in the bag stack together.</summary>
    public static bool CanStack(ItemInstance a, ItemInstance b, ContentDb content) =>
        a.BaseId == b.BaseId && a.Level == b.Level && a.Stats.SequenceEqual(b.Stats)
        && content.Items.TryGetValue(a.BaseId, out var def) && def.StackSize > 1;

    /// <summary>COS-96: dragging a stack onto an equal stack in the bag merges them up to the stack size; what does not fit stays
    /// in <paramref name="from"/>. Anything else is a plain <see cref="Move"/> (a swap).</summary>
    public bool MoveOrMerge(string from, string to, ContentDb content)
    {
        var a = Get(from);
        var b = Get(to);
        if (a == null || b == null || from == to || !from.StartsWith("bag.", StringComparison.Ordinal) || !to.StartsWith("bag.", StringComparison.Ordinal)
            || !CanStack(a, b, content))
            return Move(from, to, content);
        int size = content.Items[a.BaseId].StackSize;
        int moved = Math.Min(size - b.Amount, a.Amount);
        if (moved <= 0) return Move(from, to, content); // the target stack is full: swap as before
        _slots[to] = b with { Amount = b.Amount + moved };
        if (a.Amount - moved > 0) _slots[from] = a with { Amount = a.Amount - moved };
        else _slots.Remove(from);
        Changed?.Invoke();
        return true;
    }

    /// <summary>COS-96: Split Stack: half the stack (rounded down) goes to the first free bag slot. False for a single item or a full bag.</summary>
    public bool Split(string slot)
    {
        var item = Get(slot);
        if (item == null || item.Amount < 2 || !slot.StartsWith("bag.", StringComparison.Ordinal)) return false;
        int half = item.Amount / 2;
        for (int i = 0; i < LootRoller.BagSlots; i++)
        {
            if (_slots.ContainsKey(Bag(i))) continue;
            _slots[Bag(i)] = item with { Amount = half };
            _slots[slot] = item with { Amount = item.Amount - half };
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>COS-96: takes <paramref name="amount"/> units out of a stack (partial drop); the rest stays. Null when the slot is empty.</summary>
    public ItemInstance? Take(string slot, int amount)
    {
        var item = Get(slot);
        if (item == null || amount <= 0) return null;
        if (amount >= item.Amount) return Remove(slot);
        _slots[slot] = item with { Amount = item.Amount - amount };
        Changed?.Invoke();
        return item with { Amount = amount };
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
        Spells = new SpellBook(content)
        {
            CooldownReduction = id => PerkTuning.CooldownReduction(Perks, id),
            CooldownScale = id => PerkTuning.CooldownScale(Perks, id),
            CostScale = (id, stats) => (id == "spell.multishot" && stats.IsOn(UniqueFlagIds.Sora) ? UniqueTuning.SoraCostScale : 1) * PerkRules.CostScale(id, stats),
            CostAdd = PerkRules.CostAdd,
            CooldownRateScale = PerkRules.CooldownRate,
        };
        Stats = new StatSheet(content);
        Inventory.Changed += RebuildEquipment;
        RebuildAll();
    }

    /// <summary>XP from a kill: paid at once before a streak starts, banked into the running streak after (COS-95).</summary>
    public LevelUpResult? OnKill(double xp)
    {
        double now = Massacre.OnKill(xp, Stats.Get(StatIds.MaxMassacreTime), Stats.Get(StatIds.TimeBonusPerKill));
        return now > 0 ? AddXp(now) : null;
    }

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

    /// <summary>Raised by <see cref="OnDeath"/> with the XP lost (HUD notice).</summary>
    public event Action<double>? Died;

    /// <summary>Player death: the XP penalty and the pending massacre streak are lost. Returns the XP lost from the bar.</summary>
    public double OnDeath()
    {
        double lost = Progress.OnDeath();
        Massacre.Reset();
        Died?.Invoke(lost);
        return lost;
    }

    /// <summary>Admin: sets the level. Perks are refunded when they cost more points than the new level gives.</summary>
    public void SetLevel(int level)
    {
        Progress.SetLevel(level);
        if (Perks.SpentPoints > Progress.TotalPerkPoints) Respec();
        RebuildLevel();
    }

    /// <summary>Whether a perk can be bought now, including the custom conditions of canBuyManual perks.</summary>
    public BuyResult CanBuyPerk(string perkId) => Perks.CanBuy(perkId, Progress, perk => PerkRules.ManualBuyCheck(perk, Stats));

    public BuyResult BuyPerk(string perkId)
    {
        var r = Perks.Buy(perkId, Progress, perk => PerkRules.ManualBuyCheck(perk, Stats));
        if (r == BuyResult.Ok) Stats.SetSource("perks", Perks.Modifiers());
        return r;
    }

    public void Respec()
    {
        Perks.Respec();
        Spells.Restore(Array.Empty<string?>());
        Stats.SetSource("perks", Perks.Modifiers());
    }

    /// <summary>Free refund of one perk tree (COS-170); perks elsewhere that depended on it go too.</summary>
    public int RespecTree(PerkCategory category)
    {
        int n = Perks.RespecCategory(category);
        Stats.SetSource("perks", Perks.Modifiers());
        return n;
    }

    /// <summary>Free refund of every spell (COS-170): unlearned and taken off the bar.</summary>
    public int RespecSpells()
    {
        int n = Perks.RespecSpells();
        Spells.Restore(Array.Empty<string?>());
        return n;
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
        LastGrantDay = Progress.LastGrantDay,
    };

    public void Load(PlayerSave save)
    {
        Progress = new Progress(save.Level, save.Xp, save.BonusPerkPoints) { LastGrantDay = save.LastGrantDay };
        Perks.Restore(save.Perks, save.UnlockedSpells);
        Spells.Restore(save.SpellLoadout);
        var loot = new LootRoller(Content);
        // An item's rarity is its base's (COS-109: uniques became Legendary, which a save migration cannot know).
        Inventory.Restore(save.Slots.Select(kv => new KeyValuePair<string, ItemInstance>(kv.Key, loot.InferSlots(WithBaseRarity(kv.Value.ToItem())))));
        Massacre.Reset();
        RebuildAll();
    }

    private ItemInstance WithBaseRarity(ItemInstance item) =>
        Content.Items.TryGetValue(item.BaseId, out var def) ? item with { Rarity = (Rarity)Rarities.Clamp(def.Rarity) } : item;
}
