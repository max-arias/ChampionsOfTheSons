using Bolt;
using ChampionsOfTheSons.Core.Networking;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// Co-op session handling: the join handshake (version check, world settings) and the connection state other
/// co-op features read. The host (listen server or dedicated server) is authoritative; a client sends
/// <see cref="Hello"/> once it is in the world and gets <see cref="WorldSettings"/> or a <see cref="HelloReject"/>.
/// </summary>
public sealed class CoopFeature : IModFeature
{
    public const string Tag = CoopTestJoin.Tag;

    public static string ModVersion => ChampionsOfTheSonsMod.Instance!.Manifest.Version;

    public static bool IsClient => BoltNetwork.isRunning && BoltNetwork.isClient;
    public static bool IsHost => BoltNetwork.isRunning && BoltNetwork.isServer;

    /// <summary>Client side: the host accepted our Hello.</summary>
    public static bool Accepted { get; private set; }

    /// <summary>Client side: why the host refused us, or null.</summary>
    public static string? RejectReason { get; private set; }

    /// <summary>Host side: connections that passed the version check.</summary>
    public static readonly HashSet<BoltConnection> Verified = new(ConnectionComparer.Instance);

    private bool _sentHello;
    private float _resend;

    public void Init()
    {
        CotsNetEvent.Received += OnNet;
    }

    public void OnGameStart()
    {
        _sentHello = false;
        Accepted = false;
        RejectReason = null;
        Verified.Clear();
    }

    public void Update(float dt, bool inWorld)
    {
        CoopTest.Update();
        if (!inWorld || !IsClient || Accepted || RejectReason != null) return;
        _resend -= dt;
        if (_resend > 0) return;
        _resend = _sentHello ? 5f : 0f;
        _sentHello = true;
        try
        {
            CotsNetEvent.Instance.Send(new Hello(MessageCodec.ProtocolVersion, ModVersion), GlobalTargets.OnlyServer);
            RLog.Msg($"{Tag} client: sent Hello (protocol {MessageCodec.ProtocolVersion}, mod {ModVersion})");
        }
        catch (Exception e)
        {
            RLog.Error($"{Tag} client: could not send Hello: {e.Message}");
        }
    }

    /// <summary>Host: forget clients that left (the party feature calls this every few seconds).</summary>
    internal static void PruneGone()
    {
        if (!IsHost || Verified.Count == 0) return;
        var alive = new HashSet<BoltConnection>(ConnectionComparer.Instance);
        var it = BoltNetwork.clients.GetEnumerator();
        var nonGeneric = it.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (nonGeneric.MoveNext()) alive.Add(it.Current);
        foreach (var c in Verified.ToList())
            if (!alive.Contains(c)) CoopSession.Forget(c);
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        switch (message)
        {
            case Hello hello when IsHost:
                RLog.Msg($"{Tag} host: got Hello from {from.RemoteEndPoint} (protocol {hello.ProtocolVersion}, mod {hello.ModVersion})");
                var reason = Handshake.Check(hello, MessageCodec.ProtocolVersion, ModVersion);
                if (reason != null)
                {
                    RLog.Warning($"{Tag} host: rejecting {from.RemoteEndPoint}: {reason}");
                    CotsNetEvent.Instance.SendTo(new HelloReject(reason), from);
                    return;
                }
                Verified.Add(from);
                EnemySyncFeature.ResendAll(); // a late joiner gets every enemy that already exists (COS-79)
                CotsNetEvent.Instance.SendTo(new WorldSettings(WorldState.Rules.Difficulty.Id, WorldState.FriendlyFire), from);
                RLog.Msg($"{Tag} host: accepted {from.RemoteEndPoint}, sent WorldSettings ({WorldState.Rules.Difficulty.Id})");
                break;
            case WorldSettings ws when !IsHost:
                if (!Accepted) CoopTest.Check("handshake: host accepted the client", true, $"difficulty {ws.DifficultyId}");
                Accepted = true;
                RLog.Msg($"{Tag} client: host accepted us; WorldSettings difficulty={ws.DifficultyId} friendlyFire={ws.FriendlyFire}");
                break;
            case HelloReject rej when !IsHost:
                RejectReason = rej.Reason;
                RLog.Error($"{Tag} client: the host refused us: {rej.Reason}");
                break;
        }
    }
}
