using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Networking;

/// <summary>Who earns a kill's XP: players near the kill, each getting <see cref="EnemyScaler.KillXpShare"/>.</summary>
public static class KillCredit
{
    public const float DefaultRange = 80f;

    public static List<uint> Recipients(IEnumerable<(uint Id, Vec3 Position)> players, Vec3 killPosition, float range = DefaultRange)
    {
        float r2 = range * range;
        return players.Where(p => Sq(p.Position.X - killPosition.X) + Sq(p.Position.Y - killPosition.Y) + Sq(p.Position.Z - killPosition.Z) <= r2)
            .Select(p => p.Id).ToList();
    }

    /// <summary>
    /// Level a kill's loot is rolled at, by the host's loot-level policy, from the levels of the players near the kill.
    /// <paramref name="hostLevel"/> is the host's own level (a dedicated server has none: pass the highest player level then).
    /// </summary>
    public static int LootLevel(LootLevelPolicy policy, IReadOnlyList<(int Level, float Distance)> nearby, int hostLevel)
    {
        if (policy == LootLevelPolicy.Host || nearby.Count == 0) return Math.Max(1, hostLevel);
        return Math.Max(1, policy switch
        {
            LootLevelPolicy.Lowest => nearby.Min(p => p.Level),
            LootLevelPolicy.Average => (int)Math.Round(nearby.Average(p => p.Level)),
            LootLevelPolicy.Closest => nearby.OrderBy(p => p.Distance).First().Level,
            _ => nearby.Max(p => p.Level),
        });
    }

    /// <summary>XP each recipient gets (all recipients get the same amount).</summary>
    public static double ShareFor(double bounty, int recipients) => EnemyScaler.KillXpShare(bounty, recipients);

    private static float Sq(float v) => v * v;
}

/// <summary>
/// Buffs a teammate's aura gives the local player (COS-23). Each <see cref="AuraPulse"/> in range refreshes the buff;
/// it ends on its own when pulses stop arriving (caster died, aura ended, or the player walked out of range).
/// </summary>
public sealed class AuraBoard
{
    private readonly Dictionary<(uint Player, string Buff), double> _expires = new();

    /// <summary>Applies a pulse if <paramref name="local"/> is inside its radius. Returns whether it refreshed a buff.</summary>
    public bool Apply(AuraPulse pulse, Vec3 local, double now)
    {
        float dx = local.X - pulse.Origin.X, dy = local.Y - pulse.Origin.Y, dz = local.Z - pulse.Origin.Z;
        if (dx * dx + dy * dy + dz * dz > pulse.Radius * pulse.Radius) return false;
        _expires[(pulse.PlayerId, pulse.BuffId)] = now + pulse.Seconds;
        return true;
    }

    public bool IsActive(string buffId, double now) => _expires.Any(e => e.Key.Buff == buffId && e.Value > now);

    public IEnumerable<string> Active(double now) => _expires.Where(e => e.Value > now).Select(e => e.Key.Buff).Distinct();

    public void Prune(double now)
    {
        foreach (var k in _expires.Where(e => e.Value <= now).Select(e => e.Key).ToList()) _expires.Remove(k);
    }

    public void Clear() => _expires.Clear();
}

/// <summary>Active ping markers (COS-24). A player has one marker at a time; it expires after <see cref="Lifetime"/> seconds.</summary>
public sealed class PingBoard
{
    public const double Lifetime = 8;

    private readonly Dictionary<uint, (Ping Ping, double Expires)> _byPlayer = new();

    public event Action<Ping>? Added;
    public event Action<Ping>? Expired;

    public IEnumerable<Ping> Active => _byPlayer.Values.Select(v => v.Ping);

    public void Add(Ping ping, double now)
    {
        if (_byPlayer.TryGetValue(ping.PlayerId, out var old)) Expired?.Invoke(old.Ping);
        _byPlayer[ping.PlayerId] = (ping, now + Lifetime);
        Added?.Invoke(ping);
    }

    public void Tick(double now)
    {
        foreach (var (player, v) in _byPlayer.Where(e => e.Value.Expires <= now).ToList())
        {
            _byPlayer.Remove(player);
            Expired?.Invoke(v.Ping);
        }
    }

    public void Clear() => _byPlayer.Clear();
}

/// <summary>Host settings a dedicated server reads from its config file.</summary>
public static class ServerSettings
{
    /// <summary>The configured tier, or Easy with <paramref name="problem"/> set when the id is unknown (the server never waits for a choice).</summary>
    public static DifficultyTier Resolve(string? difficultyId, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(difficultyId)) return Difficulties.Easy;
        var tier = Difficulties.ById(difficultyId.Trim()) ?? Difficulties.All.FirstOrDefault(d => string.Equals(d.Name, difficultyId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tier != null) return tier;
        problem = $"Unknown difficulty '{difficultyId}'. Valid: {string.Join(", ", Difficulties.All.Select(d => d.Id))}. Using Easy.";
        return Difficulties.Easy;
    }
}

/// <summary>Host side of a remote pickup: whoever asks first gets the drop, everyone after gets nothing.</summary>
public static class PickupArbiter
{
    /// <summary>Takes the drop out of the world and returns its item, or null when it is already gone.</summary>
    public static ItemInstance? Claim(GroundLoot ground, uint dropId)
    {
        var drop = ground.Get((int)dropId);
        if (drop == null) return null;
        var item = drop.Item;
        return ground.TryPickup((int)dropId, new Inventory()) == PickupResult.Ok ? item : null;
    }
}
