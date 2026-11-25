using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Bridge;
using RedLoader;
using SonsSdk.Networking;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// The host's half of the state tints (COS-106, COS-154): enemy states (shield up, revived) go to the clients, and a player's
/// state effects are relayed to the other players. In <c>Net</c> so a dedicated server runs it too (it loads only the networking,
/// loot and admin features); the tints themselves are drawn by <see cref="Visuals.StateTintsFeature"/>.
/// </summary>
public sealed class StateNetFeature : IModFeature
{
    private const float Scan = 0.15f;
    private static readonly Dictionary<IntPtr, EnemyStateFlags> Sent = new();
    private float _scan;

    public void Init() => CotsNetEvent.Received += OnNet;

    public void OnGameStart() => Sent.Clear();

    /// <summary>An enemy's state as the host sees it.</summary>
    public static EnemyStateFlags StateOf(EnemyRuntime e)
    {
        var flags = EnemyStateFlags.None;
        if (e.Combat.ShieldUp) flags |= EnemyStateFlags.ShieldUp;
        if (e.Revived) flags |= EnemyStateFlags.Revived;
        return flags;
    }

    public void Update(float dt, bool inWorld)
    {
        // Not gated on inWorld: a dedicated server has no player in the world.
        if (!CoopSession.Active || !CoopSession.IsHost) return;
        _scan -= dt;
        if (_scan > 0) return;
        _scan = Scan;
        try
        {
            foreach (var e in EnemyRegistry.Live)
            {
                if (e.Actor == null) continue;
                var flags = StateOf(e);
                if (Sent.GetValueOrDefault(e.Actor.Pointer) == flags) continue;
                Sent[e.Actor.Pointer] = flags;
                ulong id = EnemySyncFeature.NetIdOf(e.Actor);
                if (id != 0) CoopSession.SendToPeers(new EnemyState(id, flags));
                if ((CoopTest.Enabled || CoopSelfTest.ServerDriver) && flags != EnemyStateFlags.None)
                    RLog.Msg($"{CoopTestJoin.Tag} host: enemy {id} state {flags} {(id != 0 ? "sent" : "not sent (no net id)")}, host {CoopSession.IsHost}");
            }
        }
        catch (Exception ex)
        {
            RLog.Warning($"[COTS] enemy states: {ex.Message}");
        }
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is not PlayerStateNet p || !CoopSession.IsHost) return;
        try
        {
            if (!CoopFeature.Verified.Contains(from)) return;
            CoopSession.SendToPeers(p with { PlayerId = CoopSession.IdOf(from) }, except: from);
        }
        catch (Exception e)
        {
            RLog.Error($"[COTS] player state relay failed: {e}");
        }
    }
}
