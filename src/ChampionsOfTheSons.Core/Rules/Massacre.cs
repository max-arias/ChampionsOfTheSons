namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Kill streak. A streak starts only on the 3rd kill in a chain where each kill comes within <see cref="ChainWindow"/> s of
/// the one before (Max, 2026-11-21, COS-95). Kills before that pay their XP at once. Once the streak starts it banks every
/// kill's XP, the chain's first kills included; each kill extends the timer; when the timer runs out the banked XP is paid
/// out times a multiplier that grows with the number of kills, minus what the first kills already paid. So the total is the
/// same as if every kill had been banked.
/// </summary>
public sealed class Massacre
{
    /// <summary>Kills needed to start a streak.</summary>
    public const int StartKills = 3;

    /// <summary>Most seconds between two kills of the chain that starts a streak.</summary>
    public const double ChainWindow = 7.5;

    /// <summary>Kills in the streak, or in the chain that may start one.</summary>
    public int Kills { get; private set; }

    /// <summary>XP banked by the running streak (the chain's first kills included).</summary>
    public double PendingXp { get; private set; }

    /// <summary>XP the chain's first kills already paid; taken off the payout.</summary>
    public double PrepaidXp { get; private set; }

    /// <summary>Seconds left in the running streak (0 = none).</summary>
    public double TimeLeft { get; private set; }

    /// <summary>Seconds left for the next kill of a chain that has not started a streak yet.</summary>
    public double ChainLeft { get; private set; }

    /// <summary>A streak is running (shown on the HUD).</summary>
    public bool Active => TimeLeft > 0;

    /// <summary>Raised when a streak ends by timeout and pays out: kills, banked XP, multiplier, XP paid. Not raised by <see cref="Reset"/>.</summary>
    public event Action<int, double, double, double>? Paid;

    /// <summary>
    /// Counts a kill. <paramref name="maxTime"/> and <paramref name="bonusPerKill"/> come from the stat sheet. Returns the XP
    /// to pay now (a kill before the streak starts), else 0 (banked).
    /// </summary>
    public double OnKill(double xp, double maxTime, double bonusPerKill)
    {
        if (Active)
        {
            Kills++;
            PendingXp += xp;
            TimeLeft = Math.Clamp(TimeLeft + bonusPerKill, 1, Math.Max(1, maxTime));
            return 0;
        }
        if (ChainLeft <= 0)
        {
            Kills = 0;
            PrepaidXp = 0;
        }
        Kills++;
        PrepaidXp += xp;
        if (Kills < StartKills)
        {
            ChainLeft = ChainWindow;
            return xp;
        }
        // The chain's 3rd kill: the streak starts and banks the whole chain.
        PrepaidXp -= xp;
        PendingXp = PrepaidXp + xp;
        ChainLeft = 0;
        TimeLeft = Math.Max(1, maxTime);
        return 0;
    }

    /// <summary>Advances the timers. Returns the XP to pay out when the streak ends this tick, else 0.</summary>
    public double Tick(double dt)
    {
        if (ChainLeft > 0)
        {
            ChainLeft -= dt;
            if (ChainLeft <= 0)
            {
                ChainLeft = 0;
                Kills = 0;
                PrepaidXp = 0;
            }
        }
        if (TimeLeft <= 0) return 0;
        TimeLeft -= dt;
        if (TimeLeft > 0) return 0;
        double mult = Multiplier(Kills);
        double payout = Payout(PendingXp, PrepaidXp, Kills);
        int kills = Kills;
        double banked = PendingXp;
        Reset();
        Paid?.Invoke(kills, banked, mult, payout);
        return payout;
    }

    /// <summary>What a streak pays when it ends: everything banked times the multiplier, less what was paid at once.</summary>
    public static double Payout(double banked, double prepaid, int kills) => Math.Max(0, banked * Multiplier(kills) - prepaid);

    /// <summary>Death: the streak and its banked XP are lost (XP already paid stays).</summary>
    public void Reset()
    {
        Kills = 0;
        PendingXp = 0;
        PrepaidXp = 0;
        TimeLeft = 0;
        ChainLeft = 0;
    }

    public static double Multiplier(int kills) => kills switch
    {
        < 3 => 1,
        3 => 1.1,
        4 => 1.25,
        5 => 1.5,
        < 10 => 1.75,
        < 12 => 2.3,
        < 16 => 3,
        < 20 => 5,
        < 25 => 8.5,
        < 30 => 16,
        < 40 => 22.5,
        < 50 => 35,
        < 65 => 50,
        < 75 => 100,
        < 100 => 250,
        _ => 1000,
    };

    /// <summary>The streak title tier for a kill count: 0 below a streak, then 1..8 (text keys <c>ui.hud.streak.N</c>).</summary>
    public static int TitleTier(int kills) => kills switch
    {
        < 3 => 0,
        < 6 => 1,
        < 10 => 2,
        < 16 => 3,
        < 25 => 4,
        < 40 => 5,
        < 65 => 6,
        < 100 => 7,
        _ => 8,
    };
}
