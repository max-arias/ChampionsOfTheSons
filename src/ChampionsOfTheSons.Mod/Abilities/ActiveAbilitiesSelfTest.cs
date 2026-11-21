using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Abilities;

/// <summary>
/// COS-20 in-game checks. One Hell elite per active affix (every other affix switched off), activated on its own: it must cast and
/// land its effect on the player; then it must stop and clean up when it goes inactive, and again when it dies (bug #43).
/// The headless game keeps spawned enemies dormant at time scale 0, so the test switches them on and steps time itself.
/// </summary>
internal static class ActiveAbilitiesSelfTest
{
    private sealed record Case(EnemyAbility Ability, float Distance, bool NeedsObjects = true);

    private static readonly Case[] Cases =
    {
        new(EnemyAbility.Meteor, 14),
        new(EnemyAbility.Laser, 14),
        new(EnemyAbility.BlackHole, 14),
        new(EnemyAbility.Blizzard, 6),
        new(EnemyAbility.Radiance, 6),
        new(EnemyAbility.Flare, 14),
        new(EnemyAbility.FireCataclysm, 6),
        new(EnemyAbility.ArcaneCataclysm, 6),
        new(EnemyAbility.Warp, 30),
        new(EnemyAbility.Chains, 12),
        new(EnemyAbility.Trapper, 14),
    };

    private static readonly Dictionary<EnemyAbility, VailActor?> Actors = new();

    private static WorldRules Rules(EnemyAbility only)
    {
        var disabled = ((EnemyAbility[])Enum.GetValues(typeof(EnemyAbility))).Where(a => a != only).ToArray();
        return new WorldRules { PartyLevelOverride = Difficulties.Hell.BaseLevelMin, AllowElites = true, DisabledAbilities = disabled };
    }

    private const float Dt = 0.25f;

    private static void Frame()
    {
        EnemyAbilityFeature.Step(Dt);
        StatBridge.Frame(Dt);
    }

    private static void Run(float seconds)
    {
        for (float t = 0; t < seconds; t += Dt)
        {
            Heal();
            Frame();
        }
    }

    private static void Heal() => LocalPlayer.Vitals.SetFullHealth();

    /// <summary>Steps time in 0.1 s frames until <paramref name="done"/> or <paramref name="max"/> seconds; true if it was reached.</summary>
    private static bool AdvanceUntil(Func<bool> done, float max)
    {
        for (float t = 0; t < max; t += Dt)
        {
            if (done()) return true;
            Heal();
            Frame();
            if (done()) return true;
        }
        return false;
    }

    public static void Register()
    {
        SelfTest.Add(0, "active affixes: spawn", () =>
        {
            ActiveAbilitySystem.Reset();
            PlayerEffects.Reset();
            // Enemies left over from earlier phases would now cast at the player too: put them to sleep (the abilities are real).
            foreach (var other in EnemyRegistry.Snapshot()) other.TestActive = false;
            PlayerState.Character.Stats.SetSource("test.tank", new[] { new StatModifier(StatIds.MaxHealth, EffectOp.Add, 1e8) });
            Heal();
            foreach (var c in Cases)
                Actors[c.Ability] = TestKit.Spawn(VailActorTypeId.Carl, c.Distance, 0, Rules(c.Ability));
            foreach (var c in Cases)
            {
                var r = EnemyRegistry.Get(Actors[c.Ability]);
                if (r != null) r.TestActive = false; // dormant until its own step, so it cannot cast while the others are tested
                SelfTest.Check($"{c.Ability}: enemy rolled the affix", r != null && r.Profile.Has(c.Ability),
                    r == null ? "no profile" : $"[{string.Join(",", r.Profile.Abilities)}]");
            }
        });

        int at = 1;
        foreach (var c in Cases)
            SelfTest.Add(at++, $"active affix {c.Ability}: casts, lands, stops when inactive, stops when dead", () => RunCase(c));

        SelfTest.Add(at + 1, "active affixes: nothing left over", () =>
        {
            SelfTest.Check("no ability slow left on the player from our test enemies", Cases.All(c => EnemyRegistry.Get(Actors[c.Ability]) is not { } r || AbilityForces.SlowsOf(r) == 0));
            PlayerState.Character.Stats.RemoveSource("test.tank");
            PlayerEffects.Reset();
            ActiveAbilitySystem.Reset();
        });
    }

