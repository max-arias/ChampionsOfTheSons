using RedLoader;
using Bolt;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// COS-12: the host owns every enemy's rolled profile and sends it to the clients, so a client's nameplates show the host's
/// level, tier and health. The host announces an enemy once (<see cref="EnemySync"/>), then only sends <see cref="EnemyHealth"/>
/// when health or armor changed. A client that sees an enemy it has no profile for can ask with <see cref="EnemyProfileRequest"/>.
/// Enemies are matched by the Bolt network id of their entity.
/// </summary>
public sealed class EnemySyncFeature : IModFeature
{
    private const float Interval = 0.25f;
    private const int MaxPerTick = 30;

    // host
    private static readonly Dictionary<ulong, (float Health, float Armor)> Announced = new();
    private static float _timer;

    // client
    private static readonly Dictionary<ulong, EnemySync> Pending = new();
    private static readonly Dictionary<ulong, EnemyRuntime> Mirrors = new();
    private static readonly Dictionary<ulong, EnemySync> ReceivedProfiles = new();
    private static float _clientTimer;

    /// <summary>Profiles the client has received, by net id (self-tests and nameplates read this).</summary>
    public static IReadOnlyDictionary<ulong, EnemySync> Received => ReceivedProfiles;

    /// <summary>The packed Bolt network id of an enemy, or 0 when it is not a networked entity.</summary>
    public static ulong NetIdOf(VailActor? actor)
    {
        try
        {
            if (actor == null) return 0;
            var entity = actor.GetComponentInParent<BoltEntity>();
            return entity != null && entity.isAttached ? entity.networkId.PackedValue : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static VailActor? ActorOf(ulong netId)
    {
        try
        {
            var entity = BoltNetwork.FindEntity(new NetworkId(netId));
            if (entity == null) return null;
            // GetComponentInChildren<T>(true) throws through IL2CPP interop here; the actor sits on the entity or below it.
            var actor = entity.GetComponent<VailActor>();
            if (actor != null) return actor;
            var below = entity.GetComponentsInChildren<VailActor>(true);
            return below.Length > 0 ? below[0] : null;
        }
        catch (Exception e)
        {
            if (!_lookupWarned) { _lookupWarned = true; RLog.Warning($"{CoopTestJoin.Tag} enemy lookup by net id failed: {e.Message}"); }
            return null;
        }
    }

    private static bool _lookupWarned;
    private static readonly HashSet<ulong> _missLogged = new();

    public void Init() => CotsNetEvent.Received += OnNet;

    public void OnGameStart()
    {
        Announced.Clear();
        Pending.Clear();
        Mirrors.Clear();
        ReceivedProfiles.Clear();
    }

    // ---------------------------------------------------------------- host

    public static EnemySync Describe(EnemyRuntime r, ulong netId)
    {
        var health = r.Stats == null ? null : EnemyRegistry.Health(r.Stats);
        float hp = health?._currentValue ?? (float)r.Profile.MaxHealth;
        return new EnemySync(netId, r.Kind.Id, r.Profile.Level, (byte)r.Profile.Tier, (float)r.Profile.MaxHealth, hp,
            (float)r.Profile.Armor, r.Profile.Abilities.Select(a => a.ToString()).ToList(), (long)r.Profile.Bounty);
    }

    public void Update(float dt, bool inWorld)
    {
        if (CoopSession.IsHost) HostTick(dt);
        else if (CoopSession.IsClient) ClientTick(dt);
    }

    private static void HostTick(float dt)
    {
        _timer -= dt;
        if (_timer > 0 || CoopFeature.Verified.Count == 0) return;
        _timer = Interval;
        // The Bridge features that normally re-resolve actors do not run on a dedicated server: without this the registry keeps
        // the actor SetMaxHealthStat saw, which is not the networked one (net id 0), and nothing is announced.
        EnemyRegistry.ResolveActors();
        int sent = 0;
        var live = new HashSet<ulong>();
        foreach (var r in EnemyRegistry.Snapshot())
        {
            if (sent >= MaxPerTick) break;
            ulong id = NetIdOf(r.Actor);
            if (id == 0) continue;
            live.Add(id);
            var health = r.Stats == null ? null : EnemyRegistry.Health(r.Stats);
            float hp = health?._currentValue ?? 0;
            float armor = (float)System.Math.Max(0, r.Profile.Armor - r.ArmorReduction);
            if (!Announced.TryGetValue(id, out var last))
            {
                CoopSession.SendToPeers(Describe(r, id));
                if (CoopTest.Enabled || Net.CoopSelfTest.ServerDriver) RLog.Msg($"{CoopTestJoin.Tag} host: announced enemy {id} ({r.Kind.Id}, level {r.Profile.Level}) to {CoopFeature.Verified.Count} peer(s)");
                Announced[id] = (hp, armor);
                sent++;
            }
            else if (System.Math.Abs(last.Health - hp) > 0.5f || System.Math.Abs(last.Armor - armor) > 0.5f)
            {
                CoopSession.SendToPeers(new EnemyHealth(id, hp, armor));
                Announced[id] = (hp, armor);
                sent++;
            }
        }
        foreach (var gone in Announced.Keys.Where(k => !live.Contains(k)).ToList()) Announced.Remove(gone);
    }

    /// <summary>A client just passed the handshake: announce every enemy again.</summary>
    public static void ResendAll() => Announced.Clear();

    // ---------------------------------------------------------------- client

    private static void ClientTick(float dt)
    {
        _clientTimer -= dt;
        if (_clientTimer > 0 || Pending.Count == 0) return;
        _clientTimer = 0.5f;
        foreach (var (id, sync) in Pending.ToList())
        {
            bool ok;
            try { ok = TryApply(sync); }
            catch (Exception e) { RLog.Error($"{CoopTestJoin.Tag} client: mirroring enemy {id} failed: {e.Message}"); ok = false; }
            if (ok) Pending.Remove(id);
        }
    }

    private static bool TryApply(EnemySync sync)
    {
        var actor = ActorOf(sync.EnemyNetId);
        var stats = actor?.GetStatsManager()?.GetStatsManager();
        if (actor == null || stats == null)
        {
            if (_missLogged.Add(sync.EnemyNetId))
                RLog.Msg($"{CoopTestJoin.Tag} client: enemy {sync.EnemyNetId} not mirrored yet: entity {(BoltNetwork.FindEntity(new NetworkId(sync.EnemyNetId)) != null)}, actor {actor != null}, stats {stats != null}");
            return false;
        }
        if (!ModContent.Db.Enemies.TryGetValue(sync.KindId, out var kind)) return true; // unknown kind: nothing to mirror
        var abilities = sync.AffixIds.Select(a => Enum.TryParse<EnemyAbility>(a, out var v) ? (EnemyAbility?)v : null)
            .Where(a => a != null).Select(a => a!.Value).ToList();
        var profile = new EnemyProfile
        {
            KindId = sync.KindId,
            Level = sync.Level,
            Tier = (EnemyTier)sync.Tier,
            Abilities = abilities,
            MaxHealth = sync.MaxHealth,
            Armor = sync.Armor,
            Bounty = sync.Bounty,
        };
        var runtime = EnemyRegistry.Mirror(stats, kind, profile, actor);
        runtime.ActorLive = true;
        Mirrors[sync.EnemyNetId] = runtime;
        ApplyHealth(runtime, sync.MaxHealth, sync.Health);
        return true;
    }

    private static void ApplyHealth(EnemyRuntime runtime, float max, float current)
    {
        var health = runtime.Stats == null ? null : EnemyRegistry.Health(runtime.Stats);
        if (health == null) return;
        health.SetMax(max);
        health.SetCurrentValue(current);
    }

    /// <summary>Asks the host for an enemy's profile (a client that sees an enemy it knows nothing about).</summary>
    public static void Request(ulong netId)
    {
        if (netId != 0 && !ReceivedProfiles.ContainsKey(netId)) CoopSession.SendToHost(new EnemyProfileRequest(netId));
    }

    // ---------------------------------------------------------------- messages

    private static void OnNet(IMessage message, BoltConnection from)
    {
        switch (message)
        {
            case EnemyProfileRequest req when CoopSession.IsHost:
                var runtime = EnemyRegistry.Snapshot().FirstOrDefault(r => NetIdOf(r.Actor) == req.EnemyNetId);
                if (runtime != null) CotsNetEvent.Instance.SendTo(Describe(runtime, req.EnemyNetId), from);
                break;
            case EnemySync s when !CoopSession.IsHost:
                if (!ReceivedProfiles.ContainsKey(s.EnemyNetId)) RLog.Msg($"{CoopTestJoin.Tag} client: got enemy {s.EnemyNetId} ({s.KindId}, level {s.Level})");
                ReceivedProfiles[s.EnemyNetId] = s;
                bool applied;
                try { applied = TryApply(s); }
                catch (Exception e) { RLog.Error($"{CoopTestJoin.Tag} client: mirroring enemy {s.EnemyNetId} failed: {e}"); applied = false; }
                if (!applied) Pending[s.EnemyNetId] = s;
                break;
            case EnemyHealth h when !CoopSession.IsHost:
                if (ReceivedProfiles.TryGetValue(h.EnemyNetId, out var known))
                {
                    ReceivedProfiles[h.EnemyNetId] = known with { Health = h.Health, Armor = h.Armor };
                    if (Mirrors.TryGetValue(h.EnemyNetId, out var mirror)) ApplyHealth(mirror, known.MaxHealth, h.Health);
                }
                break;
        }
    }
}
