namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Kill streak: kills bank their XP; each kill extends the timer; when the timer runs out the banked XP is paid
/// out times a multiplier that grows with the number of kills.
/// </summary>
public sealed class Massacre
{
    public int Kills { get; private set; }
    public double PendingXp { get; private set; }
    public double TimeLeft { get; private set; }

    public bool Active => TimeLeft > 0;

    /// <summary>Raised when a streak ends by timeout and pays out: kills, banked XP, multiplier, XP paid. Not raised by <see cref="Reset"/>.</summary>
    public event Action<int, double, double, double>? Paid;

    /// <summary>Banks a kill. <paramref name="maxTime"/> and <paramref name="bonusPerKill"/> come from the stat sheet.</summary>
    public void OnKill(double xp, double maxTime, double bonusPerKill)
    {
        Kills++;
        PendingXp += xp;
        TimeLeft = TimeLeft <= 0 ? maxTime : Math.Clamp(TimeLeft + bonusPerKill, 1, Math.Max(1, maxTime));
    }

    /// <summary>Advances the timer. Returns the XP to pay out when the streak ends this tick, else 0.</summary>
    public double Tick(double dt)
    {
        if (TimeLeft <= 0) return 0;
        TimeLeft -= dt;
        if (TimeLeft > 0) return 0;
        double mult = Multiplier(Kills);
        double payout = PendingXp * mult;
        int kills = Kills;
        double banked = PendingXp;
        Reset();
        Paid?.Invoke(kills, banked, mult, payout);
        return payout;
    }

    /// <summary>Death: the streak and its banked XP are lost.</summary>
    public void Reset()
    {
        Kills = 0;
        PendingXp = 0;
        TimeLeft = 0;
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
}
