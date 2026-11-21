using System.Text.RegularExpressions;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.Net;
using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.World;

/// <summary>
/// XP and loot from the world (COS-75): animal kills pay kill XP on the streak path, shared like enemy kills (sharks: everyone
/// gets the full amount) and valuable ones drop items; a felled tree gives every player 30 flat XP; cutting a bush or sapling
/// gives its cutter 3-5. The Harvester perk doubles the sticks a cut bush drops. Numbers: <see cref="WorldXp"/>, animals.json.
/// </summary>
[HarmonyPatch]
public sealed class WorldXpFeature : IModFeature
{
    /// <summary>The cutter stands this close to a tree or bush they brought down.</summary>
    private const float CutterRange = 10f, BushRange = 6f;

    private static readonly Rng Rng = Rng.FromTime();
    private static readonly List<(Vector3 At, float Time)> TreesPaid = new();
    private static bool _spawningAgain;

    /// <summary>Self-test: the last world XP paid and where it came from.</summary>
    public static string LastSource { get; private set; } = "";
    public static double LastXp { get; private set; }

    public void Init() => CotsNetEvent.Received += OnNet;

    // ---------------------------------------------------------------- animals

    /// <summary>From the enemy death hook, for actors that are not tracked enemies. Authority only.</summary>
    public static void OnActorDied(WorldSimActor dead, bool killedByPlayer)
    {
        if (!ModContent.Db.Animals.TryGetValue(dead.TypeId.ToString(), out var animal)) return;
        Vector3 at = dead.Position();
        // The game's flag misses some player kills: a hit from the local player within the last 10 s near the body counts too.
        float now = Time.realtimeSinceStartup;
        PlayerHits.RemoveAll(h => now - h.Time > 10f);
        bool hitByUs = PlayerHits.Any(h => Vector3.Distance(h.At, at) < 6f);
        if (!killedByPlayer && !hitByUs) return;
        PlayerHits.Clear();
        double xp = WorldXp.AnimalXp(animal, Rng);
        double own = animal.Everyone ? CoopHits.GiveEveryone(xp) : CoopHits.DistributeXpAt(xp, at);
        if (own > 0 || !(CoopSession.IsHost && CoopFeature.Verified.Count > 0)) PlayerState.Character.OnKill(own);
        LastSource = "animal." + animal.Actor;
        LastXp = own;
        foreach (var worth in WorldXp.DropWorths(animal, xp, Rng))
            if (LootFeature.RollAtWorth(worth) is { } item) LootFeature.SpawnScattered(item, at, source: DropSource.Enemy);
        if (SelfTest.Enabled) SelfTest.Log($"world: {animal.Actor} killed, {xp} XP");
    }

    private static readonly List<(Vector3 At, float Time)> PlayerHits = new();

    /// <summary>The local player hit an actor that is not a tracked enemy (an animal).</summary>
    public static void MarkPlayerHit(VailActor actor)
    {
        if (actor != null) PlayerHits.Add((actor.transform.position, Time.realtimeSinceStartup));
    }

    // ---------------------------------------------------------------- trees

    [HarmonyPatch(typeof(Sons.Gameplay.TreeCutting.TreeCutManager), nameof(Sons.Gameplay.TreeCutting.TreeCutManager.FellTree))]
    [HarmonyPostfix]
    private static void AfterFellTree(Sons.Gameplay.TreeCutting.TreeCutManager __instance)
    {
        try
        {
            if (!LocalPlayer.IsInWorld || __instance == null) return;
            var at = __instance.transform.position;
            if (Horizontal(LocalPlayer.Transform.position, at) > CutterRange || AlreadyPaid(at)) return;
            PayTree(at);
            if (CoopSession.Active) CoopSession.Broadcast(new FlatXpGranted((long)WorldXp.TreeXp, CoopSession.ToVec(at)));
        }
        catch (Exception e)
        {
            RLog.Warning($"[COTS] tree XP failed: {e.Message}");
        }
    }

    private static void PayTree(Vector3 at)
    {
        TreesPaid.Add((at, Time.realtimeSinceStartup));
        PlayerState.Character.AddXp(WorldXp.TreeXp);
        LastSource = "tree";
        LastXp = WorldXp.TreeXp;
    }