    private static void RunCase(Case c)
    {
        var a = c.Ability;
        var actor = Actors.GetValueOrDefault(a);
        var enemy = EnemyRegistry.Get(actor);
        if (actor == null || enemy == null)
        {
            SelfTest.Check($"{a}: enemy exists", false);
            return;
        }
        PlayerEffects.Reset();
        Heal();

        // 1. Active enemy: it casts and the effect reaches the player.
        enemy.TestActive = true;
        bool landed = AdvanceUntil(() => AbilityDamage.HitCount(enemy, a) > 0, 20);
        SelfTest.Check($"{a}: fired and landed on the player", landed && AbilityDamage.FiredCount(enemy, a) >= 1,
            $"fired {AbilityDamage.FiredCount(enemy, a)}, hits {AbilityDamage.HitCount(enemy, a)}, damage {AbilityDamage.DamageDealt(enemy, a):0}");
        switch (a)
        {
            case EnemyAbility.Blizzard:
                AdvanceUntil(() => AbilityForces.SlowsOf(enemy) > 0, 2);
                SelfTest.Check("Blizzard: the player is slowed", AbilityForces.SlowsOf(enemy) > 0 && AbilityForces.SpeedMultiplier < 1, AbilityForces.SpeedMultiplier.ToString("0.00"));
                SelfTest.Check("Blizzard: attack speed x0.8 while inside (COS-90)", Math.Abs(PlayerDebuffs.Buffs.Product(PlayerDebuffs.AttackSlow) - ActiveAbilityRules.BlizzardAttackSlow) < 1e-6,
                    PlayerDebuffs.Buffs.Product(PlayerDebuffs.AttackSlow).ToString("0.00"));
                break;
            case EnemyAbility.Radiance:
                SelfTest.Check("Radiance: damage dealt x0.85 while inside (COS-90)", Math.Abs(PlayerDebuffs.Buffs.Product(PlayerDebuffs.DamageDown) - ActiveAbilityRules.RadianceDamageDown) < 1e-6,
                    PlayerDebuffs.Buffs.Product(PlayerDebuffs.DamageDown).ToString("0.00"));
                break;
            case EnemyAbility.Meteor:
                SelfTest.Check("Meteor: the hit corrodes armor (COS-90)", PlayerDebuffs.Buffs.Has(PlayerDebuffs.ArmorCorruption)
                    && Math.Abs(PlayerDebuffs.LostArmor - PlayerState.Character.Stats.Get(StatIds.Armor) * ActiveAbilityRules.MeteorCorrosionShare * AbilityDamage.HitCount(enemy, a)) < 1e-6,
                    $"lost {PlayerDebuffs.LostArmor:0.##}");
                break;
            case EnemyAbility.Laser:
                SelfTest.Check("Laser: attack speed x0.7 and damage x0.8 (COS-90)", Math.Abs(PlayerDebuffs.Buffs.Product(PlayerDebuffs.AttackSlow) - ActiveAbilityRules.LaserAttackSlow) < 1e-6
                    && Math.Abs(PlayerDebuffs.Buffs.Product(PlayerDebuffs.DamageDown) - ActiveAbilityRules.LaserDamageDown) < 1e-6);
                break;
            case EnemyAbility.ArcaneCataclysm:
                SelfTest.Check("Arcane Cataclysm: the player is slowed", AbilityForces.SlowsOf(enemy) > 0, AbilityForces.SpeedMultiplier.ToString("0.00"));
                break;
            case EnemyAbility.BlackHole:
                SelfTest.Check("Black Hole: it pulls the player", AdvanceUntil(() => AbilityForces.PullOf(enemy).magnitude > 1, 3),
                    AbilityForces.PullOf(enemy).ToString());
                break;
            case EnemyAbility.Chains:
                SelfTest.Check("Chains: the player is rooted", PlayerEffects.Stunned);
                break;
            case EnemyAbility.Warp:
                {
                    var player = LocalPlayer.Transform.position;
                    float d = Vector3.Distance(player, actor.transform.position);
                    SelfTest.Check("Warp: the enemy landed next to the player", d < 8, $"{d:0.0} m away (started 30 m)");
                    break;
                }
        }

        // 2. Goes inactive (dormant, despawned): effects are removed at once and it stops casting.
        if (c.NeedsObjects)
        {
            AdvanceUntil(() => ActiveAbilitySystem.ObjectCount(enemy) > 0, 25);
            SelfTest.Check($"{a}: it has visuals while active", ActiveAbilitySystem.ObjectCount(enemy) > 0);
        }
        enemy.TestActive = false;
        Run(0.5f);
        SelfTest.Check($"{a}: inactive enemy removes its effects", ActiveAbilitySystem.ObjectCount(enemy) == 0, ActiveAbilitySystem.ObjectCount(enemy).ToString());
        SelfTest.Check($"{a}: inactive enemy leaves no slow or pull", AbilityForces.SlowsOf(enemy) == 0 && AbilityForces.PullOf(enemy) == Vector3.zero);
        SelfTest.Check($"{a}: inactive enemy frees its cast gate (COS-90)", !enemy.Combat.Gate.Busy);
        if (a is EnemyAbility.Blizzard or EnemyAbility.Radiance)
            SelfTest.Check($"{a}: inactive aura leaves no debuff on the player (COS-90)", AbilityForces.DebuffsOf(enemy) == 0, AbilityForces.DebuffsOf(enemy).ToString());
        int fired = AbilityDamage.FiredCount(enemy, a), hits = AbilityDamage.HitCount(enemy, a);
        Run(9);
        SelfTest.Check($"{a}: inactive enemy never casts or hits", AbilityDamage.FiredCount(enemy, a) == fired && AbilityDamage.HitCount(enemy, a) == hits,
            $"fired {fired} -> {AbilityDamage.FiredCount(enemy, a)}, hits {hits} -> {AbilityDamage.HitCount(enemy, a)}");

        // 3. Active again, then killed mid-ability: effects removed, no more casts.
        PlayerEffects.Reset();
        enemy.TestActive = true;
        AdvanceUntil(() => ActiveAbilitySystem.ObjectCount(enemy) > 0, 25);
        EnemyRegistry.HealthOf(actor)?.SetCurrentValue(5);
        TestKit.Kill(actor);
        Run(0.5f);
        SelfTest.Check($"{a}: dead enemy removes its effects", actor.IsDead() && ActiveAbilitySystem.ObjectCount(enemy) == 0,
            $"dead {actor.IsDead()}, objects {ActiveAbilitySystem.ObjectCount(enemy)}");
        fired = AbilityDamage.FiredCount(enemy, a);
        hits = AbilityDamage.HitCount(enemy, a);
        Run(9);
        SelfTest.Check($"{a}: dead enemy never casts or hits again", AbilityDamage.FiredCount(enemy, a) == fired && AbilityDamage.HitCount(enemy, a) == hits,
            $"fired {fired} -> {AbilityDamage.FiredCount(enemy, a)}, hits {hits} -> {AbilityDamage.HitCount(enemy, a)}");
        PlayerEffects.Reset();
    }
}
