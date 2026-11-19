using Bolt;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Ai.Vail.StimuliTypes;
using Sons.Weapon;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// COS-13: in co-op a client works out its own hits from its own stat sheet and sends them as <see cref="Hit"/>; the host applies
/// the enemy's defense and owns health. The host then shows the damage to everyone (<see cref="DamageShown"/>, COS-44) and
/// pays kill XP to the players near the kill (<see cref="XpGranted"/>).
/// The vanilla damage a remote player's swing causes on the host is dropped while mod clients are connected: the
/// <see cref="Hit"/> replaces it (otherwise it would land twice, once raw and once with stats).
/// </summary>
[HarmonyPatch]
public sealed class CoopHits : IModFeature
{
    private static readonly Rng Rng = Rng.FromTime();

    /// <summary>Net id of enemies a remote player hit last, by the player id, for kill credit.</summary>
    private static readonly Dictionary<ulong, uint> LastHitter = new();

    /// <summary>True on a host that has verified clients: remote players' vanilla hits are replaced by <see cref="Hit"/> messages.</summary>
    public static bool ReplacesVanillaRemoteHits => CoopSession.IsHost && CoopFeature.Verified.Count > 0;

    /// <summary>Self-tests: the last hit message the host applied.</summary>
    public static (uint Player, ulong Enemy, double Landed)? LastApplied { get; private set; }

    public void Init() => CotsNetEvent.Received += OnNet;

    // ---------------------------------------------------------------- client: outgoing hits

    /// <summary>The hit the local player's stat sheet makes from a weapon swing of <paramref name="rawAmount"/>.</summary>
    public static OutgoingHit LocalHit(ImpactType impactType, float rawAmount, bool head)
    {
        var sheet = PlayerState.Character.Stats;
        var weapon = impactType switch
        {
            ImpactType.Projectile => WeaponClass.Ranged,
            ImpactType.Explosion => WeaponClass.Thrown,
            _ => WeaponClass.Melee,
        };
        var hit = PlayerAttack.Weapon(sheet, weapon, rawAmount, Rng, headshot: weapon == WeaponClass.Ranged && head);
        if (weapon == WeaponClass.Thrown) hit = hit with { Amount = hit.Amount * (1 + sheet.Get(CombatStatIds.ExplosionDamage)) * sheet.Get(CombatStatIds.ThrownSpearDamage) };
        return hit;
    }

    public static HitFlags FlagsOf(OutgoingHit hit, ImpactType type)
    {
        var f = HitFlags.None;
        if (hit.Crit) f |= HitFlags.Crit;
        if (hit.Kind == DamageKind.Magic) f |= HitFlags.Magic;
        if (hit.Kind == DamageKind.Pure) f |= HitFlags.Pure;
        f |= type switch { ImpactType.Projectile => HitFlags.Ranged, ImpactType.Explosion => HitFlags.Thrown, _ => HitFlags.Melee };
        return f;
    }

    /// <summary>Sends one outgoing hit of the local player to the host (client side).</summary>
    public static void SendHit(ulong enemyNetId, OutgoingHit hit, HitFlags flags)
    {
        if (enemyNetId == 0) return;
        CoopSession.SendToHost(new Hit(enemyNetId, (float)hit.Amount, flags, (float)hit.ArmorPierce));
        if (CoopTest.Enabled) RLog.Msg($"{CoopTestJoin.Tag} client: sent Hit {hit.Amount:F0} to enemy {enemyNetId}");
    }

    [HarmonyPatch(typeof(VailActor), nameof(VailActor.ReceivedDamage))]
    [HarmonyPrefix]
    private static void BeforeReceivedDamage(VailActor __instance, ImpactType impactType, MonoBehaviourStimuli sourceStimuli, ref float amount, bool fromPlayer)
    {
        try
        {
            if (amount <= 0 || !fromPlayer || !CoopSession.Active) return;
            if (CoopSession.IsClient)
            {
                // A client's swing: our own numbers go to the host; the vanilla amount is not applied twice.
                var player = sourceStimuli?.TryCast<PlayerStimuli>();
                if (player == null || !player.IsLocalPlayer()) return;
                ulong id = EnemySyncFeature.NetIdOf(__instance);
                if (id == 0 || !CoopFeature.Accepted) return;
                var hit = LocalHit(impactType, amount, head: false);
                SendHit(id, hit, FlagsOf(hit, impactType));
                amount = 0;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} client hit hook failed: {e}");
        }
    }

    // ---------------------------------------------------------------- host: incoming hits