    /// <summary>Another player felled a tree: every player gets the XP, once (our own game may have seen the same fall).</summary>
    private static bool AlreadyPaid(Vector3 at)
    {
        float now = Time.realtimeSinceStartup;
        TreesPaid.RemoveAll(t => now - t.Time > 30f);
        return TreesPaid.Any(t => Vector3.Distance(t.At, at) < 3f);
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is not FlatXpGranted flat) return;
        try
        {
            if (CoopSession.IsHost)
            {
                if (!CoopFeature.Verified.Contains(from)) return;
                CoopSession.SendToPeers(flat, except: from);
            }
            var at = CoopSession.ToUnity(flat.Source);
            if (LocalPlayer.IsInWorld && !AlreadyPaid(at)) PayTree(at);
        }
        catch (Exception e)
        {
            RLog.Error($"[COTS] flat XP failed: {e}");
        }
    }

    // ---------------------------------------------------------------- bushes

    private static readonly Regex BushName = new("(?i)bush|shrub|sapling|fern|plant|leaf|leaves|brush|stick");
    private static readonly HashSet<string> NamesLogged = new();

    /// <summary>Whether a breakable is a bush or sapling (by its name: the game has no bush type of its own).</summary>
    public static bool IsBush(GameObject go)
    {
        string name = go.name;
        if (SelfTest.Enabled && NamesLogged.Add(name)) SelfTest.Log($"world: breakable '{name}' bush={BushName.IsMatch(name)}");
        return BushName.IsMatch(name);
    }

    [HarmonyPatch(typeof(Sons.Gameplay.BreakableObject), nameof(Sons.Gameplay.BreakableObject.OnBreak))]
    [HarmonyPostfix]
    private static void AfterBreak(Sons.Gameplay.BreakableObject __instance)
    {
        try
        {
            if (!LocalPlayer.IsInWorld || __instance == null || !IsBush(__instance.gameObject)) return;
            if (Horizontal(LocalPlayer.Transform.position, __instance.transform.position) > BushRange) return;
            double xp = WorldXp.BushXp(Rng);
            PlayerState.Character.AddXp(xp);
            LastSource = "bush";
            LastXp = xp;
        }
        catch (Exception e)
        {
            RLog.Warning($"[COTS] bush XP failed: {e.Message}");
        }
    }

    /// <summary>Harvester: a cut bush drops its sticks twice (the game's own drop list, spawned again).</summary>
    [HarmonyPatch(typeof(Sons.Gameplay.BreakableObject), nameof(Sons.Gameplay.BreakableObject.SpawnDrops))]
    [HarmonyPostfix]
    private static void AfterSpawnDrops(Sons.Gameplay.BreakableObject __instance, Vector3 hitVelocity)
    {
        if (_spawningAgain || __instance == null) return;
        try
        {
            if (PlayerState.Character.Perks.TimesBought(Harvester) == 0 || !IsBush(__instance.gameObject)) return;
            if (!LocalPlayer.IsInWorld || Horizontal(LocalPlayer.Transform.position, __instance.transform.position) > BushRange) return;
            _spawningAgain = true;
            __instance.SpawnDrops(hitVelocity);
            HarvesterDoubled++;
        }
        catch (Exception e)
        {
            RLog.Warning($"[COTS] harvester failed: {e.Message}");
        }
        finally
        {
            _spawningAgain = false;
        }
    }

    public const string Harvester = "perk.harvester";

    /// <summary>Self-test: cut bushes whose drops Harvester doubled.</summary>
    public static int HarvesterDoubled { get; private set; }

    private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // ---------------------------------------------------------------- self-test

    public void RegisterSelfTests()
    {
        VailActor? rabbit = null;
        double xpBefore = 0;
        int levelBefore = 0;
        SelfTest.Add(0.5f, "world: unpause", TestKit.Unpause);
        SelfTest.Add(1f, "world: spawn an animal", () =>
        {
            PlayerState.Character.Massacre.Reset();
            rabbit = TestKit.Spawn(VailActorTypeId.Deer, 5);
            SelfTest.Check("world: an animal spawned", rabbit != null);
            xpBefore = PlayerState.Character.Progress.Xp;
            levelBefore = PlayerState.Character.Progress.Level;
        });
        SelfTest.Add(2f, "world: kill it", () => { if (rabbit != null) TestKit.Kill(rabbit); });
        SelfTest.Add(4f, "world: animal XP", () =>
        {
            var pc = PlayerState.Character;
            bool rose = pc.Progress.Xp > xpBefore || pc.Progress.Level > levelBefore || pc.Massacre.Kills > 0;
            SelfTest.Check("world: killing an animal gives XP", rose && LastSource.StartsWith("animal."), $"{LastSource} {LastXp}, xp {xpBefore:0} -> {pc.Progress.Xp:0}");
        });
        SelfTest.Add(5f, "world: fell a tree", () =>
        {
            var trees = UnityEngine.Object.FindObjectsOfType<Sons.Gameplay.TreeCutting.TreeCutManager>();
            var me = LocalPlayer.Transform.position;
            Sons.Gameplay.TreeCutting.TreeCutManager? near = null;
            float best = 80f;
            foreach (var t in trees)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(t.transform.position, me);
                if (d < best) { best = d; near = t; }
            }
            _treeFound = near != null;
            if (near == null) { SelfTest.Log($"world: no cuttable tree within 80 m ({trees.Length} found): tree XP not checked here"); return; }
            SpellsTeleport(near.transform.position + (me - near.transform.position).normalized * 3f);
            xpBefore = PlayerState.Character.Progress.Xp;
            levelBefore = PlayerState.Character.Progress.Level;
            near.FellTree(false);
        });
        SelfTest.Add(7f, "world: tree XP", () =>
        {
            if (!_treeFound) return;
            if (LastSource != "tree") { SelfTest.Check("world: felling a tree gives flat XP", false, $"last {LastSource}"); return; }
            var pc = PlayerState.Character;
            SelfTest.Check("world: felling a tree gives flat XP", pc.Progress.Xp > xpBefore || pc.Progress.Level > levelBefore, $"xp {xpBefore:0} -> {pc.Progress.Xp:0}");
        });
    }

    private static bool _treeFound;

    private static void SpellsTeleport(Vector3 at) => Spells.SpellCaster.TeleportPlayer(TestKit.GroundAt(at) ?? at);
}
