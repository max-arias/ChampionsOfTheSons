using System.Runtime.CompilerServices;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using RedLoader;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Loot;

/// <summary>
/// Loot drops and pickup (COS-9, COS-31). Kills roll drops with <see cref="LootRoller"/>; <see cref="Ground"/> holds the
/// authoritative drop list (a network layer drives it later, COS-14); <see cref="LootVisuals"/> renders it.
/// Pickup: walk within <see cref="PickupRadius"/> m of a drop and it jumps into the bag. A drop the player threw away
/// stays put until they have walked <see cref="RearmDistance"/> m away, so it is not grabbed again at once.
/// A full bag leaves the drop on the ground with a message.
/// </summary>
public sealed class LootFeature : IModFeature
{
    public const float PickupRadius = 1.5f;
    public const float RearmDistance = 3f;
    public const float ThrowDistance = 2.5f;

    public static GroundLoot Ground { get; } = new();

    private static readonly Rng Rng = Rng.FromTime();
    private static LootRoller? _roller;
    private static readonly ConditionalWeakTable<EnemyRuntime, object> LastPos = new();
    internal static readonly HashSet<int> NoAutoPickup = new();
    private static readonly Dictionary<int, float> FullBagMessageAt = new();

    private static LootRoller Roller => _roller ??= new LootRoller(ModContent.Db);

    public void Init()
    {
        Ground.Added += LootVisuals.Create;
        Ground.Removed += (d, _) =>
        {
            LootVisuals.Destroy(d.Id);
            NoAutoPickup.Remove(d.Id);
            FullBagMessageAt.Remove(d.Id);
        };
        EnemyPatches.EnemyHit += (actor, runtime, _, _) =>
        {
            try { LastPos.AddOrUpdate(runtime, actor.transform.position); } catch { /* actor gone */ }
        };
        EnemyPatches.EnemyKilled += OnKilled;
        PlayerState.Character.LeveledUp += OnLeveledUp;
    }

    // ---------------------------------------------------------------- level-up rewards (COS-43)

    private static void OnLeveledUp(LevelUpResult r)
    {
        try { foreach (var id in r.RewardItemIds) GiveReward(id); }
        catch (Exception e) { RLog.Error($"Champions of the Sons: level-up reward failed: {e}"); }
    }

    /// <summary>Creates a reward item and puts it in the bag; what does not fit lands at the player's feet.</summary>
    public static void GiveReward(string baseId)
    {
        if (!ModContent.Db.Items.TryGetValue(baseId, out var def)) return;
        GiveOrDrop(Roller.RollItem(def, PlayerState.Character.Progress.Level, Rng), def.StackSize);
    }

    /// <summary>Bag first; a full bag drops the rest next to the player (not auto-picked until they step away) with a message.</summary>
    public static void GiveOrDrop(ItemInstance item, int stackSize)
    {
        if (PlayerState.Character.Inventory.AddToBag(item, stackSize, out var rest) || rest == null) return;
        if (LocalPlayer.IsInWorld)
        {
            if (Net.CoopLoot.RequestDrop(rest, PlayerPosition() + Vector3.forward * 1.2f, DropSource.Overflow)) { }
            else
            {
                var drop = SpawnScattered(rest, PlayerPosition(), 0.8f, 1.5f, DropSource.Overflow);
                NoAutoPickup.Add(drop.Id);
            }
        }
        else Ground.Add(rest, 0, 0, 0, stackSize, DropSource.Overflow);
        Message(UI.UiText.T("ui.loot.bag_full", "Your bag is full."));
    }

    public void OnGameStart() => ClearAll();

    public void RegisterSelfTests() => LootSelfTest.Register();

    public void Update(float dt, bool inWorld)
    {
        // Drops age even without a local player in the world: a dedicated server has none (its drops never expired before,
        // found 2026-11-21 by the co-op test), and a host holding the guide book is not "in world" either.
        Ground.Tick(dt);
        if (!inWorld) return;
        LootVisuals.Animate(Ground);
        TryPickups();
    }

    public static void ClearAll()
    {
        foreach (var id in Ground.All.Select(d => d.Id).ToList()) Ground.Remove(id);
    }

    // ---------------------------------------------------------------- drops

    private static void OnKilled(EnemyRuntime runtime, double xp)
    {
        try
        {
            var pos = LastPos.TryGetValue(runtime, out var o) ? (Vector3)o : PlayerPosition() + LocalPlayer.Transform.forward * 4;
            var drops = RollForKill(runtime.Profile, WorldState.Rules);
            if (SelfTest.Enabled) SelfTest.Log($"loot: kill of level {runtime.Profile.Level} {runtime.Profile.Tier} rolled {drops.Count} drops");
            foreach (var item in drops) SpawnScattered(item, pos);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: loot drop failed: {e}");
        }
    }

