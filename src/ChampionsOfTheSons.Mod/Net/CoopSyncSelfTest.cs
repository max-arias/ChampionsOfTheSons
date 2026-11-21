using Bolt;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Loot;
using RedLoader;
using Sons.Ai.Vail;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// Wave 5 co-op sync checks (COS-79/80/81), on top of <see cref="CoopSelfTest"/>: once its stages are done the server spawns an
/// enemy with Meteor (a projectile), Blizzard (an aura) and Chains (a root) and a scale of 1.6 next to the client, hits it with
/// the host's own damage, sends a teammate's info and a level-up line, and echoes the client's spell casts back as another
/// player's. The client checks it plays and is hit by the casts, sees the scale, the numbers, the party data, gets the enemy
/// profiles again after a rejoin (late join), and draws a teammate's Healing Dome, Portal (usable) and Black Flame on and off.
/// </summary>
public sealed class CoopSyncSelfTest : IModFeature
{
    private const uint EchoPlayer = 99, TestPeer = 77;
    private const float TestScale = 1.6f;

    // server
    private static int _stage;
    private static float _stageAt;
    private static VailActor? _enemy;
    private static bool _hooked;
    private static float _resent;

    public void Init()
    {
        if (CoopSelfTest.ServerDriver && !_hooked)
        {
            _hooked = true;
            CotsNetEvent.Received += Echo;
        }
    }

    public void OnGameStart()
    {
        _stage = 0;
        _lateDropPlaced = false;
        _eliteState = 0;
        _enemy = null;
    }

    // ================================================================= server half

    /// <summary>The client's casts come back as another player's, so the client draws a teammate's copy.</summary>
    private static void Echo(IMessage message, BoltConnection from)
    {
        if (!CoopSession.IsHost || !CoopFeature.Verified.Contains(from)) return;
        try
        {
            switch (message)
            {
                case SpellCast cast:
                    RLog.Msg($"{CoopTestJoin.Tag} server: got {cast.SpellId} ({cast.Mode}, cast {cast.CastId}, {cast.AllShapes.Count} visual(s)); echoing it");
                    CotsNetEvent.Instance.SendTo(cast with { PlayerId = EchoPlayer }, from);
                    break;
                case SpellFxEvent ev:
                    CotsNetEvent.Instance.SendTo(ev with { PlayerId = EchoPlayer }, from);
                    break;
                case PlayerInfo info:
                    RLog.Msg($"{CoopTestJoin.Tag} server: player info '{info.Name}' level {info.Level} mf {info.MagicFind:F2}; team magic find {CoopParty.TeamMagicFind():F2}");
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} server: echo failed: {e.Message}");
        }
    }

