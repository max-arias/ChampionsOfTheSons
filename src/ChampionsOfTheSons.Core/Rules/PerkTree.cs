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
