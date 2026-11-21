namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Sinew Channel (COS-128, Max 2026-11-20): a share of each cast's energy cost comes back over a few seconds. Energy and stamina are
/// one bar since COS-94, so the perk's old effect (moving part of the cost to stamina) did nothing.
/// </summary>
public sealed class EnergyRefund
{
    public const double Seconds = 3;

    private readonly List<(double PerSecond, double Left)> _pending = new();

    /// <summary>Queues <paramref name="fraction"/> of <paramref name="cost"/> to come back over <see cref="Seconds"/>.</summary>
    public void Add(double cost, double fraction)
    {
        double amount = cost * Math.Clamp(fraction, 0, 1);
        if (amount > 0) _pending.Add((amount / Seconds, Seconds));
    }

    /// <summary>Energy to give back this frame.</summary>
    public double Tick(double dt)
    {
        double gained = 0;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (perSecond, left) = _pending[i];
            double step = Math.Min(dt, left);
            gained += perSecond * step;
            if (left - step <= 1e-9) _pending.RemoveAt(i);
            else _pending[i] = (perSecond, left - step);
        }
        return gained;
    }

    public void Clear() => _pending.Clear();
}
