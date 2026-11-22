using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod;

/// <summary>Helpers for self-test steps: spawn, hit, kill, give items, screenshots.</summary>
public static class TestKit
{
    private static readonly Rng Rng = new(12345);

    /// <summary>
    /// The test save is an early-access save: the game opens a "Game Loaded" warning over the pause menu and stays
    /// paused, so bindings that need a controllable player do nothing until it is closed (COS-62).
    /// </summary>
    public static void Unpause()
    {
        foreach (var w in UnityEngine.Object.FindObjectsOfType<Sons.Gui.VersionUpgrade.ShowLegacySaveWarning>()) w.gameObject.SetActive(false);
        Sons.Gui.PauseMenu._instance?.Close();
    }

    /// <summary>Spawns an enemy <paramref name="distance"/> m in front of the player, optionally under temporary rules.</summary>
    public static VailActor? Spawn(VailActorTypeId type = VailActorTypeId.Carl, float distance = 6, float sideways = 0, WorldRules? rules = null)
    {
        var t = LocalPlayer.Transform;
        var saved = WorldState.Rules;
        if (rules != null) WorldState.SetRulesForTest(rules);
        try
        {
            return ActorTools.Spawn(type, t.position + t.forward * distance + t.right * sideways);
        }
        finally
        {
            WorldState.SetRulesForTest(saved);
        }
    }

    /// <summary>
    /// Turns (and if needed moves) the player so every spot, given as (forward, sideways) metres, has ground under it and a
    /// clear line from the eye through nothing of the world. Earlier phases leave the player anywhere, so tests that need
    /// open ground call this before <see cref="Spawn"/> (COS-152). Tries 12 headings here, then the same at four places
    /// 25 m away. False (player left where it was) if no heading works.
    /// </summary>
    public static bool FaceOpenGround(IReadOnlyList<(float Forward, float Side)> spots)
    {
        var t = LocalPlayer.Transform;
        if (t == null) return false;
        var start = t.position;
        var startRot = t.rotation;
        var places = new List<Vector3> { start };
        float floor = GroundAt(start)?.y ?? start.y;
        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            if (GroundAt(start + d * 25f) is { } g && Mathf.Abs(g.y - floor) < 6f) places.Add(g);
        foreach (var place in places)
        {
            if (place != start) Spells.SpellCaster.TeleportPlayer(place + Vector3.up * 0.05f);
            Physics.SyncTransforms();
            float here = GroundAt(t.position)?.y ?? t.position.y;
            var eye = (Camera.main != null ? Camera.main.transform.position.y - t.position.y : 1.6f) + t.position.y;
            for (int i = 0; i < 12; i++)
            {
                var rot = Quaternion.Euler(0, startRot.eulerAngles.y + i * 30f, 0);
                if (!spots.All(s => Clear(t.position, eye, here, rot * new Vector3(s.Side, 0, s.Forward)))) continue;
                t.rotation = rot;
                Physics.SyncTransforms();
                SelfTest.Log($"open ground: heading +{i * 30} deg at {t.position}");
                return true;
            }
        }
        Spells.SpellCaster.TeleportPlayer(start);
        t.rotation = startRot;
        Physics.SyncTransforms();
        SelfTest.Log("open ground: no clear heading found");
        return false;
    }

    private static bool Clear(Vector3 player, float eyeY, float floor, Vector3 offset)
    {
        var g = GroundAt(player + offset);
        if (g == null || Mathf.Abs(g.Value.y - floor) > 3f) return false; // a drop, a cliff or no ground
        var eye = new Vector3(player.x, eyeY, player.z);
        var to = g.Value + Vector3.up * 1f;
        var dir = to - eye;
        float dist = dir.magnitude;
        foreach (var hit in Physics.RaycastAll(eye, dir / dist, dist))
        {
            if (hit.transform == null || hit.collider.isTrigger) continue;
            if (LocalPlayer.Transform != null && hit.transform.IsChildOf(LocalPlayer.Transform)) continue;
            if (hit.transform.GetComponentInParent<VailActor>() != null) continue;
            return false;
        }
        return true;
    }

    /// <summary>The world surface under a point (player and enemies ignored), or null.</summary>
    public static Vector3? GroundAt(Vector3 at)
    {
        foreach (var h in Physics.RaycastAll(at + Vector3.up * 6f, Vector3.down, 16f).OrderBy(h => h.distance))
        {
            if (h.transform == null || h.collider.isTrigger) continue;
            if (LocalPlayer.Transform != null && h.transform.IsChildOf(LocalPlayer.Transform)) continue;
            if (h.transform.GetComponentInParent<VailActor>() != null) continue;
            return h.point;
        }
        return null;
    }