    private static void OnNet(IMessage message, BoltConnection from)
    {
        try
        {
            switch (message)
            {
                case Hit hit when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    ApplyRemoteHit(CoopSession.IdOf(from), hit, from);
                    break;
                case XpGranted xp when CoopSession.IsClient:
                    PlayerState.Character.OnKill(xp.Amount);
                    RLog.Msg($"{CoopTestJoin.Tag} client: granted {xp.Amount} XP by the host");
                    CoopTest.Check("xp: client received its kill XP", true, $"{xp.Amount}");
                    break;
                case PlayerPos pos when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    CoopSession.SetPosition(from, pos.Position);
                    CoopSession.SetLevel(from, pos.Level);
                    break;
                case DamageShown shown when CoopSession.IsClient:
                    CoopFx.ShowDamage(CoopSession.ToUnity(shown.Position), shown.Amount, (shown.Flags & HitFlags.Crit) != 0);
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} net handler failed: {e}");
        }
    }

    private static void ApplyRemoteHit(uint playerId, Hit hit, BoltConnection from)
    {
        var runtime = EnemyRegistry.Snapshot().FirstOrDefault(r => EnemySyncFeature.NetIdOf(r.Actor) == hit.EnemyNetId);
        if (runtime == null) return;
        // Clamp what a client may claim: finite, non-negative.
        float amount = float.IsFinite(hit.Amount) ? Mathf.Max(0, hit.Amount) : 0;
        if (amount <= 0) return;
        var kind = (hit.Flags & HitFlags.Pure) != 0 ? DamageKind.Pure : (hit.Flags & HitFlags.Magic) != 0 ? DamageKind.Magic : DamageKind.Physical;
        var outgoing = new OutgoingHit(amount, kind, (hit.Flags & HitFlags.Crit) != 0, System.Math.Max(0, hit.ArmorPierce));
        LastHitter[hit.EnemyNetId] = playerId;
        double before = runtime.Stats == null ? 0 : EnemyRegistry.Health(runtime.Stats)?._currentValue ?? 0;
        EnemyPatches.DealForced(runtime, outgoing, creditPlayer: false);
        double after = runtime.Stats == null ? 0 : EnemyRegistry.Health(runtime.Stats)?._currentValue ?? 0;
        double landed = before - after;
        LastApplied = (playerId, hit.EnemyNetId, landed);
        RLog.Msg($"{CoopTestJoin.Tag} host: applied player {playerId}'s hit {amount:F0} -> {landed:F0} landed on enemy {hit.EnemyNetId} (level {runtime.Profile.Level})");
        Vector3 pos = runtime.LastPosition ?? (runtime.Actor != null ? runtime.Actor.transform.position : Vector3.zero);
        pos += Vector3.up * 1.5f;
        var shown = new DamageShown(playerId, hit.EnemyNetId, CoopSession.ToVec(pos), (float)landed, hit.Flags);
        CoopSession.SendToPeers(shown, except: from);
        CoopFx.ShowDamage(pos, (float)landed, (hit.Flags & HitFlags.Crit) != 0);
    }

    // ---------------------------------------------------------------- host: kill XP

    /// <summary>Whether a remote player (not the host's own player) landed the last hit on this enemy.</summary>
    public static bool HitByRemote(EnemyRuntime runtime) => LastHitter.ContainsKey(EnemySyncFeature.NetIdOf(runtime.Actor));

    /// <summary>
    /// Pays kill XP: every player in range of the kill gets <see cref="KillCredit.ShareFor"/>. Returns the host's own share
    /// (0 when the host's player is out of range). Without co-op the local player gets the whole bounty, as before.
    /// </summary>
    public static double DistributeKillXp(EnemyRuntime runtime)
    {
        if (!CoopSession.IsHost || CoopFeature.Verified.Count == 0) return EnemyScaler.KillXpShare(runtime.Profile.Bounty, 1);
        Vector3 at = runtime.LastPosition ?? (runtime.Actor != null ? runtime.Actor.transform.position : Vector3.zero);
        var players = CoopSession.Players();
        var inRange = KillCredit.Recipients(players, CoopSession.ToVec(at));
        if (inRange.Count == 0) inRange = players.Select(p => p.Id).ToList(); // positions unknown: nobody is left out
        double share = KillCredit.ShareFor(runtime.Profile.Bounty, inRange.Count);
        foreach (var id in inRange)
        {
            if (id == CoopSession.HostId) continue;
            var conn = CoopSession.ByPlayerId(id);
            if (conn != null) CotsNetEvent.Instance.SendTo(new XpGranted((long)share), conn);
        }
        LastHitter.Remove(EnemySyncFeature.NetIdOf(runtime.Actor));
        RLog.Msg($"{CoopTestJoin.Tag} host: kill XP {share:F0} each to {inRange.Count} player(s)");
        return inRange.Contains(CoopSession.HostId) ? share : 0;
    }
}
