namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// The four unique items whose effects are events, not stats (content marks them <c>manual</c>). The original's
/// behavior text is not available here, so these are this mod's own rules, listed in docs/mechanics.md:
/// Gorged Ring leeches health (Biting Band was cut in COS-162), Healer's Blood Bag heals when low, Undying Promise cheats death,
/// Untainted Heart is a consumable respec.
/// </summary>
public static class UniqueItems
{
    public const string HeartOfPurity = "item.heart_of_purity";
    public const string BloodBag = "item.lama_mega_s_blood_bag";
    public const string VampireRing = "item.vampire_ring";
    public const string UndyingPromise = "item.undying_promise";

    public const double RingLifeSteal = 0.06, RingKillHeal = 0.05;
    public const double BloodBagBelow = 0.5, BloodBagPerSecond = 0.01;
    public const double UndyingCooldown = 300, UndyingHealth = 0.3;

    public static bool IsEquipped(Inventory inv, string baseId) => inv.Equipped.Any(kv => kv.Value.BaseId == baseId);

    /// <summary>Fraction of damage dealt returned as health.</summary>
    public static double LifeStealFraction(Inventory inv) =>
        IsEquipped(inv, VampireRing) ? RingLifeSteal : 0;

    /// <summary>Fraction of max health healed per kill.</summary>
    public static double KillHealFraction(Inventory inv) => IsEquipped(inv, VampireRing) ? RingKillHeal : 0;

    /// <summary>Fraction of max health regenerated per second at the given health fraction.</summary>
    public static double BloodBagRegen(Inventory inv, double healthFraction) =>
        IsEquipped(inv, BloodBag) && healthFraction < BloodBagBelow ? BloodBagPerSecond : 0;

}

/// <summary>Undying Promise: a lethal hit leaves 30% health instead, once per 5 minutes.</summary>
public sealed class UndyingPromiseState
{
    public double CooldownLeft { get; private set; }

    public void Tick(double dt) => CooldownLeft = Math.Max(0, CooldownLeft - dt);

    /// <summary>True (and starts the cooldown) when equipped, ready and <paramref name="damage"/> would kill.</summary>
    public bool TryTrigger(Inventory inv, double health, double damage)
    {
        if (CooldownLeft > 0 || damage < health || !UniqueItems.IsEquipped(inv, UniqueItems.UndyingPromise)) return false;
        CooldownLeft = UniqueItems.UndyingCooldown;
        return true;
    }

    public void Reset() => CooldownLeft = 0;
}