    public void Update(float dt, bool inWorld)
    {
        if (!CoopSelfTest.ServerDriver || CoopSelfTest.Stage < 7) return;
        var conn = CoopSession.Peers.FirstOrDefault(c => CoopSession.TryPosition(c, out _));
        if (conn == null || !CoopSession.TryPosition(conn, out var pos)) return;
        var at = CoopSession.ToUnity(pos);
        float now = Time.realtimeSinceStartup;
        if (_stageAt == 0) _stageAt = now;
        switch (_stage)
        {
            case 0: // a teammate's data and line; an ability enemy next to the client
                CoopSession.SendToPeers(new PlayerInfo(TestPeer, "Test Peer", 12, 1.5f, CoopSession.ToVec(at + new Vector3(3, 0, 3))));
                CoopSession.SendToPeers(new Announce(AnnounceKind.LevelUp, TestPeer, "Test Peer", 13));
                EnemyRegistry.TestProfileOverride = p => p with
                {
                    Abilities = new List<EnemyAbility> { EnemyAbility.Meteor, EnemyAbility.Blizzard, EnemyAbility.Chains },
                    Scale = TestScale,
                };
                try
                {
                    _enemy = ActorTools.Spawn(VailActorTypeId.Carl, at + new Vector3(10, 0, 0));
                    RLog.Msg($"{CoopTestJoin.Tag} server: spawned the ability enemy {(_enemy == null ? "FAILED" : "at " + _enemy.transform.position)}");
                }
                catch (Exception e)
                {
                    RLog.Error($"{CoopTestJoin.Tag} server: ability enemy spawn failed: {e}");
                }
                Next(now);
                break;
            case 1: // the host's own damage on it (a number for the client)
                if (now - _stageAt < 3) break;
                EnemyRegistry.TestProfileOverride = null;
                var rt = EnemyRegistry.Get(_enemy);
                RLog.Msg($"{CoopTestJoin.Tag} server: ability enemy {EnemySyncFeature.NetIdOf(_enemy)} abilities {(rt == null ? "NONE" : string.Join(",", rt.Profile.Abilities))} scale {rt?.Profile.Scale:F2} active {rt?.IsActive}");
                if (rt != null) EnemyPatches.DealForced(rt, new OutgoingHit(5, DamageKind.Pure, false, 0), creditPlayer: false);
                RLog.Msg($"{CoopTestJoin.Tag} server: host damage numbers sent {CoopHits.HostNumbersSent}");
                Next(now);
                break;
            case 2: // let it cast for a while (the client's ability, party and late-join steps run meanwhile)
                // A long-lived drop out of pickup reach for the client's late-join check (COS-146); placed once the client's
                // loot steps are done, so they never pick it by mistake.
                // An elite to the side for the client's tier-effect check (COS-146): the client draws it from the synced tier.
                if (_eliteState == 0 && now - _stageAt > 4)
                {
                    _eliteState = 1;
                    EnemyRegistry.TestProfileOverride = p => p with { Tier = EnemyTier.Elite, Abilities = Array.Empty<EnemyAbility>() };
                    try { ActorTools.Spawn(VailActorTypeId.Carl, at + new Vector3(-12, 0, 6)); }
                    catch (Exception e) { RLog.Error($"{CoopTestJoin.Tag} server: elite spawn failed: {e.Message}"); }
                }
                else if (_eliteState == 1 && now - _stageAt > 8)
                {
                    _eliteState = 2;
                    EnemyRegistry.TestProfileOverride = null;
                }
                if (!_lateDropPlaced && now - _stageAt > 20)
                {
                    _lateDropPlaced = true;
                    try
                    {
                        var gift = new LootRoller(ModContent.Db).RollItem(ModContent.Db.Items.Values.First(d => LootRoller.IsEquippable(d) && d.Rarity == 1), 10, new ChampionsOfTheSons.Core.Rng(7));
                        var lateDrop = LootFeature.SpawnAt(gift, at + new Vector3(0, 0, 40), DropSource.Overflow);
                        RLog.Msg($"{CoopTestJoin.Tag} server: late-join drop {lateDrop.Id}, {lateDrop.Remaining:F0} s left");
                    }
                    catch (Exception e)
                    {
                        RLog.Error($"{CoopTestJoin.Tag} server: late-join drop failed: {e.Message}");
                    }
                }
                if (now - _resent > 3)
                {
                    _resent = now;
                    CoopSession.SendToPeers(new PlayerInfo(TestPeer, "Test Peer", 12, 1.5f, CoopSession.ToVec(at + new Vector3(3, 0, 3))));
                    // A teammate's War Cry around the client (COS-23).
                    CoopSession.SendToPeers(new AuraPulse(TestPeer, "spell.war_cry", CoopSession.ToVec(at), 20, 5, 1.3f, 1.2f));
                }
                if (now - _stageAt < 110) break;
                RLog.Msg($"{CoopTestJoin.Tag} server: ability casts sent {Abilities.AbilityNet.Sent}");
                var dead = EnemyRegistry.Get(_enemy);
                if (dead != null) EnemyPatches.DealForced(dead, new OutgoingHit(1e12, DamageKind.Pure, false, 0), creditPlayer: false);
                Next(now);
                break;
        }
    }

    private static void Next(float now)
    {
        _stage++;
        _stageAt = now;
    }

    // ================================================================= client half

    private static float _wait = -1;
    private static bool _rooted;
    private static int _castState, _joinState;
    private static bool _lateDropPlaced;
    private static int _eliteState;
    private static Dictionary<int, double> _dropsBefore = new();
    private static int _shapesBefore;

