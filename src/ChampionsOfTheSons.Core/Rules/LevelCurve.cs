namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// A smooth, monotonic curve over levels given as knots of log10 values (COS-150): monotone cubic (Fritsch–Carlson)
/// interpolation between knots, so the curve never overshoots or dips between two rising knots; straight in log10
/// (constant growth per level) before the first and past the last knot.
/// </summary>
public sealed class LevelCurve
{
    private readonly double[] _x, _y, _m;

    /// <param name="knots">(level, log10 value) pairs, levels strictly rising.</param>
    public LevelCurve(params (double Level, double Log10)[] knots)
    {
        if (knots.Length < 2) throw new ArgumentException("A curve needs two knots or more.", nameof(knots));
        _x = knots.Select(k => k.Level).ToArray();
        _y = knots.Select(k => k.Log10).ToArray();
        int n = _x.Length;
        var d = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            if (_x[i + 1] <= _x[i]) throw new ArgumentException("Knot levels must rise.", nameof(knots));
            d[i] = (_y[i + 1] - _y[i]) / (_x[i + 1] - _x[i]);
        }
        _m = new double[n];
        _m[0] = d[0];
        _m[n - 1] = d[n - 2];
        for (int i = 1; i < n - 1; i++) _m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
        for (int i = 0; i < n - 1; i++)
        {
            if (d[i] == 0) { _m[i] = 0; _m[i + 1] = 0; continue; }
            double a = _m[i] / d[i], b = _m[i + 1] / d[i], s = a * a + b * b;
            if (s > 9)
            {
                double t = 3 / Math.Sqrt(s);
                _m[i] = t * a * d[i];
                _m[i + 1] = t * b * d[i];
            }
        }
    }

    /// <summary>log10 of the curve at <paramref name="level"/>.</summary>
    public double Log10At(double level)
    {
        int n = _x.Length;
        if (level <= _x[0]) return _y[0] + _m[0] * (level - _x[0]);
        if (level >= _x[n - 1]) return _y[n - 1] + _m[n - 1] * (level - _x[n - 1]);
        int i = Array.BinarySearch(_x, level);
        if (i >= 0) return _y[i];
        i = ~i - 1;
        double h = _x[i + 1] - _x[i], t = (level - _x[i]) / h, t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * _y[i] + (t3 - 2 * t2 + t) * h * _m[i] + (-2 * t3 + 3 * t2) * _y[i + 1] + (t3 - t2) * h * _m[i + 1];
    }

    public double At(double level) => Math.Pow(10, Log10At(level));
}
