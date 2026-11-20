namespace ChampionsOfTheSons.Core.Rules;

/// <summary>How dangerous an enemy is to the local player, at a glance (COS-68). Drawn as tally marks on the nameplate.</summary>
public enum DangerRating : byte
{
    Trivial = 0,
    Even = 1,
    Tough = 2,
    Dangerous = 3,
    Deadly = 4,
}

/// <summary>An elite's personal name: a given name id and an epithet id from content/elite_names.json (COS-68).</summary>
public sealed record EliteName(string GivenId, string EpithetId);

/// <summary>Rules for what the enemy scanner and nameplates show (COS-68). New rules; COTF had no danger rating.</summary>
public static class EnemyInfo
{
    /// <summary>
    /// Level gap against the player, in steps of <c>max(3, playerLevel / 10)</c> levels: more than two steps below is
    /// Trivial, up to one step above is Even, up to three steps above is Tough, beyond that Dangerous.
    /// Elite and Miniboss add one rating, Boss adds two; capped at Deadly.
    /// </summary>
    public static DangerRating Danger(EnemyTier tier, int enemyLevel, int playerLevel)
    {
        double step = Math.Max(3, playerLevel / 10.0);
        double gap = enemyLevel - playerLevel;
        int r = gap < -2 * step ? 0 : gap <= step ? 1 : gap <= 3 * step ? 2 : 3;
        r += tier switch { EnemyTier.Elite or EnemyTier.Miniboss => 1, EnemyTier.Boss => 2, _ => 0 };
        return (DangerRating)Math.Clamp(r, 0, 4);
    }

    /// <summary>Elites, minibosses and bosses get a random given name and epithet; normal enemies none.</summary>
    public static EliteName? PickName(EnemyTier tier, IReadOnlyList<string> givenIds, IReadOnlyList<string> epithetIds, Rng rng)
    {
        if (tier == EnemyTier.Normal || givenIds.Count == 0 || epithetIds.Count == 0) return null;
        return new EliteName(rng.Pick(givenIds), rng.Pick(epithetIds));
    }

    /// <summary>Extra damage per hit over the base game's own hit (see <see cref="EnemyScaler.DamageToPlayer"/>).</summary>
    public static double ExtraHitDamage(double damageMult) => Math.Max(0, damageMult - 20);
}
