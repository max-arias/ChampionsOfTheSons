namespace ChampionsOfTheSons.Core;

/// <summary>
/// Small deterministic RNG (SplitMix64). Every roll in the rules core takes one of these,
/// so tests are reproducible and the host can share seeds with clients.
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    public static Rng FromTime() => new((ulong)DateTime.UtcNow.Ticks);

    public ulong NextUInt64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform integer in [min, maxExclusive). Returns min when the range is empty.</summary>
    public int Range(int min, int maxExclusive)
    {
        if (maxExclusive <= min) return min;
        return min + (int)(NextUInt64() % (ulong)(maxExclusive - min));
    }

    /// <summary>Uniform integer in [min, max] (both inclusive).</summary>
    public int RangeInclusive(int min, int max) => Range(min, max + 1);

    /// <summary>Uniform float in [min, max).</summary>
    public float Range(float min, float max) => min + (float)NextDouble() * (max - min);

    /// <summary>True with probability <paramref name="p"/> (clamped to [0, 1]).</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Index picked with probability proportional to its weight. -1 when all weights are 0.</summary>
    public int PickWeighted(IReadOnlyList<double> weights)
    {
        double total = 0;
        foreach (var w in weights) total += Math.Max(0, w);
        if (total <= 0) return -1;
        double r = NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            r -= Math.Max(0, weights[i]);
            if (r < 0) return i;
        }
        return weights.Count - 1;
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Range(0, items.Count)];
}
