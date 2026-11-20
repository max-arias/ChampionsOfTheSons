using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Items the bag's Use action consumes (COTF's Consume). An item is usable when it is the respec heart or carries a stat
/// with a use effect; using it removes one unit and runs each effect with the rolled value.
/// </summary>
public static class Consumables
{
    /// <summary>+round(value) permanent perk points: they count toward the total, so they are spendable now and survive a respec.</summary>
    public const string PerkPointStat = "stat.permanentperkpoints";
    /// <summary>Grants the rolled value as XP (30 × level³ × rarity multiplier) through the normal XP path (XP gain applies).</summary>
    public const string ExperienceStat = "stat.experience";

    public static bool CanUse(ItemInstance item) =>
        item.BaseId == UniqueItems.HeartOfPurity || item.Stats.Any(s => s.StatId is PerkPointStat or ExperienceStat);

    /// <summary>Uses one unit from a bag slot. False (nothing changes) when the slot is empty, not a bag slot or not usable.</summary>
    public static bool Use(PlayerCharacter pc, string bagSlot)
    {
        if (!bagSlot.StartsWith("bag.", StringComparison.Ordinal)) return false;
        var item = pc.Inventory.Get(bagSlot);
        if (item == null || !CanUse(item)) return false;
        if (item.BaseId == UniqueItems.HeartOfPurity && pc.Perks.SpentPoints == 0) return false; // nothing to respec: keep the heart
        pc.Inventory.TakeOne(bagSlot);
        foreach (var stat in item.Stats)
        {
            if (stat.StatId == PerkPointStat) pc.Progress.BonusPerkPoints += (int)Math.Round(stat.Value, MidpointRounding.AwayFromZero);
            else if (stat.StatId == ExperienceStat) pc.AddXp(stat.Value);
        }
        if (item.BaseId == UniqueItems.HeartOfPurity) pc.Respec();
        return true;
    }
}
