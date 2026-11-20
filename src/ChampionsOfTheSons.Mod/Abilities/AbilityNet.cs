using Bolt;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Net;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Abilities;

/// <summary>
/// COS-79, host side: every active ability cast is sent to each client as an <see cref="AbilityEvent"/> with the target spot and
/// a seed, and whether that client is the one aimed at. Only enemies the clients know (a Bolt network id) are sent.
/// </summary>
internal static class AbilityNet
{
    /// <summary>Casts sent so far (co-op test logs).</summary>
    public static int Sent { get; private set; }

    public static void SendBegin(EnemyRuntime enemy, EnemyAbility ability, Vector3 target, ulong seed, uint targetId) =>
        Send(enemy, ability, AbilityPhase.Begin, target, seed, targetId);

    public static void SendLock(EnemyRuntime enemy, EnemyAbility ability, Vector3 point) =>
        Send(enemy, ability, AbilityPhase.Lock, point, 0, uint.MaxValue);

    private static void Send(EnemyRuntime enemy, EnemyAbility ability, AbilityPhase phase, Vector3 point, ulong seed, uint targetId)
    {
        if (!CoopSession.IsHost || CoopFeature.Verified.Count == 0) return;
        ulong netId = EnemySyncFeature.NetIdOf(enemy.Actor);
        if (netId == 0) return;
        foreach (var peer in CoopSession.Peers)
        {
            try
            {
                CotsNetEvent.Instance.SendTo(new AbilityEvent(netId, ability.ToString(), phase, CoopSession.ToVec(point), seed,
                    CoopSession.IdOf(peer) == targetId), peer);
            }
            catch (Exception e)
            {
                RLog.Warning($"{CoopTestJoin.Tag} ability send failed: {e.Message}");
            }
        }
        Sent++;
        if (CoopTest.Enabled || CoopSelfTest.ServerDriver)
            RLog.Msg($"{CoopTestJoin.Tag} host: {ability} {phase} of enemy {netId} at {point} (target player {targetId}) sent");
    }
}

/// <summary>
/// COS-79, client side: copies of the host enemies' abilities. Auras (Blizzard, Radiance) run on every mirrored enemy that has
/// one; casts start when an <see cref="AbilityEvent"/> arrives. Each copy draws the same visuals and resolves damage, slows,
/// roots and pulls on this client's own player, with the enemy's synced damage multiplier.
/// </summary>
internal static class AbilityReplicas
{
    private static readonly Dictionary<(EnemyRuntime Enemy, EnemyAbility Ability), AbilityInstance> Live = new();

    /// <summary>Casts started from host messages, by ability (co-op test).</summary>
    public static readonly Dictionary<EnemyAbility, int> Started = new();

    /// <summary>Aura copies created (co-op test).</summary>
    public static int AurasRun { get; private set; }

    private static bool _hooked;

    public static void Init()
    {
        if (_hooked) return;
        _hooked = true;
        CotsNetEvent.Received += OnNet;
    }

    public static void Clear()
    {
        foreach (var i in Live.Values) Dispose(i);
        Live.Clear();
    }

    private static AbilityInstance? GetOrCreate(EnemyRuntime enemy, EnemyAbility ability)
    {
        if (Live.TryGetValue((enemy, ability), out var i)) return i;
        var handler = ActiveAbilitySystem.Find(ability);
        if (handler == null) return null;
        i = handler.Create(enemy);
        i.Replica = true; // the factories build host instances; a copy is marked before it ever ticks
        Live[(enemy, ability)] = i;
        return i;
    }

    public static void Tick(float dt)
    {
        foreach (var enemy in EnemyRegistry.Snapshot())
        {
            if (!enemy.IsActive) continue;
            foreach (var aura in new[] { EnemyAbility.Blizzard, EnemyAbility.Radiance })
                if (enemy.Profile.Has(aura) && !Live.ContainsKey((enemy, aura)) && GetOrCreate(enemy, aura) != null) AurasRun++;
        }
        foreach (var key in Live.Keys.ToList())
        {
            var i = Live[key];
            // Gone from the registry (died, or mirrored again under a new stats object): its copy goes too.
            if (!key.Enemy.IsActive || EnemyRegistry.Get(key.Enemy.Stats) != key.Enemy)
            {
                Dispose(i);
                Live.Remove(key);
                continue;
            }
            try
            {
                i.Tick(dt);
            }
            catch (Exception e)
            {
                RLog.Error($"Champions of the Sons: {key.Ability} copy failed: {e}");
                Dispose(i);
                Live.Remove(key);
            }
        }
        AbilityForces.Age(dt);
    }

    private static void Dispose(AbilityInstance i)
    {
        try { i.Dispose(); }
        catch (Exception e) { RLog.Error($"Champions of the Sons: ability copy cleanup failed: {e}"); }
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is not AbilityEvent ev || !CoopSession.IsClient) return;
        try
        {
            if (!Enum.TryParse<EnemyAbility>(ev.Ability, out var ability)) return;
            var enemy = EnemyRegistry.Get(EnemySyncFeature.ActorOf(ev.EnemyNetId));
            if (enemy == null || !enemy.IsActive)
            {
                if (CoopTest.Enabled) RLog.Msg($"{CoopTestJoin.Tag} client: {ev.Ability} of enemy {ev.EnemyNetId} skipped: {(enemy == null ? "no mirror" : "not active")}");
                return;
            }
            if (GetOrCreate(enemy, ability) is not CastInstance cast) return;
            var point = CoopSession.ToUnity(ev.Point);
            if (ev.Phase == AbilityPhase.Lock)
            {
                cast.OnLock(point);
                return;
            }
            cast.StartReplica(point, ev.TargetIsYou, ev.Seed);
            Started[ability] = Started.GetValueOrDefault(ability) + 1;
            if (CoopTest.Enabled) RLog.Msg($"{CoopTestJoin.Tag} client: playing {ability} of enemy {ev.EnemyNetId} at {point} (aimed at me: {ev.TargetIsYou})");
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} ability event failed: {e}");
        }
    }
}
