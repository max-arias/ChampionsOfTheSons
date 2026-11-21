using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using TheForest.Utils;
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

    public void Init()
    {
        CotsNetEvent.Received += OnNet;
        EnemyPatches.EnemyHit += OnHostDamage;
        EnemyCombatState.Applied += OnEffectApplied;
        PlayerBridge.PlayerHurt += (damage, dodged, kind) =>
        {
            if (dodged || damage <= 0.5 || !CoopSession.Active) return;
            _hurt += damage;
            _hurtMagic |= kind == DamageKind.Magic;
        };
    }

    /// <summary>True while the host applies a client's hit (that one is shown to the others by <see cref="ApplyRemoteHit"/>).</summary>
    private static bool _applyingRemote;

    /// <summary>Damage numbers the host sent for its own hits, damage over time and spells (COS-81, co-op test).</summary>
    public static int HostNumbersSent { get; private set; }

    /// <summary>Host: every other hit that lands on an enemy (the host's own swings and spells, bleed and burn ticks, thorns)
    /// is shown to the clients too, so everyone sees every hit (COS-81).</summary>
    private static void OnHostDamage(VailActor actor, EnemyRuntime runtime, double damage, bool crit)
    {
        try
        {
            if (_applyingRemote || damage <= 0.5 || !CoopSession.IsHost || CoopFeature.Verified.Count == 0) return;
            ulong id = EnemySyncFeature.NetIdOf(actor);
            if (id == 0) return;
            Vector3 pos = runtime.LastPosition ?? actor.transform.position;
            pos += Vector3.up * 1.7f * Mathf.Max(0.5f, (float)runtime.Profile.Scale);
            var flags = crit ? HitFlags.Crit : HitFlags.None;
            if (EnemyPatches.LastHitKind == DamageKind.Magic) flags |= HitFlags.Magic;
            else if (EnemyPatches.LastHitKind == DamageKind.Pure) flags |= HitFlags.Pure;
            CoopSession.SendToPeers(new DamageShown(CoopSession.HostId, id, CoopSession.ToVec(pos), (float)damage, flags));
            HostNumbersSent++;
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} damage number broadcast failed: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- damage the local player takes (COS-114)

    private static double _hurt;
    private static bool _hurtMagic;
    private static float _hurtTimer;

    /// <summary>Damage the local player took is shown to the teammates, summed over a short window so a DoT is one number a tick.</summary>
    public void Update(float dt, bool inWorld)
    {
        _hurtTimer -= dt;
        if (_hurtTimer > 0 || _hurt <= 0) return;
        _hurtTimer = 0.3f;
        double amount = _hurt;
        bool magic = _hurtMagic;
        _hurt = 0;
        _hurtMagic = false;
        if (!inWorld || !CoopSession.Active || LocalPlayer.Transform == null || (CoopSession.IsClient && !CoopFeature.Accepted)) return;
        var shown = new DamageShown(CoopSession.HostId, 0, CoopSession.ToVec(LocalPlayer.Transform.position + Vector3.up * 2.1f), (float)amount,
            magic ? HitFlags.Magic : HitFlags.None);
        CoopSession.Broadcast(shown);
    }

    // ---------------------------------------------------------------- client: outgoing hits

    /// <summary>Kill XP the host has granted this client (co-op test checks).</summary>
    public static long XpReceived { get; private set; }

    /// <summary>Hits and effects the client forwarded to the host (co-op test checks).</summary>
    public static int Forwarded { get; private set; }
    public static int EffectsForwarded { get; private set; }

    /// <summary>True on an accepted co-op client: hits, debuffs and pushes on host enemies go to the host (COS-13).</summary>
    public static bool ForwardsToHost => CoopSession.IsClient && CoopFeature.Accepted && !EnemyRegistry.IsAuthority;

    public static HitFlags WeaponFlag(WeaponClass weapon) => weapon switch
    {
        WeaponClass.Ranged => HitFlags.Ranged,
        WeaponClass.Thrown => HitFlags.Thrown,
        _ => HitFlags.Melee,
    };

    /// <summary>Client: sends one of the local player's hits on a host enemy (weapon, spell, thorns, splash) to the host.
    /// The hit comes out of the same pipeline as the host's own (stats, procs, headshots); the host applies defense.</summary>
    public static void Forward(EnemyRuntime runtime, OutgoingHit hit, HitFlags weapon)
    {
        ulong id = EnemySyncFeature.NetIdOf(runtime.Actor);
        if (id == 0 || hit.Amount <= 0) return;
        var flags = weapon;
        if (hit.Crit) flags |= HitFlags.Crit;
        if (hit.Kind == DamageKind.Magic) flags |= HitFlags.Magic;
        if (hit.Kind == DamageKind.Pure) flags |= HitFlags.Pure;
        CoopSession.SendToHost(new Hit(id, (float)hit.Amount, flags, (float)hit.ArmorPierce));
        Forwarded++;
        if (CoopTest.Enabled) RLog.Msg($"{CoopTestJoin.Tag} client: sent Hit {hit.Amount:F0} ({flags}) to enemy {id}");
    }

    /// <summary>Co-op test: the hit the local stat sheet makes from a weapon swing of <paramref name="rawAmount"/>.</summary>
    public static OutgoingHit LocalHit(ImpactType impactType, float rawAmount, bool head) =>
        PlayerAttack.Weapon(PlayerState.Character.Stats, impactType == ImpactType.Projectile ? WeaponClass.Ranged : WeaponClass.Melee, rawAmount, Rng,
            headshot: impactType == ImpactType.Projectile && head);

    /// <summary>Co-op test: sends a hit message straight to the host.</summary>
    public static void SendHit(ulong enemyNetId, OutgoingHit hit, HitFlags flags)
    {
        if (enemyNetId == 0) return;
        CoopSession.SendToHost(new Hit(enemyNetId, (float)hit.Amount, flags, (float)hit.ArmorPierce));
    }

    /// <summary>True while the host applies a client's forwarded effect.</summary>
    private static bool _applyingEffect;

    /// <summary>Client: a debuff our hit or spell put on a mirror goes to the host's enemy too.</summary>
    private static void OnEffectApplied(EnemyCombatState state, EnemyEffect e)
    {
        try
        {
            if (_applyingEffect || !ForwardsToHost) return;
            var runtime = EnemyRegistry.Live.FirstOrDefault(r => ReferenceEquals(r.Combat, state));
            ulong id = runtime == null ? 0 : EnemySyncFeature.NetIdOf(runtime.Actor);
            if (id == 0) return;
            CoopSession.SendToHost(new EnemyEffectNet(id, e.Kind, e.Source, (float)e.Amount, (float)e.Seconds, e.Dot));
            EffectsForwarded++;
        }
        catch (Exception ex)
        {
            RLog.Warning($"{CoopTestJoin.Tag} effect forward failed: {ex.Message}");
        }
    }

    /// <summary>Client: a spell pushed or pulled a mirror; the host moves its enemy the same way.</summary>
    public static void ForwardPush(EnemyRuntime runtime, Vector3 delta)
    {
        ulong id = EnemySyncFeature.NetIdOf(runtime.Actor);
        if (id != 0) CoopSession.SendToHost(new EnemyPush(id, CoopSession.ToVec(delta)));
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
                case EnemyEffectNet fx when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    ApplyRemoteEffect(CoopSession.IdOf(from), fx);
                    break;
                case EnemyPush push when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    var pushed = FindLive(push.EnemyNetId);
                    var delta = CoopSession.ToUnity(push.Delta);
                    // A spell moves an enemy a few metres at most: refuse larger claims.
                    if (pushed is { IsActive: true, Actor: not null } && delta.magnitude <= 20f) pushed.Actor.transform.position += delta;
                    break;
                case XpGranted xp when CoopSession.IsClient:
                    XpReceived += xp.Amount;
                    PlayerState.Character.OnKill(xp.Amount);
                    RLog.Msg($"{CoopTestJoin.Tag} client: granted {xp.Amount} XP by the host");
                    CoopTest.Check("xp: client received its kill XP", true, $"{xp.Amount}");
                    break;
                case PlayerPos pos when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    CoopSession.SetPosition(from, pos.Position);
                    CoopSession.SetLevel(from, pos.Level);
                    break;
                case DamageShown hurt when CoopSession.IsHost && hurt.EnemyNetId == 0 && CoopFeature.Verified.Contains(from):
                    // A client's own damage taken (COS-114): to the other clients, and on the host's screen.
                    var relayed = hurt with { PlayerId = CoopSession.IdOf(from) };
                    CoopSession.SendToPeers(relayed, except: from);
                    var hp = CoopSession.ToUnity(relayed.Position);
                    if (LocalPlayer.Transform != null && Vector3.Distance(hp, LocalPlayer.Transform.position) <= DamageNumberRange)
                        CoopFx.ShowDamage(hp, relayed.Amount, false);
                    break;
                case DamageShown shown when CoopSession.IsClient:
                    // Only hits near this player (COS-81); the setting is checked where the number is drawn.
                    var at = CoopSession.ToUnity(shown.Position);
                    if (LocalPlayer.Transform == null || Vector3.Distance(at, LocalPlayer.Transform.position) <= DamageNumberRange)
                        CoopFx.ShowDamage(at, shown.Amount, (shown.Flags & HitFlags.Crit) != 0);
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} net handler failed: {e}");
        }
    }

    /// <summary>Teammates' and the host's hits farther than this from the local player show no number.</summary>
    private const float DamageNumberRange = 80f;

    private static EnemyRuntime? FindLive(ulong netId) => EnemyRegistry.Live.FirstOrDefault(r => EnemySyncFeature.NetIdOf(r.Actor) == netId);

    /// <summary>Host: a client's debuff on one of our enemies (COS-13), clamped to sane values.</summary>
    private static void ApplyRemoteEffect(uint playerId, EnemyEffectNet fx)
    {
        var runtime = FindLive(fx.EnemyNetId);
        if (runtime == null || !float.IsFinite(fx.Amount) || !float.IsFinite(fx.Seconds)) return;
        double seconds = System.Math.Clamp(fx.Seconds, 0, 60);
        double amount = fx.Kind switch
        {
            EnemyEffectKind.Slow => System.Math.Clamp(fx.Amount, 0, 1),
            EnemyEffectKind.Weaken => System.Math.Clamp(fx.Amount, 1, 5),
            _ => System.Math.Max(0, fx.Amount),
        };
        // Sources are per player: two players' bleeds run side by side, like their own hits would.
        string source = fx.Source.Length == 0 ? "" : $"{fx.Source}#{playerId}";
        LastHitter[fx.EnemyNetId] = playerId;
        _applyingEffect = true;
        try { runtime.Combat.Apply(new EnemyEffect(fx.Kind, source, amount, seconds, fx.Dot)); }
        finally { _applyingEffect = false; }
    }

    private static void ApplyRemoteHit(uint playerId, Hit hit, BoltConnection from)
    {
        var runtime = FindLive(hit.EnemyNetId);
        if (runtime == null) return;
        // Clamp what a client may claim: finite, non-negative.
        float amount = float.IsFinite(hit.Amount) ? Mathf.Max(0, hit.Amount) : 0;
        if (amount <= 0) return;
        var kind = (hit.Flags & HitFlags.Pure) != 0 ? DamageKind.Pure : (hit.Flags & HitFlags.Magic) != 0 ? DamageKind.Magic : DamageKind.Physical;
        var outgoing = new OutgoingHit(amount, kind, (hit.Flags & HitFlags.Crit) != 0, System.Math.Max(0, hit.ArmorPierce));
        LastHitter[hit.EnemyNetId] = playerId;
        double before = runtime.Stats == null ? 0 : EnemyRegistry.Health(runtime.Stats)?._currentValue ?? 0;
        _applyingRemote = true;
        try { EnemyPatches.DealForced(runtime, outgoing, creditPlayer: false); }
        finally { _applyingRemote = false; }
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
