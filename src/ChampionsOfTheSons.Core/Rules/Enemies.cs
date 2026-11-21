namespace ChampionsOfTheSons.Core.Rules;

public enum EnemyTier : byte
{
    Normal = 0,
    Elite = 1,
    Miniboss = 2,
    Boss = 3,
}

/// <summary>The 26 enemy abilities (affixes).</summary>
public enum EnemyAbility
{
    Steadfast,
    EliteSteadfast,
    BossSteadfast,
    Radiance,
    Chains,
    BlackHole,
    Trapper,
    Juggernaut,
    Gargantuan,
    Tiny,
    ExtraHealth,
    ExtraDamage,
    Basher,
    Blizzard,
    RainEmpowerment,
    Shielding,
    Meteor,
    Flare,
    Undead,
    Warp,
    Sacrifice,
    Avenger,
    Laser,
    Poisonous,
    ArcaneCataclysm,
    FireCataclysm,
}

/// <summary>
/// What the rules need to know about an enemy kind. The game bridge maps SotF actor types to these.
/// </summary>
public sealed record EnemyKind
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The game's actor type name (SotF VailActorTypeId, e.g. "Carl", "Fingers").</summary>
    public string ActorType { get; init; } = "";
    /// <summary>Levels added on top of the party level (COTF: normal 1, leader 2, painted 3...).</summary>
    public int LevelExtra { get; init; } = 1;
    /// <summary>Always elite with 10 abilities (COTF's Megan; in SotF the story bosses).</summary>
    public bool BossType { get; init; }
    public bool CanBeElite { get; init; } = true;
    public bool CanBeUndead { get; init; }
}

/// <summary>The rolled, host-owned state of one enemy.</summary>
public sealed record EnemyProfile
{
    public string KindId { get; init; } = "";
    public int Level { get; init; }
    public EnemyTier Tier { get; init; }
    public IReadOnlyList<EnemyAbility> Abilities { get; init; } = Array.Empty<EnemyAbility>();
    public double MaxHealth { get; init; }
    /// <summary>Multiplier on the enemy's damage formula (see <see cref="EnemyScaler.DamageToPlayer"/>).</summary>
    public double DamageMult { get; init; }
    public double Armor { get; init; }
    public double Speed { get; init; } = 1;
    public double Scale { get; init; } = 1;
    public double Bounty { get; init; }
    /// <summary>Personal name of an elite, miniboss or boss (COS-68); null for normal enemies.</summary>
    public EliteName? Name { get; init; }

    /// <summary>Max fraction of max health one hit can remove; 1 = uncapped.</summary>
    public double HitCap => Abilities.Contains(EnemyAbility.BossSteadfast) ? 0.01
        : Abilities.Contains(EnemyAbility.EliteSteadfast) ? 0.04
        : Abilities.Contains(EnemyAbility.Steadfast) ? 0.06
        : 1;

    public bool Has(EnemyAbility a) => Abilities.Contains(a);
}

/// <summary>Rolls enemy profiles from kind, party level, the game's difficulty and host rules.</summary>
public static class EnemyScaler
{
    private static readonly EnemyAbility[] AllAbilities = (EnemyAbility[])Enum.GetValues(typeof(EnemyAbility));

    private static readonly EnemyAbility[] Steadfasts = { EnemyAbility.Steadfast, EnemyAbility.EliteSteadfast, EnemyAbility.BossSteadfast };

    /// <summary>Abilities that need difficulty Master (d ≥ 3) or higher. Radiance added per the original's issue #31.</summary>
    private static readonly EnemyAbility[] MasterOnly = { EnemyAbility.Meteor, EnemyAbility.BlackHole, EnemyAbility.ArcaneCataclysm, EnemyAbility.FireCataclysm, EnemyAbility.Radiance };

    /// <summary>
    /// COS-130: <c>party ± 1 + kind extra + game-difficulty offset + host extra levels</c>, at least 1. <paramref name="partyLevel"/>
    /// comes from <see cref="EnemyLevels.PartyLevel"/> (or <see cref="WorldRules.PartyLevelOverride"/>).
    /// </summary>
    public static int RollLevel(EnemyKind kind, WorldRules rules, int partyLevel, Rng rng)
    {
        int party = Math.Max(1, rules.PartyLevelOverride ?? partyLevel);
        return Math.Max(1, party + rng.RangeInclusive(-1, 1) + kind.LevelExtra + rules.GameDifficulty.LevelOffset + rules.ExtraEnemyLevels);
    }

    // COS-150: the three enemy level curves, as log10 knots (monotone cubic between knots, straight in log10 past the last).
    // Fitted in PacingSim (real drops, Merge) so a plain on-level enemy takes about 8-20 s to kill and about 6-60 of its hits
    // to kill a geared player on Normal, while Hard Survival (+10 levels) stays as close to that as one curve allows. They
    // follow the player's gear, which grows fastest from level 40 to 80. COTF's curves (L^2.38 health, e^⌊L/6⌋ damage,
    // L^2.43 × band armor) left late enemies dying in one hit and mid-level ones unable to hurt the player.

