using Bolt;
using ChampionsOfTheSons.Core.Networking;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>Who is in the game and where, plus the send helpers every co-op feature uses.</summary>
public static class CoopSession
{
    /// <summary>The host's own player id (a dedicated server has no local player but still uses it for host-made events).</summary>
    public const uint HostId = 0;

    private static readonly Dictionary<BoltConnection, Vec3> Positions = new();
    private static readonly Dictionary<BoltConnection, int> Levels = new();

    public static bool Active => BoltNetwork.isRunning;
    public static bool IsHost => BoltNetwork.isRunning && BoltNetwork.isServer;
    public static bool IsClient => BoltNetwork.isRunning && BoltNetwork.isClient;

    /// <summary>Id the host gives a client (stable for the connection).</summary>
    public static uint IdOf(BoltConnection connection) => (uint)connection.ConnectionId + 1;

    public static List<BoltConnection> Peers => CoopFeature.Verified.ToList();

    public static BoltConnection? ByPlayerId(uint id) => Peers.FirstOrDefault(c => IdOf(c) == id);

    public static void Forget(BoltConnection connection)
    {
        Positions.Remove(connection);
        Levels.Remove(connection);
        CoopFeature.Verified.Remove(connection);
    }

    public static bool TryPosition(BoltConnection connection, out Vec3 position) => Positions.TryGetValue(connection, out position);

    public static void SetLevel(BoltConnection connection, int level) => Levels[connection] = level;

    public static int LevelOf(BoltConnection connection) => Levels.GetValueOrDefault(connection, 1);

    /// <summary>The level a kill's loot is rolled at: by the host's loot-level policy over every player the host knows.
    /// A dedicated server has no character of its own, so only the clients count then.</summary>
    public static int LootLevel(int hostLevel)
    {
        var nearby = new List<(int Level, float Distance)>();
        if (LocalPlayer.IsInWorld && LocalPlayer.Transform != null) nearby.Add((hostLevel, 0));
        foreach (var c in Peers) nearby.Add((LevelOf(c), 0));
        return KillCredit.LootLevel(WorldState.Rules.LootLevel, nearby, hostLevel);
    }

    public static void SetPosition(BoltConnection connection, Vec3 position) => Positions[connection] = position;

    /// <summary>Every player the host knows with a position: itself (if it has a body) and each verified client.</summary>
    public static List<(uint Id, Vec3 Position)> Players()
    {
        var list = new List<(uint, Vec3)>();
        if (LocalPlayer.IsInWorld && LocalPlayer.Transform != null)
        {
            var p = LocalPlayer.Transform.position;
            list.Add((HostId, new Vec3(p.x, p.y, p.z)));
        }
        foreach (var c in Peers)
            if (Positions.TryGetValue(c, out var pos)) list.Add((IdOf(c), pos));
        return list;
    }

    public static Vec3 ToVec(Vector3 v) => new(v.x, v.y, v.z);
    public static Vector3 ToUnity(Vec3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Host: sends to every verified client, optionally skipping one (the sender of a relayed message).</summary>
    public static void SendToPeers(IMessage message, BoltConnection? except = null)
    {
        if (!IsHost) return;
        foreach (var c in Peers)
        {
            if (c == except) continue;
            try { CotsNetEvent.Instance.SendTo(message, c); }
            catch (Exception e) { RLog.Warning($"{CoopTestJoin.Tag} send {message.Type} failed: {e.Message}"); }
        }
    }

    /// <summary>Client: sends to the host.</summary>
    public static void SendToHost(IMessage message)
    {
        if (!IsClient) return;
        try { CotsNetEvent.Instance.Send(message, GlobalTargets.OnlyServer); }
        catch (Exception e) { RLog.Warning($"{CoopTestJoin.Tag} send {message.Type} failed: {e.Message}"); }
    }

    /// <summary>From either side: client → host, host → all clients.</summary>
    public static void Broadcast(IMessage message)
    {
        if (IsHost) SendToPeers(message);
        else SendToHost(message);
    }
}
