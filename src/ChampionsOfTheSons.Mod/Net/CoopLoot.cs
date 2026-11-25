using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using HarmonyLib;
using RedLoader;
using UnityEngine;
using PickupReq = ChampionsOfTheSons.Core.Networking.PickupRequest;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// COS-14: drops are made by the host only. The host tells every client about each drop and its removal; a client that walks
/// over a drop asks the host (<see cref="PickupReq"/>) and only the first request wins (<see cref="PickupArbiter"/>): the
/// winner gets <see cref="PickupGranted"/>, everyone else just sees the drop vanish.
/// </summary>
[HarmonyPatch]
public sealed class CoopLoot : IModFeature
{
    private static readonly Dictionary<int, float> Asked = new();

    /// <summary>Self-tests: the drops this client currently shows because of the host.</summary>
    public static int MirroredCount { get; private set; }

    /// <summary>Self-tests: last pickup the client was granted.</summary>
    public static ItemInstance? LastGranted { get; private set; }

    public void Init()
    {
        CotsNetEvent.Received += OnNet;
        LootFeature.Ground.Added += d =>
        {
            if (CoopSession.IsHost) CoopSession.SendToPeers(new DropSpawned((uint)d.Id, d.Item, new Vec3(d.X, d.Y, d.Z), (float)d.Remaining));
        };
        LootFeature.Ground.Removed += (d, _) =>
        {
            if (CoopSession.IsHost) CoopSession.SendToPeers(new DropRemoved((uint)d.Id));
        };
    }

    public void OnGameStart()
    {
        Asked.Clear();
        PendingThrows.Clear();
    }

    /// <summary>Where this client recently asked the host for a drop: the mirror that lands there is not walked over at once.</summary>
    private static readonly List<(Vector3 Pos, float At)> PendingThrows = new();

    /// <summary>Co-op client: asks the host to make a drop for everyone (COS-14). False when not a client: drop locally.</summary>
    public static bool RequestDrop(ItemInstance item, Vector3 pos, DropSource source)
    {
        if (!CoopSession.IsClient || !CoopFeature.Accepted) return false;
        pos = LootFeature.SnapToGround(pos);
        PendingThrows.Add((pos, Time.realtimeSinceStartup));
        CoopSession.SendToHost(new DropRequest(item, CoopSession.ToVec(pos), source));
        return true;
    }

    private static void MarkOwnThrow(int id, Vector3 at)
    {
        float now = Time.realtimeSinceStartup;
        PendingThrows.RemoveAll(p => now - p.At > 10f);
        int i = PendingThrows.FindIndex(p => new Vector2(p.Pos.x - at.x, p.Pos.z - at.z).magnitude < 1.5f);
        if (i < 0) return;
        PendingThrows.RemoveAt(i);
        LootFeature.NoAutoPickup.Add(id);
    }

    /// <summary>A client just passed the handshake: send it everything on the ground.</summary>
    public static void SendAllTo(BoltConnection connection)
    {
        foreach (var d in LootFeature.Ground.All.ToList())
            CotsNetEvent.Instance.SendTo(new DropSpawned((uint)d.Id, d.Item, new Vec3(d.X, d.Y, d.Z), (float)d.Remaining), connection);
    }

    // A client does not pick up by itself: it asks the host.
    [HarmonyPatch(typeof(LootFeature), nameof(LootFeature.Pickup))]
    [HarmonyPrefix]
    private static bool BeforePickup(int id, ref PickupResult __result)
    {
        if (!CoopSession.IsClient || !CoopFeature.Accepted) return true;
        float now = Time.realtimeSinceStartup;
        if (!Asked.TryGetValue(id, out var last) || now - last > 1.5f)
        {
            Asked[id] = now;
            CoopSession.SendToHost(new PickupReq((uint)id));
        }
        __result = PickupResult.Unknown;
        return false;
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        try
        {
            switch (message)
            {
                case DropSpawned ds when CoopSession.IsClient:
                    int stack = ModContent.Db.Items.TryGetValue(ds.Item.BaseId, out var def) ? def.StackSize : 1;
                    LootFeature.Ground.Mirror((int)ds.DropId, ds.Item, ds.Position.X, ds.Position.Y, ds.Position.Z, stack, ds.Remaining);
                    MarkOwnThrow((int)ds.DropId, new Vector3(ds.Position.X, ds.Position.Y, ds.Position.Z));
                    MirroredCount = LootFeature.Ground.Count;
                    break;
                case DropRemoved dr when CoopSession.IsClient:
                    LootFeature.Ground.Remove((int)dr.DropId);
                    Asked.Remove((int)dr.DropId);
                    MirroredCount = LootFeature.Ground.Count;
                    break;
                case DropRequest dq when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    var at = CoopSession.ToUnity(dq.Position);
                    // A throw lands next to the thrower: refuse drops far from where the client last said it was.
                    if (CoopSession.TryPosition(from, out var p) && Vector3.Distance(CoopSession.ToUnity(p), at) > 12f) break;
                    var made = LootFeature.SpawnAt(dq.Item, at, dq.Source == DropSource.Overflow ? DropSource.Overflow : DropSource.Thrown);
                    RLog.Msg($"{CoopTestJoin.Tag} host: player {CoopSession.IdOf(from)} dropped {dq.Item.BaseId} as drop {made.Id}");
                    break;
                case PickupReq req when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    OnPickupRequest(req, from);
                    break;
                case PickupGranted grant when CoopSession.IsClient:
                    OnGranted(grant);
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} loot handler failed: {e}");
        }
    }

    private static void OnPickupRequest(PickupReq req, BoltConnection from)
    {
        var drop = LootFeature.Ground.Get((int)req.DropId);
        if (drop == null) return; // already taken: the requester was told by DropRemoved
        if (CoopSession.TryPosition(from, out var pos) && Vector3.Distance(CoopSession.ToUnity(pos), new Vector3(drop.X, drop.Y, drop.Z)) > 8f)
            return; // too far away: a stale request or a cheater
        var item = PickupArbiter.Claim(LootFeature.Ground, req.DropId); // removes the drop; DropRemoved goes to everyone
        if (item == null) return;
        CotsNetEvent.Instance.SendTo(new PickupGranted(req.DropId, item), from);
        RLog.Msg($"{CoopTestJoin.Tag} host: pickup of drop {req.DropId} granted to player {CoopSession.IdOf(from)}");
    }

    private static void OnGranted(PickupGranted grant)
    {
        LastGranted = grant.Item;
        int stack = ModContent.Db.Items.TryGetValue(grant.Item.BaseId, out var def) ? def.StackSize : 1;
        if (!PlayerState.Character.Inventory.AddToBag(grant.Item, stack, out var rest))
        {
            // The bag filled up between the request and the answer: the rest drops at our feet (local only).
            LootFeature.Message(UI.UiText.T("ui.loot.bag_full", "Your bag is full."));
            if (rest != null && TheForest.Utils.LocalPlayer.IsInWorld) RequestDrop(rest, TheForest.Utils.LocalPlayer.Transform.position, DropSource.Overflow);
        }
        else LootFeature.AutoBind(grant.Item);
        RLog.Msg($"{CoopTestJoin.Tag} client: picked up {grant.Item.BaseId} (granted by the host)");
        CoopTest.Check("loot: client was granted the pickup", true, grant.Item.BaseId);
    }
}