    /// <summary>Called by <see cref="CoopSelfTest"/> before its last (version check) step.</summary>
    internal static void RegisterClientSteps()
    {
        CoopTest.Add("tier fx: the host's elite carries its effect on the client (COS-146)", () =>
        {
            if (_wait < 0) _wait = Time.realtimeSinceStartup;
            bool on = Visuals.TierFxFeature.All.Values.Any(a => a.Tier == EnemyTier.Elite);
            if (!on && Time.realtimeSinceStartup - _wait < 15) return false;
            CoopTest.Check("tier fx: elite effect attached on the client", on || !Visuals.FxKit.Available,
                $"{Visuals.TierFxFeature.All.Count} attached, fx available {Visuals.FxKit.Available}");
            _wait = -1;
            return true;
        }, 20);

        CoopTest.Add("team spell: a teammate's War Cry buffs the client (COS-23)", () =>
        {
            if (_wait < 0) _wait = Time.realtimeSinceStartup;
            bool on = Spells.SpellWorld.Buffs.Has("team.war_cry");
            if (!on && Time.realtimeSinceStartup - _wait < 15) return false;
            CoopTest.Check("team spell: War Cry from a teammate applied", on, $"{CoopExtras.TeamSpellsApplied} team pulse(s) applied");
            _wait = -1;
            return true;
        }, 20);

        CoopTest.Add("abilities: a host enemy's casts play on the client", () =>
        {
            if (_wait < 0)
            {
                _wait = Time.realtimeSinceStartup;
                Admin.AdminState.Apply(new AdminGrant(AdminAction.God, 1, "")); // the copies may hurt: stay alive for the rest
            }
            if (PlayerEffects.Stunned) _rooted = true;
            var started = Abilities.AbilityReplicas.Started;
            bool meteor = started.GetValueOrDefault(EnemyAbility.Meteor) > 0, chains = started.GetValueOrDefault(EnemyAbility.Chains) > 0;
            // The headless client runs slow frames, so game time (casts) lags the real clock: also wait for a copy to land a hit.
            bool hit = Abilities.AbilityDamage.PlayerHits > 0;
            if (!(meteor && chains && _rooted && hit) && Time.realtimeSinceStartup - _wait < 55) return false;
            CoopTest.Check("abilities: Meteor (projectile) plays on the client", meteor, $"{started.GetValueOrDefault(EnemyAbility.Meteor)} cast(s)");
            CoopTest.Check("abilities: Chains roots the client", chains && _rooted, $"{started.GetValueOrDefault(EnemyAbility.Chains)} cast(s), rooted {_rooted}");
            return true;
        }, 60);

        CoopTest.Add("abilities: the aura copy hurts the client", () =>
        {
            // Enemy swings land only when the AI decides to attack: reported when one did, not required.
            if (PlayerBridge.LastClientMeleeScaled is { } m)
                CoopTest.Check("melee: an enemy swing on the client is scaled by the host profile", m.Scaled >= m.Raw, $"{m.Raw:F1} -> {m.Scaled:F1}");
            CoopTest.Check("abilities: Blizzard aura runs on the client and hits its player",
                Abilities.AbilityReplicas.AurasRun > 0 && Abilities.AbilityDamage.PlayerHits > 0,
                $"auras {Abilities.AbilityReplicas.AurasRun}, ability hits on me {Abilities.AbilityDamage.PlayerHits}");
            return true;
        });

        CoopTest.Add("enemy scale shows on the client", () =>
        {
            var sync = EnemySyncFeature.Received.Values.FirstOrDefault(s => s.Scale > 1.5f);
            var actor = sync == null ? null : EnemySyncFeature.ActorOf(sync.EnemyNetId);
            float scale = actor == null ? 0 : actor.GetScaleMult();
            CoopTest.Check("enemy scale shows on the client", sync != null && Mathf.Abs(scale - TestScale) < 0.05f, $"synced {sync?.Scale:F2}, actor {scale:F2}");
            return true;
        });

        CoopTest.Add("damage numbers: the host's own hit shows on the client", () =>
        {
            CoopTest.Check("damage numbers: the host's own hit shows on the client", CoopFx.DamageShownCount >= 2, $"{CoopFx.DamageShownCount} shown");
            return true;
        });

        CoopTest.Add("party: a teammate's level and line arrive", () =>
        {
            var peer = CoopParty.Others.FirstOrDefault(m => m.Id == TestPeer);
            var line = CoopParty.Lines.FirstOrDefault(l => l.PlayerId == TestPeer && l.Kind == AnnounceKind.LevelUp);
            CoopTest.Check("party: teammate's level known", peer?.Level == 12, $"{peer?.Name} level {peer?.Level}");
            CoopTest.Check("party: level-up line arrived", line != null && line.Level == 13, Hud.PartyHud.Format(line ?? new Announce(AnnounceKind.LevelUp, 0, "", 0)));
            return true;
        });

        CoopTest.Add("late join: enemy profiles arrive again after a rejoin", () =>
        {
            if (_joinState == 0)
            {
                EnemySyncFeature.ForgetReceived();
                _dropsBefore = LootFeature.Ground.All.ToDictionary(d => d.Id, d => d.Remaining);
                foreach (var id in _dropsBefore.Keys.ToList()) LootFeature.Ground.Remove(id);
                CoopSession.SendToHost(new Hello(MessageCodec.ProtocolVersion, CoopFeature.ModVersion));
                _wait = Time.realtimeSinceStartup;
                _joinState = 1;
                return false;
            }
            if (EnemySyncFeature.Received.Count == 0) return false;
            if (LootFeature.Ground.All.Count() < _dropsBefore.Count && Time.realtimeSinceStartup - _wait < 10) return false;
            CoopTest.Check("late join: the host resends existing enemies' profiles", true, $"{EnemySyncFeature.Received.Count} profile(s) after the rejoin");
            double elapsed = Time.realtimeSinceStartup - _wait;
            var back = LootFeature.Ground.All.ToList();
            CoopTest.Check("late join: drops on the ground arrive again (COS-14)", _dropsBefore.Count > 0 && _dropsBefore.Keys.All(id => back.Any(d => d.Id == id)),
                $"{_dropsBefore.Count} before, {back.Count} after");
            bool lifetimesKept = back.Where(d => _dropsBefore.ContainsKey(d.Id)).All(d => System.Math.Abs(d.Remaining - (_dropsBefore[d.Id] - elapsed)) < 3);
            CoopTest.Check("late join: a resent drop keeps the host's remaining lifetime (COS-146)", _dropsBefore.Count > 0 && lifetimesKept,
                string.Join(", ", back.Select(d => $"{d.Id}: {d.Remaining:F0} s (was {(_dropsBefore.TryGetValue(d.Id, out var r) ? r : -1):F0})")));
            return true;
        }, 20);

        CoopTest.Add("spells: casts carry their visuals and a teammate's copy is drawn", () =>
        {
            var pc = PlayerState.Character;
            switch (_castState)
            {
                case 0:
                    _shapesBefore = Spells.SpellNetTest.RemoteShapesDrawn;
                    Cast(0, "spell.healing_dome");
                    CoopTest.Check("spells: Healing Dome cast carries its visuals", Spells.SpellNetTest.LastShapes("spell.healing_dome") > 0, $"{Spells.SpellNetTest.LastShapes("spell.healing_dome")} shape(s)");
                    Cast(1, "spell.portal");
                    CoopTest.Check("spells: Portal cast carries both gates", Spells.SpellNetTest.LastPortals() == 2, $"{Spells.SpellNetTest.LastPortals()} gate(s)");
                    Cast(2, "spell.black_flame");
                    CoopTest.Check("spells: Black Flame switches on", pc.Spells.IsActive(2));
                    _wait = Time.realtimeSinceStartup;
                    _castState = 1;
                    return false;
                case 1:
                    if (Time.realtimeSinceStartup - _wait < 1.5f) return false;
                    Spells.SpellNetTest.CastSlot(2); // switch the flame off
                    CoopTest.Check("spells: Black Flame switches off", !pc.Spells.IsActive(2));
                    _wait = Time.realtimeSinceStartup;
                    _castState = 2;
                    return false;
                default:
                    bool drawn = Spells.SpellNetTest.RemoteShapesDrawn - _shapesBefore >= 4;
                    bool portal = Spells.SpellNetTest.SharedPortals > 0;
                    bool off = Spells.SpellNetTest.RemoteShapesStopped > 0;
                    if (!(drawn && portal && off) && Time.realtimeSinceStartup - _wait < 10) return false;
                    CoopTest.Check("spells: a teammate's casts are drawn from their visuals", drawn, $"{Spells.SpellNetTest.RemoteShapesDrawn - _shapesBefore} shape(s)");
                    CoopTest.Check("spells: a teammate's Portal pair is usable here", portal, $"{Spells.SpellNetTest.SharedPortals}");
                    CoopTest.Check("spells: a teammate's Black Flame goes off", off, $"{Spells.SpellNetTest.RemoteShapesStopped} stopped");
                    return true;
            }
        }, 30);
    }

    private static void Cast(int slot, string id) => Spells.SpellNetTest.Cast(slot, id);
}