    /// <summary>Rolls the items one kill drops (empty when the kill drops nothing). Item level = player level.</summary>
    public static List<ItemInstance> RollForKill(EnemyProfile enemy, WorldRules rules)
    {
        var result = new List<ItemInstance>();
        // Co-op: the sum of every player's magic find (COS-81); alone: the player's own.
        double mf = Net.CoopParty.TeamMagicFind();
        int level = PlayerState.Character.Progress.Level;
        if (Net.CoopSession.IsHost && Net.CoopFeature.Verified.Count > 0) level = Net.CoopSession.LootLevel(level); // co-op: the host's loot-level policy over all players
        if (LootRoller.DropsXpHeart(enemy.Tier, Rng) && ModContent.Db.Items.TryGetValue(LootRoller.XpHeart, out var heart))
            result.Add(Roller.RollItem(heart, level, Rng));
        if (!LootRoller.ShouldDrop(enemy, rules, mf, Rng)) return result;
        int count = LootRoller.DropCount(enemy, rules, Rng);
        for (int i = 0; i < count; i++)
        {
            var item = Roller.RollDrop(enemy.Bounty, level, mf, Difficulties.ForLevel(enemy.Level).Index, Rng, rules.MinLootRarity);
            if (item != null) result.Add(item);
        }
        return result;
    }

    public static GroundDrop SpawnScattered(ItemInstance item, Vector3 around, float minRadius = 0.6f, float maxRadius = 1.8f, DropSource source = DropSource.Enemy)
    {
        float angle = (float)(Rng.NextDouble() * Math.PI * 2);
        float r = minRadius + (float)Rng.NextDouble() * (maxRadius - minRadius);
        return SpawnAt(item, around + new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r), source);
    }

    public static GroundDrop SpawnAt(ItemInstance item, Vector3 pos, DropSource source = DropSource.Enemy)
    {
        pos = SnapToGround(pos);
        int stack = ModContent.Db.Items.TryGetValue(item.BaseId, out var def) ? def.StackSize : 1;
        return Ground.Add(item, pos.x, pos.y, pos.z, stack, source);
    }

    internal static Vector3 SnapToGround(Vector3 pos)
    {
        // The lowest surface just around pos: the first hit from above can be a player or corpse collider.
        var hits = Physics.RaycastAll(pos + Vector3.up * 2f, Vector3.down, 6f);
        bool found = false;
        float y = 0;
        foreach (var h in hits)
        {
            if (h.point.y > pos.y + 1f) continue;
            if (!found || h.point.y < y) { y = h.point.y; found = true; }
        }
        return found ? new Vector3(pos.x, y + 0.05f, pos.z) : pos;
    }

    /// <summary>Inventory "drop to world": the item lands ahead of the player and is not auto-picked until they step away.</summary>
    /// <param name="amount">How many of a stack to drop (COS-96: Drop 1); the whole stack by default.</param>
    public static GroundDrop? DropFromInventory(string slot, int amount = int.MaxValue)
    {
        var inv = PlayerState.Character.Inventory;
        // Not IsInWorld: holding the guide book clears it, and the book inventory drops from there (COS-59).
        if (inv.Get(slot) == null || LocalPlayer.Transform == null) return null;
        var item = inv.Take(slot, amount)!;
        var t = LocalPlayer.Transform;
        // Co-op client: the host makes the drop, so everyone sees it (COS-14); it arrives as a mirror.
        if (Net.CoopLoot.RequestDrop(item, t.position + t.forward * ThrowDistance, DropSource.Thrown)) return null;
        var drop = SpawnAt(item, t.position + t.forward * ThrowDistance, DropSource.Thrown);
        NoAutoPickup.Add(drop.Id);
        return drop;
    }

    /// <summary>Inventory "destroy": the item is gone for good.</summary>
    public static bool Destroy(string slot) => PlayerState.Character.Inventory.Remove(slot) != null;

    // ---------------------------------------------------------------- pickup

    private static Vector3 PlayerPosition() => LocalPlayer.Transform.position;

    private static void TryPickups()
    {
        if (Ground.Count == 0) return;
        var p = PlayerPosition();
        foreach (var d in Ground.All.ToList())
        {
            // The player transform sits well above the feet, so only the horizontal distance counts (plus a loose height limit).
            float dist = Mathf.Abs(p.y - d.Y) > 5f ? float.MaxValue : Mathf.Sqrt((p.x - d.X) * (p.x - d.X) + (p.z - d.Z) * (p.z - d.Z));
            if (NoAutoPickup.Contains(d.Id))
            {
                if (dist > RearmDistance) NoAutoPickup.Remove(d.Id);
                continue;
            }
            if (dist <= PickupRadius) Pickup(d.Id);
        }
    }

    public static PickupResult Pickup(int id)
    {
        var item = Ground.Get(id)?.Item;
        var result = Ground.TryPickup(id, PlayerState.Character.Inventory);
        if (result == PickupResult.Ok && item != null)
        {
            if (SelfTest.Enabled) SelfTest.Log($"loot: picked up {item.BaseId}");
            Message(string.Format(UI.UiText.T("ui.followups.picked_up", "Picked up {0}"), UI.UiText.ItemName(item)));
        }
        else if (result == PickupResult.BagFull && item != null)
        {
            // Throttled: the player may stand on the drop for a while.
            float now = Time.realtimeSinceStartup;
            if (!FullBagMessageAt.TryGetValue(id, out var last) || now - last > 4f)
            {
                FullBagMessageAt[id] = now;
                Message(UI.UiText.T("ui.loot.bag_full", "Your bag is full."));
            }
        }
        return result;
    }

    public static void Message(string text)
    {
        LastMessage = text;
        try { SonsTools.ShowMessage(text); } catch (Exception e) { RLog.Warning($"Champions of the Sons: message failed: {e.Message}"); }
    }

    /// <summary>Last message shown, for self-tests.</summary>
    public static string? LastMessage { get; private set; }
}
