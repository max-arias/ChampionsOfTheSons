using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

public enum BuyResult
{
    Ok,
    UnknownPerk,
    NotEnoughPoints,
    LevelTooLow,
    PathLocked,
    RequirementsMissing,
    AlreadyBought,
    NotBuyable,
}

/// <summary>
/// Bought perks and spells, and the perk-point budget they share. Points = level + permanent bonus;
/// perks cost their <see cref="PerkDef.Cost"/> per purchase, spells cost <see cref="SpellUnlockCost"/>.
/// </summary>
public sealed class PerkTree
{
    public const int SpellUnlockCost = 2;

    private readonly ContentDb _content;
    private readonly Dictionary<string, int> _bought = new();
    private readonly HashSet<string> _spells = new();

    public PerkTree(ContentDb content) => _content = content;

    public IReadOnlyDictionary<string, int> Bought => _bought;
    public IReadOnlyCollection<string> UnlockedSpells => _spells;

    public int SpentPoints =>
        _bought.Sum(kv => (_content.Perks.TryGetValue(kv.Key, out var p) ? p.Cost : 0) * kv.Value) + _spells.Count * SpellUnlockCost;

    public int AvailablePoints(Progress progress) => progress.TotalPerkPoints - SpentPoints;

    public int TimesBought(string perkId) => _bought.TryGetValue(perkId, out var n) ? n : 0;

    /// <summary>Why a perk can or can't be bought. <paramref name="extraCheck"/> covers perks with a custom condition.</summary>
    public BuyResult CanBuy(string perkId, Progress progress, Func<PerkDef, bool>? extraCheck = null)
    {
        if (!_content.Perks.TryGetValue(perkId, out var perk)) return BuyResult.UnknownPerk;
        if (TimesBought(perkId) > 0 && !perk.Stackable) return BuyResult.AlreadyBought;
        if (progress.Level < perk.LevelReq) return BuyResult.LevelTooLow;
        if (perk.UnlockPath.Count > 0 && !perk.UnlockPath.Any(p => TimesBought(p) > 0)) return BuyResult.PathLocked;
        if (perk.Requires.Any(p => TimesBought(p) == 0)) return BuyResult.RequirementsMissing;
        if (perk.CanBuyManual && extraCheck != null && !extraCheck(perk)) return BuyResult.NotBuyable;
        if (AvailablePoints(progress) < perk.Cost) return BuyResult.NotEnoughPoints;
        return BuyResult.Ok;
    }

    public BuyResult Buy(string perkId, Progress progress, Func<PerkDef, bool>? extraCheck = null)
    {
        var result = CanBuy(perkId, progress, extraCheck);
        if (result == BuyResult.Ok) _bought[perkId] = TimesBought(perkId) + 1;
        return result;
    }

    public BuyResult CanUnlockSpell(string spellId, Progress progress)
    {
        if (!_content.Spells.TryGetValue(spellId, out var spell)) return BuyResult.UnknownPerk;
        if (_spells.Contains(spellId)) return BuyResult.AlreadyBought;
        if (progress.Level < spell.LevelReq) return BuyResult.LevelTooLow;
        if (AvailablePoints(progress) < SpellUnlockCost) return BuyResult.NotEnoughPoints;
        return BuyResult.Ok;
    }

    public BuyResult UnlockSpell(string spellId, Progress progress)
    {
        var result = CanUnlockSpell(spellId, progress);
        if (result == BuyResult.Ok) _spells.Add(spellId);
        return result;
    }

    /// <summary>Respec: unbuy every perk and spell.</summary>
    public void Respec()
    {
        _bought.Clear();
        _spells.Clear();
    }

    /// <summary>Free respec of one tree (COS-170): unbuys every perk of <paramref name="category"/>, then any perk in another tree whose unlock
    /// path or requirements no longer hold, so what stays bought is still reachable. Spells stay. Returns how many perks were unbought.</summary>
    public int RespecCategory(PerkCategory category)
    {
        int removed = 0;
        foreach (var id in _bought.Keys.Where(id => _content.Perks.TryGetValue(id, out var p) && p.Category == category).ToList())
        {
            _bought.Remove(id);
            removed++;
        }
        return removed + DropUnreachable();
    }

    /// <summary>Free respec of the spells (COS-170): every unlocked spell is unlearned. Returns how many.</summary>
    public int RespecSpells()
    {
        int n = _spells.Count;
        _spells.Clear();
        return n;
    }

    private int DropUnreachable()
    {
        int removed = 0;
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var id in _bought.Keys.ToList())
            {
                if (!_content.Perks.TryGetValue(id, out var p)) continue; // unknown ids are kept, like Restore does
                bool path = p.UnlockPath.Count == 0 || p.UnlockPath.Any(x => TimesBought(x) > 0);
                bool requires = p.Requires.All(x => TimesBought(x) > 0);
                if (path && requires) continue;
                _bought.Remove(id);
                removed++;
                changed = true;
            }
        }
        return removed;
    }

    /// <summary>Restores saved state without checks (unknown IDs are kept so a content update can't wipe them).</summary>
    public void Restore(IReadOnlyDictionary<string, int> perks, IEnumerable<string> spells)
    {
        _bought.Clear();
        foreach (var (id, n) in perks)
            if (n > 0) _bought[id] = n;
        _spells.Clear();
        foreach (var s in spells) _spells.Add(s);
    }

    /// <summary>Stat modifiers from all bought perks (stackable perks apply once per purchase).</summary>
    public List<StatModifier> Modifiers()
    {
        var mods = new List<StatModifier>();
        foreach (var (id, times) in _bought)
        {
            if (!_content.Perks.TryGetValue(id, out var perk)) continue;
            for (int i = 0; i < times; i++)
                foreach (var e in perk.Effects)
                    mods.Add(ItemEffects.ToModifier(e, 0));
        }
        return mods;
    }
}
