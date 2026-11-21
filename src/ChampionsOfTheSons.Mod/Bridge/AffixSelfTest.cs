using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using Sons.Ai.Vail;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// COS-19 in-game checks: one Hell elite per passive ability, forced by switching every other ability off, then each ability's effect
/// on the game (scale, speed, health, immunity, revive, stun, poison, rain, avenger/sacrifice, and "nothing acts on an inactive enemy").
/// The headless game runs at time scale 0 with spawned enemies inactive, so the tests switch enemies on and step time themselves.
/// </summary>
internal static class AffixSelfTest
{
    private const float T0 = 20;
    private static readonly Dictionary<EnemyAbility, VailActor?> Actors = new();
    private static VailActor? _plain, _bleeder, _avenger, _sacrifice;
    private static CountingHandler? _counter;
    private static float _healthBefore;

    private sealed class CountingHandler : IAbilityHandler
    {
        public EnemyAbility Ability => EnemyAbility.Tiny;
        public int Ticks, TicksWhileInactive;
        public readonly Dictionary<EnemyRuntime, int> PerEnemy = new();
        public void OnTick(EnemyRuntime enemy, float dt)
        {
            Ticks++;
            PerEnemy[enemy] = PerEnemy.GetValueOrDefault(enemy) + 1;
            if (!enemy.IsActive) TicksWhileInactive++;
        }
    }

    private static readonly EnemyAbility[] Forced =
    {
        EnemyAbility.Gargantuan, EnemyAbility.Tiny, EnemyAbility.Juggernaut, EnemyAbility.ExtraHealth, EnemyAbility.ExtraDamage,
        EnemyAbility.Undead, EnemyAbility.Shielding, EnemyAbility.Basher, EnemyAbility.Poisonous, EnemyAbility.RainEmpowerment,
    };

    /// <summary>Hell, only <paramref name="only"/> may roll, and the damage multiplier is pinned to 1000 so test hits on the player are small.</summary>
    private static WorldRules Rules(params EnemyAbility[] only)
    {
        var disabled = ((EnemyAbility[])Enum.GetValues(typeof(EnemyAbility))).Where(a => !only.Contains(a)).ToArray();
        return new WorldRules
        {
            PartyLevelOverride = Difficulties.Hell.BaseLevelMin,
            AllowElites = true,
            DisabledAbilities = disabled,
            EnemyDamage = 1000.0 / EnemyScaler.DamageMultiplier(301),
        };
    }

    private static WorldRules Plain => new() { PartyLevelOverride = 1, AllowElites = false };

    private static EnemyRuntime? Rt(VailActor? a) => EnemyRegistry.Get(a);

    private static void Clean() => PlayerState.FromSave(new Core.Saving.PlayerSave());

    internal static void Advance(float seconds)
    {
        for (float t = 0; t < seconds; t += 0.1f)
        {
            EnemyAbilityFeature.Step(0.1f);
            StatBridge.Frame(0.1f);
        }
    }

    internal static void Activate(params VailActor?[] actors)
    {
        foreach (var a in actors)
        {
            var r = Rt(a);
            if (r != null) r.TestActive = true;
        }
    }

