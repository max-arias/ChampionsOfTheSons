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

    /// <summary>A plain hit from the local player through the game's damage path (our hooks apply).</summary>
    public static void Hit(VailActor actor, float amount, ImpactType type = ImpactType.Melee) =>
        actor.ReceivedDamage(null, type, 0, null, amount, true, false, false, Vector3.forward);

    /// <summary>Kills an enemy as the local player: lethal hits, then the game's death sequence
    /// (calling ReceivedDamage directly skips the caller that normally starts it).</summary>
    public static void Kill(VailActor actor)
    {
        for (int i = 0; i < 6 && actor.GetHealth() > 0; i++) Hit(actor, 1e9f);
        if (!actor.IsDead() && actor.GetHealth() <= 0) actor.OnDeath();
    }

    public static EnemyRuntime? Profile(VailActor? actor) => EnemyRegistry.Get(actor);

    /// <summary>Rolls an item into the bag. Null base id = random item of the given rarity.</summary>
    public static ItemInstance Give(string? baseId = null, int rarity = 4, int level = 20)
    {
        var loot = new LootRoller(ModContent.Db);
        var def = baseId != null ? ModContent.Db.Items[baseId] : loot.PickBase(rarity, level, Rng)!;
        var item = loot.RollItem(def, level, Rng);
        PlayerState.Character.Inventory.AddToBag(item, def.StackSize);
        return item;
    }

    /// <summary>Saves a screenshot to %TEMP%\cots-ui\<name>.png and logs the path. Null if capture failed.</summary>
    public static string? Screenshot(string name)
    {
        var path = UI.UiCapture.SaveScreen(name);
        SelfTest.Log(path != null ? $"screenshot {path}" : $"screenshot {name} failed: {UI.UiCapture.LastError}");
        return path;
    }
}
