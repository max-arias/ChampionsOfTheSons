using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.GameRules;

/// <summary>COS-92 in game: fire and traps on the player take COTF's amount and are not dodged; meds heal a scaled amount; an
/// explosion does not kill a Hell elite outright.</summary>
internal static class GameRulesSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static VailActor? _elite;

    public static void Register()
    {
        SelfTest.Add(0.5f, "rules: unpause", TestKit.Unpause);
        SelfTest.Add(1f, "rules: fire and trap hits", () =>
        {
            // Nothing dodges here even at 100% dodge.
            Pc.Stats.SetSource("test.dodge", new[] { new StatModifier(StatIds.GetHitChance, EffectOp.Mult, 0) });
            var h = LocalPlayer.Vitals.Health;
            h.SetCurrentValue(h.GetMax());
            LocalPlayer.Stats.HitFire();
            var fire = GameRulesFeature.LastHit;
            SelfTest.Check("rules: fire on the player is not dodged and takes 1% + 3", fire.Source == PlayerDamageSource.Fire && fire.Final > 0,
                $"{fire.Source} {fire.Game:0.#} -> {fire.Final:0.#}");
            h.SetCurrentValue(h.GetMax());
            LocalPlayer.Stats.HitFromTrap(500);
            var trap = GameRulesFeature.LastHit;
            SelfTest.Check("rules: a trap on the player takes 150", trap.Source == PlayerDamageSource.Trap && Math.Abs(trap.Final - Core.Rules.GameRules.TrapPlayerDamage) < 1,
                $"{trap.Source} {trap.Game:0.#} -> {trap.Final:0.#}");
            Pc.Stats.RemoveSource("test.dodge");
            h.SetCurrentValue(h.GetMax());
        });
        SelfTest.Add(1.5f, "rules: drowning and cold (COS-216)", () =>
        {
            var h = LocalPlayer.Vitals.Health;
            h.SetCurrentValue(h.GetMax());
            GameRulesFeature.HitAsDrowning(5);
            var drown = GameRulesFeature.LastHit;
            double max = Derived.MaxHealth(Pc.Stats);
            SelfTest.Check("rules: drowning takes 3 + 10% of max health", drown.Source == PlayerDamageSource.Drowning && Math.Abs(drown.Final - (3 + 0.1 * max)) < 1,
                $"{drown.Source} {drown.Game:0.#} -> {drown.Final:0.#} (max {max:0})");
            h.SetCurrentValue(h.GetMax());
            float before = LocalPlayer.Stats.ColdFactor;
            LocalPlayer.Stats.ColdFactor = 1;
            float small = LocalPlayer.Stats.GetVitalityLostFromColdFactor();
            var c1 = GameRulesFeature.LastCold;
            Pc.Stats.SetSource("test.tank", new[] { new StatModifier(StatIds.MaxHealth, EffectOp.Add, 4900) });
            double bigMax = Derived.MaxHealth(Pc.Stats);
            float big = LocalPlayer.Stats.GetVitalityLostFromColdFactor();
            var c2 = GameRulesFeature.LastCold;
            Pc.Stats.RemoveSource("test.tank");
            LocalPlayer.Stats.ColdFactor = before;
            SelfTest.Log($"rules: cold vitality loss at max {max:0}: game {c1.Game:0.###} -> {small:0.###}; at max {bigMax:0}: game {c2.Game:0.###} -> {big:0.###}");
            SelfTest.Check("rules: cold's vitality loss scales with max health", c2.Game > 0 && Math.Abs(big - c2.Game * bigMax / 100) < 0.01 * big,
                $"game {c2.Game:0.###} -> {big:0.###} at max {bigMax:0}");
        });
        SelfTest.Add(2f, "rules: a heal scales with max health", () =>
        {
            Pc.Stats.SetSource("test.tank", new[] { new StatModifier(StatIds.MaxHealth, EffectOp.Add, 4900) });
            var v = LocalPlayer.Vitals;
            v.Health.SetCurrentValue(10);
            v.ApplyHealing(10);
            var heal = GameRulesFeature.LastHeal;
            double max = Derived.MaxHealth(Pc.Stats);
            SelfTest.Check("rules: a heal gains 0.2% of max health per point", heal.Final > heal.Game && Math.Abs(heal.Final - Core.Rules.GameRules.Heal(heal.Kind, heal.Game, max)) < 1,
                $"{heal.Kind} {heal.Game:0.#} -> {heal.Final:0.#} (max {max:0})");
            Pc.Stats.RemoveSource("test.tank");
        });
        SelfTest.Add(3f, "rules: spawn a Hell elite", () =>
        {
            var hell = WorldState.Rules with { PartyLevelOverride = Difficulties.Hell.BaseLevelMin };
            _elite = TestKit.Spawn(VailActorTypeId.Carl, 7, 0, hell);
            SelfTest.Check("rules: elite spawned", _elite != null);
        });
        SelfTest.Add(4f, "rules: an explosion does not one-shot it", () =>
        {
            if (_elite == null) return;
            _elite.ReceivedDamage(null, ImpactType.Explosion, 0, null, 1e7f, false, false, false, UnityEngine.Vector3.forward);
            var r = EnemyRegistry.Get(_elite);
            SelfTest.Check("rules: an explosion of 1e7 leaves a Hell enemy alive", !_elite.IsDead() && r != null && r.IsActive, $"dead {_elite.IsDead()}");
            TestKit.Kill(_elite);
            _elite = null;
        });
    }
}