    /// <summary>Health of a plain enemy with 100 vanilla health, by level.</summary>
    public static readonly LevelCurve HealthCurve = new(
        (1, 2.0), (5, 2.5), (10, 2.9), (20, 3.53), (30, 3.9), (40, 4.9), (50, 6.0), (60, 7.15), (70, 8.3), (80, 8.9),
        (90, 9.3), (100, 9.7), (110, 9.9), (120, 10.08), (130, 10.25), (200, 11.05), (300, 12.05));

    /// <summary>Damage multiplier (see <see cref="DamageToPlayer"/>), by level.</summary>
    public static readonly LevelCurve DamageCurve = new(
        (1, 0.3), (6, 0.92), (12, 1.4), (20, 2.15), (30, 2.97), (40, 4.05), (50, 4.92), (60, 5.65), (70, 6.22), (80, 6.5),
        (90, 6.78), (100, 7.05), (110, 7.3), (120, 7.6), (130, 7.75), (200, 8.5), (300, 9.5));

    /// <summary>Armor scale <c>A</c> of the armor roll <c>U(A/3 + 1, 2A/3 + 20)</c>, by level. Grows about as fast as the
    /// player's armor pierce, so pierce strips it in a few hits instead of armor cutting every hit by 99% late.</summary>
    public static readonly LevelCurve ArmorCurve = new(
        (1, 0), (10, 2.43), (20, 3.2), (30, 3.7), (50, 4.25), (75, 4.55), (100, 4.75), (130, 4.9), (200, 5.15), (300, 5.4));

    public static double DamageMultiplier(int level) => DamageCurve.At(level);

    /// <remarks>Vanilla health is a floor (SotF enemies would otherwise be far weaker than vanilla at level 1); the vanilla health
    /// scales the curve as in COTF, <c>(√vanilla + 45) / 55</c>, so a 100-health kind gets the curve itself.</remarks>
    public static double MaxHealth(int level, double vanillaHealth, double healthMul)
    {
        double v = Math.Max(0, vanillaHealth);
        return Math.Max(v, (Math.Sqrt(v) + 45) / 55 * HealthCurve.At(level)) * healthMul;
    }

    /// <summary>
    /// COTF's health curve, kept as the price list of kills: the bounty (XP, and the loot rarity roll it feeds) is computed
    /// from this health, not <see cref="MaxHealth"/>, so the COS-60 XP and drop tuning does not move when toughness is tuned.
    /// </summary>
    public static double RewardHealth(int level, double vanillaHealth, double healthMul) =>
        Math.Max(Math.Max(0, vanillaHealth),
            (Math.Sqrt(Math.Max(0, vanillaHealth)) + 45) * Math.Pow(level, 2.38) * 0.2 * Math.Pow(2, (level - 50) / 32.0)) * healthMul;

    public static double RollArmor(int level, double armorMul, Rng rng)
    {
        double a = ArmorCurve.At(level);
        return Math.Floor(rng.Range((float)(a / 3 + 1), (float)(a * 2 / 3 + 20)) * armorMul);
    }

    /// <summary>The mean of <see cref="RollArmor"/> with no host multiplier.</summary>
    public static double MeanArmor(int level) => ArmorCurve.At(level) / 2 + 10.5;

    /// <summary>The bounty of a plain enemy with 100 vanilla health at <paramref name="level"/>: the XP curve's unit (COS-60).</summary>
    public static double ReferenceBounty(int level) => Bounty(RewardHealth(level, 100, 1), level, 0, 1);

    public static double Bounty(double maxHealth, int level, int abilityCount, double xpMul)
    {
        double b = Math.Pow(maxHealth, 0.8) * 0.8 * Math.Pow(level, 0.25) + 100;
        if (abilityCount > 1) b *= abilityCount;
        b *= Math.Max(1, level / 20.0);
        return b * xpMul;
    }