    public static void Register()
    {
        SelfTest.Add(T0, "affix: spawn", () =>
        {
            Clean();
            SelfTest.Log($"time scale {Time.timeScale}, delta {Time.deltaTime}");
            // The elites are hostile: a tanky player keeps the world running.
            PlayerState.Character.Stats.SetSource("test.tank", new[] { new StatModifier(StatIds.MaxHealth, EffectOp.Add, 100000) });
            int i = 0;
            foreach (var a in Forced)
            {
                var actor = TestKit.Spawn(a == EnemyAbility.Undead ? VailActorTypeId.Fingers : VailActorTypeId.Carl, 9 + i / 4 * 3, (i % 4 - 1.5f) * 4, Rules(a));
                Actors[a] = actor;
                i++;
            }
            _plain = TestKit.Spawn(distance: 5, sideways: -9, rules: Plain);
            _bleeder = TestKit.Spawn(distance: 5, sideways: 9, rules: Plain);
            _avenger = TestKit.Spawn(distance: 20, sideways: -3, rules: Rules(EnemyAbility.Avenger));
            _sacrifice = TestKit.Spawn(distance: 20, sideways: 3, rules: Rules(EnemyAbility.Sacrifice));
            _counter = new CountingHandler();
            EnemyAbilities.Register(_counter);
            SelfTest.Check("spawned the forced-ability enemies", Actors.Values.All(a => a != null) && _plain != null && _avenger != null);
        });

        SelfTest.Add(T0 + 4, "affix: profiles carry the forced ability", () =>
        {
            LocalPlayer.Vitals.SetFullHealth();
            foreach (var (ability, actor) in Actors)
            {
                var r = Rt(actor);
                SelfTest.Check($"{ability}: rolled", r != null && r.Profile.Has(ability),
                    r == null ? "no profile" : $"[{string.Join(",", r.Profile.Abilities)}] tier {r.Profile.Tier} level {r.Profile.Level}, active {r.IsActive}");
            }
            Activate(Actors.Values.ToArray());
            Activate(_plain, _bleeder, _avenger, _sacrifice);
            Advance(0.5f);
        });

        SelfTest.Add(T0 + 5, "affix: scale and speed (Gargantuan, Tiny)", () =>
        {
            foreach (var (ab, ac) in Actors.Take(2))
            {
                var rr = Rt(ac);
                var go = ac!.gameObject;
                SelfTest.Log($"diag {ab}: same actor={rr?.Actor != null && rr.Actor.Pointer == ac.Pointer} self={go.activeSelf} hierarchy={go.activeInHierarchy} parent={go.transform.parent?.name} parentActive={go.transform.parent?.gameObject.activeInHierarchy} layer={go.layer} pos={go.transform.position} scene={go.scene.name}");
            }
            foreach (var a in new[] { EnemyAbility.Gargantuan, EnemyAbility.Tiny })
            {
                var actor = Actors[a];
                var r = Rt(actor);
                if (actor == null || r == null) continue;
                SelfTest.Check($"{a}: game scale follows the profile", Mathf.Abs(actor.GetScaleMult() - (float)r.Profile.Scale) < 0.02f,
                    $"{actor.GetScaleMult():0.00} vs {r.Profile.Scale:0.00}");
                float anim = actor._animator != null ? actor._animator.speed : -1;
                SelfTest.Check($"{a}: animation speed follows the profile", Mathf.Abs(anim - (float)r.Profile.Speed) < 0.05f * (float)r.Profile.Speed,
                    $"animator {anim:0.00} vs profile {r.Profile.Speed:0.00} (wanted {r.WantedSpeed:0.00}, active {r.IsActive})");
            }
            var g = Rt(Actors[EnemyAbility.Gargantuan]);
            var t = Rt(Actors[EnemyAbility.Tiny]);
            SelfTest.Check("Gargantuan is bigger and slower than Tiny", g != null && t != null && g.Profile.Scale > t.Profile.Scale && g.Profile.Speed < t.Profile.Speed);
        });

        SelfTest.Add(T0 + 5.5f, "affix: health and damage (ExtraHealth, ExtraDamage)", () =>
        {
            var h = Rt(Actors[EnemyAbility.ExtraHealth]);
            var d = Rt(Actors[EnemyAbility.ExtraDamage]);
            if (h != null)
            {
                double expected = EnemyScaler.MaxHealth(h.Profile.Level, h.VanillaMaxHealth, 1) * (h.Profile.Tier == EnemyTier.Boss ? 20 : h.Profile.Tier == EnemyTier.Miniboss ? 10 : 5) * 4;
                SelfTest.Check("ExtraHealth: x4 health on top of the tier multiplier", Math.Abs(h.Profile.MaxHealth / expected - 1) < 1e-6, $"{h.Profile.MaxHealth:E3} vs {expected:E3}");
                SelfTest.Check("ExtraHealth: the game has that max health", Mathf.Abs(Actors[EnemyAbility.ExtraHealth]!.GetMaxHealth() / (float)h.Profile.MaxHealth - 1) < 0.01f);
            }
            if (d != null)
            {
                double dmgExpected = EnemyScaler.DamageMultiplier(d.Profile.Level) * 5;
                SelfTest.Check("ExtraDamage: x5 damage multiplier", Math.Abs(d.Profile.DamageMult / dmgExpected - 1) < 1e-6, $"{d.Profile.DamageMult:0.0} vs {dmgExpected:0.0}");
                double hit = EnemyScaler.DamageToPlayer(d.Profile, 30, d.Combat.DamageDealtMultiplier);
                SelfTest.Check("ExtraDamage: hits harder than vanilla", hit > 30, hit.ToString("0"));
            }
        });

        SelfTest.Add(T0 + 6, "affix: Juggernaut ignores crowd control", () =>
        {
            var actor = Actors[EnemyAbility.Juggernaut];
            var r = Rt(actor);
            if (actor == null || r == null) return;
            SelfTest.Check("Juggernaut: knockdown switched off in the game", !actor._knockdownEnabled);
            SelfTest.Check("Juggernaut: refuses slow", !r.Combat.ApplySlow("test", 0.5, 5));
            SelfTest.Check("Juggernaut: refuses stun", !r.Combat.ApplyStun(2));
            SelfTest.Check("Juggernaut: refuses damage over time (COS-90)", !r.Combat.ApplyDot("test", DotKind.Fire, 5, 3) && r.Combat.DotCount == 0);
            SelfTest.Check("Juggernaut: a hit builds no stagger (COS-90)", actor._staggerPerHit == 0, actor._staggerPerHit.ToString());
            var before = actor.transform.position;
            Spells.SpellWorld.Knock(r, Vector3.forward * 3);
            Spells.SpellWorld.Step(0.1f);
            SelfTest.Check("Juggernaut: spells knock it back not at all (COS-90)", Spells.SpellWorld.SlidingCount == 0 && Vector3.Distance(before, actor.transform.position) < 0.01f);
            var plain = Rt(_plain)!;
            SelfTest.Check("a normal enemy is slowed", plain.Combat.ApplySlow("test", 0.5, 5));
            Advance(0.5f);
            float anim = _plain!._animator.speed;
            SelfTest.Check("slow reached the game: animator speed = profile speed x slow", Mathf.Abs(anim - (float)(plain.Profile.Speed * 0.5)) < 0.05f,
                $"animator {anim:0.00}, expected {plain.Profile.Speed * 0.5:0.00}");
            Advance(5);
            SelfTest.Check("slow expired", !plain.Combat.HasSlow && Mathf.Abs(_plain._animator.speed - (float)plain.Profile.Speed) < 0.05f);
        });

        SelfTest.Add(T0 + 7, "affix: Undead revives once", () =>
        {
            var actor = Actors[EnemyAbility.Undead];
            var r = Rt(actor);
            var hp = EnemyRegistry.HealthOf(actor);
            if (actor == null || r == null || hp == null) return;
            hp.SetCurrentValue(5);
            TestKit.Hit(actor, 1e9f);
            SelfTest.Check("Undead: not dead after a lethal hit", !actor.IsDead() && r.Revived, $"hp {actor.GetHealth():E2}");
            SelfTest.Check("Undead: back at half health", Mathf.Abs(actor.GetHealth() / (float)r.Profile.MaxHealth - 0.5f) < 0.02f, $"{actor.GetHealth() / r.Profile.MaxHealth:0.000}");
            Advance(0.3f);
            SelfTest.Check("Undead: speed x1.1 and model x1.4 after reviving (COS-90)",
                Math.Abs(r.Combat.SpeedMultiplier - AbilityTuning.UndeadSpeed) < 1e-6 && Mathf.Abs(actor.GetScaleMult() - EnemyAbilityFeature.WantedScale(r)) < 0.01f
                && Math.Abs(EnemyAbilityFeature.WantedScale(r) - r.Profile.Scale * AbilityTuning.UndeadScale) < 1e-4,
                $"speed {r.Combat.SpeedMultiplier:0.00}, scale {actor.GetScaleMult():0.00} (wanted {EnemyAbilityFeature.WantedScale(r):0.00})");
            hp.SetCurrentValue(5);
            TestKit.Kill(actor);
            SelfTest.Check("Undead: the second lethal hit kills it", actor.IsDead());
        });

        SelfTest.Add(T0 + 8, "affix: Shielding absorbs, then lets damage through", () =>
        {
            var actor = Actors[EnemyAbility.Shielding];
            if (actor == null) return;
            float before = actor.GetHealth();
            TestKit.Hit(actor, 1e9f);
            SelfTest.Check("Shielding: the first hit does nothing", Mathf.Abs(actor.GetHealth() - before) < 1f, $"{before:E3} -> {actor.GetHealth():E3}");
            Advance(2);
            TestKit.Hit(actor, 1e9f);
            SelfTest.Check("Shielding: still immune inside the 3 s window", Mathf.Abs(actor.GetHealth() - before) < 1f);
            Advance(1.5f);
            TestKit.Hit(actor, 1e9f);
            SelfTest.Check("Shielding: damage lands after the window", actor.GetHealth() < before - 1f, $"{before:E3} -> {actor.GetHealth():E3}");
        });

        SelfTest.Add(T0 + 9, "affix: Basher stuns the player, Poisonous poisons them", () =>
        {
            PlayerEffects.Reset();
            LocalPlayer.Vitals.SetFullHealth();
            // The enemy's melee swing is what the bridge attributes the next player hit to.
            PlayerEffects.LastAttacker = (Rt(Actors[EnemyAbility.Basher])!, Time.time);
            LocalPlayer.Stats.Hit(1f);
            SelfTest.Check("Basher: player is stunned", PlayerEffects.Stunned);
            Advance(1.2f);
            SelfTest.Check("Basher: the stun lasts about 1 s", !PlayerEffects.Stunned);
            _healthBefore = LocalPlayer.Vitals.Health._currentValue;
            PlayerEffects.LastAttacker = (Rt(Actors[EnemyAbility.Poisonous])!, Time.time);
            LocalPlayer.Stats.Hit(3f);
            SelfTest.Check("Poisonous: a poison is running", PlayerEffects.PoisonCount > 0, PlayerEffects.PoisonCount.ToString());
            Advance(3.2f);
            SelfTest.Check("Poisonous: health fell more than the hit alone", LocalPlayer.Vitals.Health._currentValue < _healthBefore - 8,
                $"{_healthBefore:0.0} -> {LocalPlayer.Vitals.Health._currentValue:0.0}");
            PlayerEffects.Reset();
            LocalPlayer.Vitals.SetFullHealth();
        });

        SelfTest.Add(T0 + 10, "affix: Rain Empowerment", () =>
        {
            EnemyAbilityFeature.RainOverride = true;
            Advance(0.3f);
            var actor = Actors[EnemyAbility.RainEmpowerment];
            var r = Rt(actor);
            if (actor == null || r == null) return;
            SelfTest.Check("Rain: damage x5, armor x5, speed x2", r.Combat.RainActive && Math.Abs(r.Combat.DamageDealtMultiplier - 5) < 1e-9 && r.Combat.ArmorMultiplier == 5 && r.Combat.SpeedMultiplier == 2);
            SelfTest.Check("Rain: the animation runs twice as fast", Mathf.Abs(actor._animator.speed - (float)r.Profile.Speed * 2) < 0.1f, $"{actor._animator.speed:0.00} vs {r.Profile.Speed * 2:0.00}");
            var gargantuan = Rt(Actors[EnemyAbility.Gargantuan]);
            SelfTest.Check("Rain: other enemies are not empowered", gargantuan != null && !gargantuan.Combat.RainActive);
            EnemyAbilityFeature.RainOverride = false;
            Advance(0.3f);
            SelfTest.Check("Rain: it ends with the rain", !r.Combat.RainActive);
        });

        SelfTest.Add(T0 + 11, "affix: Avenger and Sacrifice", () =>
        {
            var a = Rt(_avenger);
            var s = Rt(_sacrifice);
            if (a == null || s == null) return;
            Advance(0.3f);
            EnemyRegistry.HealthOf(_avenger)!.SetCurrentValue((float)(a.Profile.MaxHealth * 0.4));
            EnemyRegistry.HealthOf(_sacrifice)!.SetCurrentValue(5);
            _healthBefore = _avenger!.GetHealth();
            TestKit.Kill(_sacrifice!);
            SelfTest.Check("Sacrifice died", _sacrifice!.IsDead());
            SelfTest.Check("Avenger: grew angrier", a.Combat.FuryStacks >= 1 && a.Combat.DamageDealtMultiplier > 1, $"stacks {a.Combat.FuryStacks}");
            SelfTest.Check("Sacrifice: healed its neighbour", _avenger.GetHealth() > _healthBefore + (float)a.Profile.MaxHealth * 0.2f,
                $"{_healthBefore / a.Profile.MaxHealth:0.00} -> {_avenger.GetHealth() / a.Profile.MaxHealth:0.00}");
        });

        SelfTest.Add(T0 + 12, "affix: bleed damages over time", () =>
        {
            var r = Rt(_bleeder);
            if (r == null) return;
            _healthBefore = _bleeder!.GetHealth();
            r.Combat.ApplyDot("player.bleed", DotKind.Bleed, Math.Max(1, _healthBefore / 20), 3);
            Advance(3.5f);
            SelfTest.Check("bleed took health in 3 ticks", _bleeder.GetHealth() < _healthBefore * 0.95f && _bleeder.GetHealth() > _healthBefore * 0.8f,
                $"{_healthBefore:0.0} -> {_bleeder.GetHealth():0.0}");
            SelfTest.Check("bleed ended", r.Combat.DotCount == 0);
        });

        SelfTest.Add(T0 + 12.5f, "affix: burning melts armor, knockback slides", () =>
        {
            var r = Rt(_bleeder);
            if (r == null || _bleeder == null) return;
            r.ArmorReduction = 0;
            r.Combat.ApplyDot("test.burn", DotKind.Fire, 2, 3);
            Advance(3.5f);
            double want = Math.Min(r.Profile.Armor, 6);
            SelfTest.Check("burn ticks add their damage to the armor reduction, capped at its armor (COS-90)",
                r.Profile.Armor <= 0 || Math.Abs(r.ArmorReduction - want) < 0.01, $"reduction {r.ArmorReduction:0.##}, armor {r.Profile.Armor:0.##}, wanted {want:0.##}");

            var start = _bleeder.transform.position;
            Spells.SpellWorld.Knock(r, Vector3.forward * 3);
            Spells.SpellWorld.Step(0.05f);
            float first = Vector3.Distance(start, _bleeder.transform.position);
            SelfTest.Check("knockback is not an instant jump (COS-90)", first > 0.1f && first < 2.9f, $"{first:0.00} m after 0.05 s");
            for (int i = 0; i < 20; i++) Spells.SpellWorld.Step(0.1f);
            float total = Vector3.Distance(start, _bleeder.transform.position);
            SelfTest.Check("knockback slides the whole distance and stops", total > 2.8f && Spells.SpellWorld.SlidingCount == 0, $"{total:0.00} m, sliding {Spells.SpellWorld.SlidingCount}");
        });

        SelfTest.Add(T0 + 13, "affix: abilities stop on an inactive enemy (bug #43)", () =>
        {
            var actor = Actors[EnemyAbility.Tiny];
            if (actor == null || _counter == null) return;
            Advance(1);
            var tiny = Rt(actor)!;
            SelfTest.Check("a Tiny handler ticks while the enemy is active", _counter.PerEnemy.GetValueOrDefault(tiny) >= 9, _counter.PerEnemy.GetValueOrDefault(tiny).ToString());
            SelfTest.Check("the handler never ran for an inactive enemy", _counter.TicksWhileInactive == 0);
            int at = _counter.PerEnemy.GetValueOrDefault(tiny);
            Rt(actor)!.TestActive = false;
            Advance(1);
            SelfTest.Check("an inactive enemy stops ticking", _counter.PerEnemy.GetValueOrDefault(tiny) == at, $"{at} -> {_counter.PerEnemy.GetValueOrDefault(tiny)}");
            var r = tiny;
            r.Combat.ApplyDot("test", DotKind.Fire, 1, 3);
            Advance(3.5f);
            SelfTest.Check("its damage over time does not run either", r.Combat.DotCount == 1);
            PlayerState.Character.Stats.RemoveSource("test.tank");
        });
    }
}
