using System.Numerics;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Flicker Strike (COS-141, our own spell): the caster blinks from enemy to enemy within range, nearest-neighbour order, comes
/// back to the start, and after a short beat every visited enemy takes its hit in the same frame. Each extra enemy in the chain
/// makes every slash hit harder.
/// </summary>
public static class FlickerStrikeRules
{
    public const float Range = 20f;
    public const int MaxTargets = 8;
    public const double Beat = 0.3, MaxHopPause = 0.12, RunBudget = 1.0;
    public const double BaseDamage = 40, Scaling = 3, ComboPerTarget = 0.1;

    /// <summary>Pause at each hop: at most 0.12 s, and the whole run stays under about a second however many enemies.</summary>
    public static double HopPause(int targets) => Math.Min(MaxHopPause, RunBudget / Math.Max(1, targets));

    /// <summary>Damage multiplier for a chain of <paramref name="targets"/> enemies: ×1 for one, ×1.7 for eight.</summary>
    public static double Combo(int targets) => 1 + ComboPerTarget * (Math.Max(1, targets) - 1);

    /// <summary>
    /// The visiting order: candidates within <see cref="Range"/> of the start (the caller has already dropped unreachable ones),
    /// at most <see cref="MaxTargets"/>, each hop going to the nearest unvisited one from where the caster stands. Returns indexes
    /// into <paramref name="candidates"/>.
    /// </summary>
    public static List<int> Route(Vector3 start, IReadOnlyList<Vector3> candidates)
    {
        var left = Enumerable.Range(0, candidates.Count).Where(i => Vector3.Distance(start, candidates[i]) <= Range).ToList();
        var route = new List<int>();
        var at = start;
        while (left.Count > 0 && route.Count < MaxTargets)
        {
            int best = left.OrderBy(i => Vector3.DistanceSquared(at, candidates[i])).ThenBy(i => i).First();
            route.Add(best);
            left.Remove(best);
            at = candidates[best];
        }
        return route;
    }
}
