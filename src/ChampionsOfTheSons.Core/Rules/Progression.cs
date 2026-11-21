namespace ChampionsOfTheSons.Core.Rules;

/// <summary>XP curve and attribute growth.</summary>
public static class XpCurve
{
    /// <summary>From this level on, the goal is effectively unreachable.</summary>
    public const int PracticalMaxLevel = 138;

    /// <summary>XP needed to go from <paramref name="level"/> to the next level.</summary>
    public static double Goal(int level)
    {
        if (level >= PracticalMaxLevel) return 5e18;
        double l = level;
        return Math.Round(0.8 * Math.Pow(l, 4) + Math.Pow(l, 3) + 2 * l * l + 20 * l + 4 * Math.Exp(l / 3.3) + Math.Pow(1.36, l - 5));
    }

    /// <summary>Attribute base value at a level: 1 + A(L), A(1)=1, A(x)=A(x−1)+round(√x).</summary>
    public static int AttributeBase(int level)
    {
        int a = 1;
        for (int x = 2; x <= level; x++) a += (int)Math.Round(Math.Sqrt(x), MidpointRounding.AwayFromZero);
        return 1 + a;
    }
}

/// <summary>
/// XP penalty on player death (COS-76; COTS rule, COTF reset the whole bar): half of the XP into the current level,
/// but never more than a tenth of that level's goal. The level and its perk points are never lost.
/// </summary>
public static class DeathPenalty
{
    public const double ShareOfProgress = 0.5;
    public const double MaxShareOfGoal = 0.1;

    public static double XpLost(int level, double xp) =>
        xp <= 0 ? 0 : Math.Min(xp * ShareOfProgress, XpCurve.Goal(level) * MaxShareOfGoal);
}

/// <summary>What one call to <see cref="Progress.AddXp"/> did.</summary>
public sealed record LevelUpResult(int OldLevel, int NewLevel, IReadOnlyList<string> RewardItemIds)
{
    public bool LeveledUp => NewLevel > OldLevel;
}

/// <summary>Level, XP into the current level and permanent bonus perk points.</summary>
public sealed class Progress
{
    public const string HeartOfPurity = "item.heart_of_purity";
    public const string GreaterMutatedHeart = "item.greater_mutated_heart";

    public int Level { get; private set; } = 1;
    public double Xp { get; private set; }
    public int BonusPerkPoints { get; set; }

    /// <summary>The game day daily grants last paid out on; -1 before the first (COS-100).</summary>
    public int LastGrantDay { get; set; } = -1;

    /// <summary>COS-100: whether daily grants pay out on <paramref name="day"/>. The first day ever seen only sets the baseline;
    /// a later day pays once (however many days passed) and becomes the new baseline. Survives a reload, so a reload neither pays
    /// twice nor loses the grant.</summary>
    public bool TakeDailyGrant(int day)
    {
        if (LastGrantDay < 0) { LastGrantDay = day; return false; }
        if (day <= LastGrantDay) return false;
        LastGrantDay = day;
        return true;
    }

    /// <summary>Perk points earned in total: one per level plus permanent bonus points.</summary>
    public int TotalPerkPoints => Level + BonusPerkPoints;

    public Progress() { }

    public Progress(int level, double xp, int bonusPerkPoints)
    {
        Level = Math.Max(1, level);
        Xp = Math.Max(0, xp);
        BonusPerkPoints = bonusPerkPoints;
    }

    /// <summary>Adds XP (already multiplied by sources like massacre) scaled by the player's XP gain stat.</summary>
    public LevelUpResult AddXp(double amount, double expGain = 1)
    {
        int old = Level;
        var rewards = new List<string>();
        if (amount <= 0) return new LevelUpResult(old, old, rewards);
        Xp += amount * expGain;
        while (Xp >= XpCurve.Goal(Level) && Level < XpCurve.PracticalMaxLevel)
        {
            Xp -= XpCurve.Goal(Level);
            Level++;
            if (Level % 10 == 0) rewards.Add(HeartOfPurity);
            else if (Level >= 10 && Level % 20 == 5) rewards.Add(GreaterMutatedHeart);
        }
        return new LevelUpResult(old, Level, rewards);
    }

    /// <summary>Death: loses <see cref="DeathPenalty.XpLost"/> of the XP into the current level; the level is kept. Returns the XP lost.</summary>
    public double OnDeath()
    {
        double lost = DeathPenalty.XpLost(Level, Xp);
        Xp -= lost;
        return lost;
    }

    /// <summary>Admin: sets the level directly, with no XP into it. Clamped to 1..<see cref="XpCurve.PracticalMaxLevel"/>.</summary>
    public void SetLevel(int level)
    {
        Level = Math.Clamp(level, 1, XpCurve.PracticalMaxLevel);
        Xp = 0;
    }

    public double ProgressToNext => Xp / XpCurve.Goal(Level);
}
