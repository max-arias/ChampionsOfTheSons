using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using Sons.Ai.Vail;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>In-game checks for the enemy and player hooks (run with --cots-selftest).</summary>
public static class BridgeSelfTest
{
    private static VailActor? _easy;
    private static VailActor? _hell;
    private static VailActor? _victim;

    public static void Register()
    {
        SelfTest.Add(1, "spawn enemies", () =>
        {
            var t = LocalPlayer.Transform;
            // Party level 1 vs Hell's level 300 (COS-130: levels follow the party; the override stands in for it).
            // No elite on the low one: a random defensive affix (shield, steadfast) would swallow the 10-damage probe below.
            var saved = WorldState.Rules;
            WorldState.SetRulesForTest(saved with { AllowElites = false, PartyLevelOverride = 1 });
            _easy = ActorTools.Spawn(VailActorTypeId.Carl, t.position + t.forward * 6 + t.right * 2);
            WorldState.SetRulesForTest(saved with { PartyLevelOverride = Difficulties.Hell.BaseLevelMin });
            _hell = ActorTools.Spawn(VailActorTypeId.Carl, t.position + t.forward * 6 - t.right * 2);
            WorldState.SetRulesForTest(saved);
            SelfTest.Check("spawned two cannibals", _easy != null && _hell != null);
        });

        SelfTest.Add(4, "enemy scaling", () =>
        {
            var easy = EnemyRegistry.Get(_easy);
            var hell = EnemyRegistry.Get(_hell);
            SelfTest.Check("easy enemy has a profile", easy != null, $"registry={EnemyRegistry.Count}");
            SelfTest.Check("hell enemy has a profile", hell != null);
            if (easy == null || hell == null) return;
            SelfTest.Log($"easy: level {easy.Profile.Level} {easy.Profile.Tier} hp {_easy!.GetMaxHealth():N0} (vanilla {easy.VanillaMaxHealth:N0}) armor {easy.Profile.Armor:N0}");
            SelfTest.Log($"hell: level {hell.Profile.Level} {hell.Profile.Tier} [{string.Join(",", hell.Profile.Abilities)}] hp {_hell!.GetMaxHealth():N0} armor {hell.Profile.Armor:N0} bounty {hell.Profile.Bounty:N0}");
            SelfTest.Check("easy level in range", easy.Profile.Level is >= 2 and <= 4, easy.Profile.Level.ToString());
            SelfTest.Check("hell level is 301", hell.Profile.Level == 301, hell.Profile.Level.ToString());
            SelfTest.Check("game max health = our max health", Math.Abs(_easy.GetMaxHealth() - easy.Profile.MaxHealth) <= easy.Profile.MaxHealth * 0.01 + 1,
                $"{_easy.GetMaxHealth()} vs {easy.Profile.MaxHealth}");
            SelfTest.Check("hell enemy is much tougher", _hell.GetMaxHealth() > _easy.GetMaxHealth() * 1000);
            SelfTest.Check("hell enemy is elite or better", hell.Profile.Tier != EnemyTier.Normal);
        });

        SelfTest.Add(5, "player damage goes through our stats", () =>
        {
            if (_easy == null) return;
            float before = _easy.GetHealth();
            _easy.ReceivedDamage(null, ImpactType.Melee, 0, null, 10f, true, false, false, Vector3.forward);
            float after = _easy.GetHealth();
            var hit = EnemyPatches.LastHit;
            SelfTest.Check("damage hook ran", hit != null);
            SelfTest.Check("enemy lost health", after < before, $"{before:N1} -> {after:N1}, hook {hit}");
        });

        SelfTest.Add(6, "spawn a normal enemy to kill", () =>
        {
            var t = LocalPlayer.Transform;
            var rules = WorldState.Rules;
            WorldState.SetRulesForTest(rules with { AllowElites = false });
            _victim = ActorTools.Spawn(VailActorTypeId.Carl, t.position + t.forward * 8);
            WorldState.SetRulesForTest(rules);
        });

        SelfTest.Add(8, "kill gives xp", () =>
        {
            if (_victim == null) return;
            var r = EnemyRegistry.Get(_victim);
            SelfTest.Log($"victim: level {r?.Profile.Level} {r?.Profile.Tier} hp {_victim.GetHealth():N0}, bounty {r?.Profile.Bounty:N0}");
            _victim.ReceivedDamage(null, ImpactType.Melee, 0, null, 1e9f, true, false, false, Vector3.forward);
            SelfTest.Log($"after lethal ReceivedDamage: hp {_victim.GetHealth():N1} dead={_victim.IsDead()} hook={EnemyPatches.LastHit}");
        });

        SelfTest.Add(9, "finish the victim", () =>
        {
            if (_victim == null || _victim.IsDead()) return;
            for (int i = 0; i < 5 && !_victim.IsDead(); i++)
                _victim.ReceivedDamage(null, ImpactType.Melee, 0, null, 1e9f, true, false, false, Vector3.forward);
            SelfTest.Log($"after 5 more hits: hp {_victim.GetHealth():N1} dead={_victim.IsDead()}");
            if (!_victim.IsDead() && _victim.GetHealth() <= 0)
            {
                // ReceivedDamage alone doesn't run the death sequence (its caller ServerProcessImpact does); start it.
                _victim.OnDeath();
                SelfTest.Log($"OnDeath(): dead={_victim.IsDead()}");
            }
        });

        SelfTest.Add(12, "kill was credited", () =>
        {
            var m = PlayerState.Character.Massacre;
            SelfTest.Check("victim died", _victim == null || _victim.IsDead(), $"dead={_victim?.IsDead()}");
            SelfTest.Check("kill banked xp in the streak", m.Kills >= 1 && m.PendingXp > 0, $"kills={m.Kills} pending={m.PendingXp:N0}");
            SelfTest.Check("dead enemy left the registry", EnemyRegistry.Get(_victim) == null);
            SelfTest.Check("elite cap held on the first enemy", EnemyRegistry.Get(_easy) is not { Profile.HitCap: < 1 } || !_easy!.IsDead());
        });

        SelfTest.Add(10, "equip changes max health", () =>
        {
            var c = PlayerState.Character;
            var ring = ModContent.Db.Items.Values.First(i => i.Type == "ring");
            c.Inventory.AddToBag(new ItemInstance { BaseId = ring.Id, Level = 10, Stats = new[] { new RolledStat("stat.maximumlife", 50) } });
            var slot = c.Inventory.Slots.First(kv => kv.Value.BaseId == ring.Id && kv.Key.StartsWith("bag.")).Key;
            SelfTest.Check("ring equipped", c.Inventory.Equip(slot, ModContent.Db));
        });

        SelfTest.Add(11, "max health applied", () =>
        {
            float expected = (float)Derived.MaxHealth(PlayerState.Character.Stats);
            float actual = LocalPlayer.Vitals.Health.GetMax();
            SelfTest.Check("game max health follows the stat sheet", Mathf.Abs(expected - actual) < 0.5f, $"{actual} vs {expected}");
            SelfTest.Check("ring raised max health above 100", actual >= 149, actual.ToString("N1"));
        });

        SelfTest.Add(12, "incoming damage", () =>
        {
            float before = LocalPlayer.Vitals.Health._currentValue;
            LocalPlayer.Stats.Hit(5f);
            var hit = PlayerBridge.LastIncoming;
            SelfTest.Check("defense hook ran", hit != null, hit.ToString());
            SelfTest.Log($"player health {before:N1} -> {LocalPlayer.Vitals.Health._currentValue:N1}");
        });
    }
}
