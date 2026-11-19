using Bolt;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
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
/// The two halves of the server+client co-op test (tools/run-coop-test.ps1, docs/coop-testing.md).
/// Server half (launch option <c>--cots-coop-test</c> on the dedicated server): once a verified client has reported its position it
/// spawns an enemy and a drop next to it, sends a ping, an aura, a spell and a damage number to it, and logs what the host applied.
/// Client half (<see cref="CoopTest"/> steps): checks each of those arrived and sends its own hits, pickup and ping back.
/// </summary>
public sealed class CoopSelfTest : IModFeature
{
    public static bool ServerDriver { get; } = Environment.GetCommandLineArgs().Contains("--cots-coop-test");

    private static string? ExpectedDifficulty
    {
        get
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "--cots-expect-difficulty");
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }

    // server state
    private static int _stage;
    private static float _stageAt;
    private static VailActor? _enemy;
    private static GroundDrop? _drop;
    private static int _auraUntilStage;

    public void Init()
    {
        if (CoopTest.Enabled) RegisterClientSteps();
    }

    public void OnGameStart()
    {
        _stage = 0;
        _enemy = null;
        _drop = null;
    }

    // ================================================================= server half

    private static float _diagAt, _peerInWorldAt = -1;
    private static bool _saved;

    /// <summary>Saves the server's world once the test client has been in it for a while, so the next run continues
    /// (SaveMode Continue) with the client's player already past the arrival and intro instead of a fresh start.</summary>
    private static void SaveOnce()
    {
        if (_saved) return;
        if (_peerInWorldAt < 0) _peerInWorldAt = Time.realtimeSinceStartup;
        if (Time.realtimeSinceStartup - _peerInWorldAt < 20) return;
        _saved = true;
        try
        {
            Sons.Gameplay.DedicatedServerAutosave.QueueAutosave();
            RLog.Msg($"{CoopTestJoin.Tag} server: queued a save with the client in the world");
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} server: save failed: {e.Message}");
        }
    }

    public void Update(float dt, bool inWorld)
    {
        if (!ServerDriver) return;
        var conn = CoopSession.IsHost && CoopFeature.Verified.Count > 0 ? CoopSession.Peers.FirstOrDefault(c => CoopSession.TryPosition(c, out _)) : null;
        if (conn == null)
        {
            if (Time.realtimeSinceStartup > _diagAt)
            {
                _diagAt = Time.realtimeSinceStartup + 10;
                RLog.Msg($"{CoopTestJoin.Tag} server: waiting: host={CoopSession.IsHost} verified={CoopFeature.Verified.Count} peers={CoopSession.Peers.Count()} withPosition={CoopSession.Peers.Count(c => CoopSession.TryPosition(c, out _))} inWorld={inWorld}");
            }
            return;
        }
        CoopSession.TryPosition(conn, out var pos);
        SaveOnce();
        var at = CoopSession.ToUnity(pos);
        float now = Time.realtimeSinceStartup;
        if (_stageAt == 0) _stageAt = now;

        switch (_stage)
        {
            case 0: // an enemy near the client
                if (now - _stageAt < 3) break;
                try
                {
                    _enemy = ActorTools.Spawn(VailActorTypeId.Carl, at + new Vector3(8, 0, 0));
                    RLog.Msg($"{CoopTestJoin.Tag} server: spawned enemy {(_enemy == null ? "FAILED" : "at " + _enemy.transform.position)}");
                }
                catch (Exception e)
                {
                    RLog.Error($"{CoopTestJoin.Tag} server: spawn failed: {e}");
                }
                Next(now);
                break;
            case 1: // report the enemy the host rolled
                if (now - _stageAt < 2) break;
                var rt = EnemyRegistry.Get(_enemy);
                if (rt != null)
                    RLog.Msg($"{CoopTestJoin.Tag} server: enemy net id {EnemySyncFeature.NetIdOf(_enemy)} level {rt.Profile.Level} tier {rt.Profile.Tier} maxHealth {rt.Profile.MaxHealth:F0}");
                else
                    RLog.Error($"{CoopTestJoin.Tag} server: the spawned enemy has no profile");
                Next(now);
                break;
            case 2: // wait for the client to kill it
                if (_enemy != null && !_enemy.IsDead() && now - _stageAt < 60) break;
                RLog.Msg($"{CoopTestJoin.Tag} server: enemy dead={_enemy == null || _enemy.IsDead()}, hit applied={CoopHits.LastApplied != null}");
                Next(now);
                break;
            case 3: // drop an item on top of the client
                if (now - _stageAt < 2) break;
                var item = new LootRoller(ModContent.Db).RollItem(new LootRoller(ModContent.Db).PickBase(4, 20, new Rng(7))!, 20, new Rng(7));
                _drop = LootFeature.SpawnAt(item, at + new Vector3(0.3f, 0, 0));
                RLog.Msg($"{CoopTestJoin.Tag} server: dropped {item.BaseId} as drop {_drop.Id} at the client");
                Next(now);
                break;
            case 4: // wait for the pickup
                if (LootFeature.Ground.Get(_drop!.Id) != null && now - _stageAt < 30) break;
                RLog.Msg($"{CoopTestJoin.Tag} server: drop {_drop.Id} left the ground: {(LootFeature.Ground.Get(_drop.Id) == null ? "claimed" : "STILL THERE")}");
                Next(now);
                break;
            case 5: // ping, spell, damage number, aura to the client
                CoopExtras.SendPing(PingKind.Location, at + new Vector3(5, 0, 5), 0);
                CoopExtras.SendSpell("spell.blink", at, Vector3.forward * 5);
                CoopSession.SendToPeers(new DamageShown(99, 1, CoopSession.ToVec(at + Vector3.up * 2), 1234, HitFlags.Crit));
                _auraUntilStage = 1;
                Next(now);
                break;
            case 6: // aura pulses for 6 s, then stop
                if (now - _stageAt < 6) { if ((int)(now * 2) % 2 == 0) CoopSession.SendToPeers(new AuraPulse(99, "buff.coop_test", CoopSession.ToVec(at), 20, 2f)); break; }
                RLog.Msg($"{CoopTestJoin.Tag} server: aura pulses stopped");
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

    private static ulong _enemyId;
    private static float _wait;

    private static float Health(ulong id) => EnemySyncFeature.Received.TryGetValue(id, out var s) ? s.Health : float.NaN;

    private static void RegisterClientSteps()
    {
        CoopTest.Add("difficulty: client got the server's configured difficulty", () =>
        {
            var want = ExpectedDifficulty;
            if (want == null) return true;
            CoopTest.Check("difficulty: client got the server's configured difficulty", WorldState.Rules.Difficulty.Id == want,
                $"want {want}, have {WorldState.Rules.Difficulty.Id}");
            return true;
        });

        CoopTest.Add("enemy sync: client sees the host's profile", () =>
        {
            if (EnemySyncFeature.Received.Count == 0) return false;
            var (id, sync) = EnemySyncFeature.Received.First();
            _enemyId = id;
            var actor = EnemySyncFeature.ActorOf(id);
            var mirrored = EnemyRegistry.Get(actor);
            CoopTest.Check("enemy sync: client sees the host's profile", mirrored != null && mirrored.Profile.Level == sync.Level && sync.Level >= 1,
                $"host level {sync.Level} tier {sync.Tier} hp {sync.Health:F0}/{sync.MaxHealth:F0}; client nameplate level {mirrored?.Profile.Level}");
            return mirrored != null;
        }, 40);

        CoopTest.Add("client hit: gear changes the damage", () =>
        {
            switch (_hitState)
            {
                case 0:
                    _h0 = Health(_enemyId);
                    PlayerState.Character.Stats.Clear();
                    CoopHits.SendHit(_enemyId, CoopHits.LocalHit(ImpactType.Melee, 200, false) with { Crit = false }, HitFlags.Melee);
                    _wait = Time.realtimeSinceStartup;
                    _hitState = 1;
                    return false;
                case 1:
                    if (Time.realtimeSinceStartup - _wait < 1.5f) return false;
                    _h1 = Health(_enemyId);
                    PlayerState.Character.Stats.SetSource("coop.test_gear", new[] { new StatModifier(StatIds.MeleeFlatDmg, EffectOp.Add, 50) });
                    CoopHits.SendHit(_enemyId, CoopHits.LocalHit(ImpactType.Melee, 200, false) with { Crit = false }, HitFlags.Melee);
                    _wait = Time.realtimeSinceStartup;
                    _hitState = 2;
                    return false;
                default:
                    if (Time.realtimeSinceStartup - _wait < 1.5f) return false;
                    float h2 = Health(_enemyId);
                    PlayerState.Character.Stats.RemoveSource("coop.test_gear");
                    double first = _h0 - _h1, boosted = _h1 - h2;
                    CoopTest.Check("client hit: lands on the host-owned enemy", first > 0, $"first hit removed {first:F0} health");
                    CoopTest.Check("client hit: gear changes the damage", boosted > first && first > 0, $"first hit removed {first:F0}, with +50 flat gear {boosted:F0}");
                    return true;
            }
        }, 20);

        CoopTest.Add("kill: client finishes the enemy and gets XP", () =>
        {
            float hp = Health(_enemyId);
            if (hp > 0 && !float.IsNaN(hp))
            {
                if (Time.realtimeSinceStartup - _wait > 0.5f)
                {
                    _wait = Time.realtimeSinceStartup;
                    CoopHits.SendHit(_enemyId, new OutgoingHit(1e9, DamageKind.Pure, false, 0), HitFlags.Melee | HitFlags.Pure);
                }
                return false;
            }
            CoopTest.Check("kill: host reports the enemy dead to the client", hp <= 0, $"health {hp}");
            return true;
        }, 40);

        CoopTest.Add("loot: the drop shows on the client", () =>
        {
            if (CoopLoot.MirroredCount == 0) return false;
            var d = LootFeature.Ground.All.First();
            CoopTest.Check("loot: the drop shows on the client", true, $"drop {d.Id} at {d.X:F1},{d.Y:F1},{d.Z:F1}");
            // Walk onto it: the client asks the host, which grants it once.
            LocalPlayer.Transform.position = new Vector3(d.X, d.Y + 1f, d.Z);
            _wait = Time.realtimeSinceStartup;
            return true;
        }, 60);

        CoopTest.Add("loot: only the host's grant puts it in the bag", () =>
        {
            if (CoopLoot.LastGranted == null && Time.realtimeSinceStartup - _wait < 15) return false;
            CoopTest.Check("loot: pickup granted once", CoopLoot.LastGranted != null && LootFeature.Ground.Count == 0,
                $"granted {CoopLoot.LastGranted?.BaseId}, drops left {LootFeature.Ground.Count}");
            return true;
        }, 20);

        CoopTest.Add("ping: the host's ping shows and expires", () =>
        {
            if (!CoopExtras.Pings.Active.Any())
            {
                if (_pingSeen) { CoopTest.Check("ping: expired", true); return true; }
                return false;
            }
            if (!_pingSeen) { _pingSeen = true; CoopTest.Check("ping: showed up from the host", true); }
            return false;
        }, 40);

        CoopTest.Add("spell: a teammate's cast is shown", () =>
        {
            CoopTest.Check("spell: cast by another player shown", CoopFx.LastSpell?.SpellId == "spell.blink", $"{CoopFx.LastSpell?.SpellId}");
            CoopTest.Check("damage numbers: hit by another player shown", CoopFx.DamageShownCount > 0, $"{CoopFx.DamageShownCount}");
            return true;
        });

        CoopTest.Add("aura: buff arrives and ends", () =>
        {
            bool on = CoopExtras.IsBuffed("buff.coop_test");
            // The host may finish pulsing before this step starts: a pulse applied at any time counts as seen.
            if (on || CoopExtras.PulsesApplied > 0) _auraSeen = true;
            if (!_auraSeen) return false;
            if (on) return false;
            CoopTest.Check("aura: teammate's buff applied then ended after the pulses stopped", true);
            return true;
        }, 30);

        CoopTest.Add("version check: a wrong version is refused", () =>
        {
            if (!_badHelloSent)
            {
                _badHelloSent = true;
                CoopSession.SendToHost(new Hello(MessageCodec.ProtocolVersion + 1, "9.9.9"));
                return false;
            }
            if (CoopFeature.RejectReason == null) return false;
            CoopTest.Check("version check: a wrong version is refused with a clear message", CoopFeature.RejectReason.Contains("version mismatch"), CoopFeature.RejectReason);
            return true;
        }, 15);
    }

    private static bool _pingSeen, _auraSeen, _badHelloSent;
    private static int _hitState;
    private static float _h0, _h1;
}