    public static EnemyProfile Roll(EnemyKind kind, double vanillaHealth, WorldRules rules, int partyLevel, Rng rng)
    {
        int level = RollLevel(kind, rules, partyLevel, rng);
        var d = Difficulties.ForLevel(level);

        var forced = rules.ForcedTier;
        bool elite = forced is { } f ? f != EnemyTier.Normal
            : rules.AllowElites && kind.CanBeElite && (rng.Chance(rules.GameDifficulty.EliteChance) || kind.BossType || d.IsHell);
        var tier = EnemyTier.Normal;
        var abilities = new List<EnemyAbility>();
        if (elite)
        {
            int n = kind.BossType ? 10 : rng.RangeInclusive(d.EliteAbilityCount.Min, d.EliteAbilityCount.Max);
            if (forced == EnemyTier.Boss) n = Math.Max(n, 6);
            else if (forced == EnemyTier.Miniboss) n = Math.Max(n, 5);
            abilities = PickAbilities(n, kind, d, rng, rules.DisabledAbilities);
            if (forced is { } ft ? ft == EnemyTier.Boss : n >= 6 && rng.Chance(0.1))
            {
                tier = EnemyTier.Boss;
                abilities.RemoveAll(a => Steadfasts.Contains(a));
                abilities.Add(EnemyAbility.BossSteadfast);
            }
            else if (forced is { } fm ? fm == EnemyTier.Miniboss : n > 4 && rng.Chance(0.25))
            {
                tier = EnemyTier.Miniboss;
                abilities.RemoveAll(a => Steadfasts.Contains(a));
                abilities.Add(EnemyAbility.EliteSteadfast);
            }
            else
            {
                tier = EnemyTier.Elite;
            }
        }

        double hp = MaxHealth(level, vanillaHealth, rules.EnemyHealth);
        double dmg = DamageMultiplier(level) * rules.EnemyDamage;
        double armor = RollArmor(level, rules.EnemyArmor, rng);
        double speed = (0.94 + level / 170.0) * rules.EnemySpeed;
        double scale = 1;

        switch (tier)
        {
            case EnemyTier.Elite: hp *= 5; armor *= 2; scale = 1.1; break;
            case EnemyTier.Miniboss: hp *= 10; armor *= 2; scale = 1.15; break;
            case EnemyTier.Boss: hp *= 20; armor *= 2; scale = 1.2; break;
        }
        foreach (var a in abilities)
        {
            switch (a)
            {
                case EnemyAbility.Steadfast:
                case EnemyAbility.EliteSteadfast:
                case EnemyAbility.BossSteadfast:
                    armor *= 2; break;
                case EnemyAbility.Gargantuan:
                    armor *= 3; hp *= 2.5; dmg *= 2.5; speed /= 1.5; scale *= 1.5; break;
                case EnemyAbility.Tiny:
                    speed *= 1.1; dmg *= 1.2; scale *= 0.6; break;
                case EnemyAbility.ExtraHealth:
                    hp *= 4; break;
                case EnemyAbility.ExtraDamage:
                    dmg *= 5; break;
                case EnemyAbility.Juggernaut:
                    speed /= 1.15; break;
            }
        }

        return new EnemyProfile
        {
            KindId = kind.Id,
            Level = level,
            Tier = tier,
            Abilities = abilities,
            MaxHealth = hp,
            DamageMult = dmg,
            Armor = armor,
            Speed = speed,
            Scale = scale,
            Bounty = Bounty(hp * RewardHealth(level, vanillaHealth, 1) / MaxHealth(level, vanillaHealth, 1), level, abilities.Count, rules.Xp),
        };
    }

    /// <summary>n distinct abilities: one steadfast at most, Tiny and Gargantuan exclusive, Master+ gating, Undead only where allowed.</summary>
    public static List<EnemyAbility> PickAbilities(int n, EnemyKind kind, DifficultyTier d, Rng rng, IReadOnlyCollection<EnemyAbility>? disabled = null)
    {
        var allowed = AllAbilities.Where(a =>
            (d.Index >= 3 || !MasterOnly.Contains(a)) &&
            (kind.CanBeUndead || a != EnemyAbility.Undead) &&
            (disabled == null || !disabled.Contains(a))).ToList();
        var picked = new List<EnemyAbility>();
        while (picked.Count < n && allowed.Count > 0)
        {
            var a = rng.Pick(allowed);
            picked.Add(a);
            allowed.Remove(a);
            if (Steadfasts.Contains(a)) allowed.RemoveAll(x => Steadfasts.Contains(x));
            if (a == EnemyAbility.Tiny) allowed.Remove(EnemyAbility.Gargantuan);
            if (a == EnemyAbility.Gargantuan) allowed.Remove(EnemyAbility.Tiny);
        }
        return picked;
    }

    /// <summary>
    /// Kill XP per player in range. COTF split the bounty evenly over every player in the game (its issue #25:
    /// a friend far away barely leveled). Here only players in range share it, and each extra player adds 20%
    /// to the pot so grouping up is never a loss.
    /// </summary>
    public static double KillXpShare(double bounty, int playersInRange) =>
        playersInRange <= 0 ? 0 : bounty * (1 + 0.2 * (playersInRange - 1)) / playersInRange;

    /// <summary>Damage an enemy deals to the player before the player's defenses.</summary>
    /// <remarks>COTF: damageMult + vanilla − 20, tuned for The Forest's numbers. SotF weapons hit for less, so
    /// the vanilla damage is a floor: low levels feel like the base game and scaling only ever adds.</remarks>
    public static double DamageToPlayer(EnemyProfile enemy, double vanillaDamage, double debuffDamageMult = 1) =>
        Math.Max(vanillaDamage, enemy.DamageMult * debuffDamageMult + vanillaDamage - 20);
}
