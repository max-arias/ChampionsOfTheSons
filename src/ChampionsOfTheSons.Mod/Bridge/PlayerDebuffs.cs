using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>The local player's buffs and debuffs (COS-89): one <see cref="PlayerBuffs"/> on the character's stat sheet, rebuilt when the
/// character is (a load). Slows, stun, root, silence, poison, energy leak, armor corruption and the timed immunities all live here.</summary>
public static class PlayerDebuffs
{
    private static PlayerBuffs? _buffs;
    private static StatSheet? _sheet;

    public static PlayerBuffs Buffs
    {
        get
        {
            var stats = PlayerState.Character.Stats;
            if (_buffs == null || !ReferenceEquals(_sheet, stats))
            {
                _buffs?.Clear();
                _sheet = stats;
                _buffs = new PlayerBuffs(stats, ModContent.Db.Buffs);
            }
            return _buffs;
        }
    }

    public const string MoveSlow = "buff.move_slow", AttackSlow = "buff.attack_slow", Poison = "buff.poison", DamageDown = "buff.damage_down",
        EnergyLeak = "buff.energy_leak", ArmorCorruption = "buff.armor_corruption", Root = "buff.root", Stun = "buff.stun", Silence = "buff.silence",
        StunImmune = "buff.stun_immune", RootImmune = "buff.root_immune", DebuffImmune = "buff.debuff_immune", DebuffResistant = "buff.debuff_resistant";

    /// <summary>Two or more poisons slow the player (COTF).</summary>
    public const double PoisonsSlow = 0.7;

    /// <summary>Movement multiplier from every slow (and two poisons).</summary>
    public static float SpeedMultiplier => (float)(Buffs.Product(MoveSlow) * (Buffs.CountOf(Poison) >= 2 ? PoisonsSlow : 1));

    /// <summary>Armor removed by corruption, for incoming hits.</summary>
    public static double LostArmor => Buffs.Total(ArmorCorruption);
}