    /// <summary>A plain hit from the local player through the game's damage path (our hooks apply).</summary>
    public static void Hit(VailActor actor, float amount, ImpactType type = ImpactType.Melee) =>
        actor.ReceivedDamage(null, type, 0, null, amount, true, false, false, Vector3.forward);

    /// <summary>Kills an enemy as the local player: lethal hits, then the game's death sequence
    /// (calling ReceivedDamage directly skips the caller that normally starts it).</summary>
    public static void Kill(VailActor actor)
    {
        // Hell elites have ~1e10 health, steadfast caps a hit at a few percent, Shielding eats a hit and Undead revives:
        // drop health to 1 before each hit.
        for (int i = 0; i < 6 && actor.GetHealth() > 0; i++)
        {
            EnemyRegistry.HealthOf(actor)?.SetCurrentValue(1);
            Hit(actor, 1e9f);
        }
        if (!actor.IsDead() && actor.GetHealth() <= 0) actor.OnDeath();
    }

    public static EnemyRuntime? Profile(VailActor? actor) => EnemyRegistry.Get(actor);

    /// <summary>
    /// Puts one of the game's items (by item id) into the player's inventory if it is not there yet. Self-tests must not
    /// depend on what the test save happens to own (COS-129: the fresh save owns no weapons).
    /// </summary>
    public static bool EnsureGameItem(int itemId)
    {
        var inv = LocalPlayer.Inventory;
        return inv != null && (inv.AmountOf(itemId, true, false) > 0 || inv.AddItem(itemId, 1, true, false, null));
    }

    /// <summary>
    /// Empties the hands for an equip: the game refuses to equip a weapon while a held-only item (something carried in the
    /// hands without being in the inventory, as earlier test phases can leave behind) is in hand (COS-129).
    /// </summary>
    public static void FreeHands()
    {
        var inv = LocalPlayer.Inventory;
        if (inv != null && inv.HeldOnlyItemController != null) inv.StashHeldItems(false, true);
    }

    /// <summary>Owns and holds one of the game's items: adds it if needed, frees the hands, equips it. False if the game refused.</summary>
    public static bool EquipGameItem(int itemId)
    {
        var inv = LocalPlayer.Inventory;
        if (inv == null || !EnsureGameItem(itemId)) return false;
        FreeHands();
        if (inv.TryEquip(itemId, false, true, false)) return true;
        inv.StashHeldItems(false, true); // whatever else is in hand
        return inv.TryEquip(itemId, false, true, false);
    }

    /// <summary>Rolls an item into the bag. Null base id = random item of the given rarity.</summary>
    public static ItemInstance Give(string? baseId = null, int rarity = 2, int level = 20)
    {
        var loot = new LootRoller(ModContent.Db);
        var def = baseId != null ? ModContent.Db.Items[baseId] : loot.PickBase(rarity, level, Rng)!;
        var item = loot.RollItem(def, level, Rng);
        PlayerState.Character.Inventory.AddToBag(item, def.StackSize);
        return item;
    }

    private static int _releaseAtFrame = -1;

    /// <summary>
    /// A tap of the attack button: the mouse's left button down now and up a few frames later, through the game's own
    /// input. Holding it charges an attack and calling the controller's AttackButtonDown alone never lets go, so neither
    /// starts a plain swing (COS-158).
    /// </summary>
    public static void TapAttack()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) { SelfTest.Log("tap attack: no mouse"); return; }
        MouseButton(mouse, true);
        _releaseAtFrame = Time.frameCount + 3;
    }

    /// <summary>Per frame while self-tests run: releases a tapped button.</summary>
    public static void Tick()
    {
        if (_releaseAtFrame < 0 || Time.frameCount < _releaseAtFrame) return;
        _releaseAtFrame = -1;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null) MouseButton(mouse, false);
    }

    private static void MouseButton(UnityEngine.InputSystem.Mouse mouse, bool down)
    {
        try
        {
            var buffer = UnityEngine.InputSystem.LowLevel.StateEvent.From(mouse, out var ev, Unity.Collections.Allocator.Temp);
            UnityEngine.InputSystem.InputControlExtensions.WriteValueIntoEvent(mouse.leftButton, down ? 1f : 0f, ev);
            UnityEngine.InputSystem.InputSystem.QueueEvent(ev);
            buffer.Dispose();
        }
        catch (Exception e)
        {
            SelfTest.Log("tap attack: mouse event failed " + e.Message);
        }
    }

    /// <summary>Saves a screenshot to %TEMP%\cots-ui\<name>.png and logs the path. Null if capture failed.</summary>
    public static string? Screenshot(string name)
    {
        var path = UI.UiCapture.SaveScreen(name);
        SelfTest.Log(path != null ? $"screenshot {path}" : $"screenshot {name} failed: {UI.UiCapture.LastError}");
        return path;
    }
}
