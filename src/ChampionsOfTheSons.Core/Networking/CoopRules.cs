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

    /// <summary>
    /// Magic find used for a kill's drops in co-op (COS-81, Max's decision): the sum of every player's magic find. A player's
    /// value is their multiplier (1 = no bonus); a missing or broken value counts as 1, so each player adds at least their base.
    /// With no players (a dedicated server before anyone joined) it is 1.
    /// </summary>
    public static double TeamMagicFind(IEnumerable<double> perPlayer)
    {
        double sum = 0;
        int n = 0;
        foreach (var mf in perPlayer)
        {
            sum += double.IsFinite(mf) && mf > 0 ? mf : 1;
            n++;
        }
        return n == 0 ? 1 : sum;
    }

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

/// <summary>
/// Active ping markers (COS-24, COS-98). A player has one marker at a time; it expires after <see cref="Lifetime"/> seconds
/// (Max 2026-11-21: 30 s) or when that player sends a <see cref="PingKind.Clear"/>.
/// </summary>
public sealed class PingBoard
{
    public const double Lifetime = 30;

    /// <summary>Pinging within this many metres of your own marker clears it instead of placing a new one.</summary>
    public const float ClearRadius = 3f;

    private readonly Dictionary<uint, (Ping Ping, double Expires)> _byPlayer = new();

    public event Action<Ping>? Added;
    public event Action<Ping>? Expired;

    public IEnumerable<Ping> Active => _byPlayer.Values.Select(v => v.Ping);

    public void Add(Ping ping, double now)
    {
        if (_byPlayer.TryGetValue(ping.PlayerId, out var old))
        {
            _byPlayer.Remove(ping.PlayerId);
            Expired?.Invoke(old.Ping);
        }
        if (ping.Kind == PingKind.Clear) return;
        _byPlayer[ping.PlayerId] = (ping, now + Lifetime);
        Added?.Invoke(ping);
    }

    /// <summary>The player's marker now, or null.</summary>
    public Ping? Of(uint playerId) => _byPlayer.TryGetValue(playerId, out var v) ? v.Ping : null;

    /// <summary>Ends a player's marker early (the enemy it follows died). False if there was none.</summary>
    public bool Remove(uint playerId)
    {
        if (!_byPlayer.Remove(playerId, out var v)) return false;
        Expired?.Invoke(v.Ping);
        return true;
    }

    /// <summary>
    /// What a press of the ping key does for <paramref name="playerId"/>: nothing hit, or a point near (or the same target as)
    /// their own marker, clears it; anything else places a new one. Null: nothing to do (no hit and no marker).
    /// </summary>
    public PingKind? PressKind(uint playerId, PingKind hitKind, Vec3? hit, ulong target)
    {
        var own = Of(playerId);
        if (hit == null) return own != null ? PingKind.Clear : null;
        if (own != null && ((target != 0 && own.TargetId == target && own.Kind == hitKind) || Distance(own.Position, hit.Value) <= ClearRadius))
            return PingKind.Clear;
        return hitKind;
    }

    private static float Distance(Vec3 a, Vec3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
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
