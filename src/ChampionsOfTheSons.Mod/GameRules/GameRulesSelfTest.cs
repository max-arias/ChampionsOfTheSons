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
