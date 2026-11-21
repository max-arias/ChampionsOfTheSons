using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// COS-26 in-game checks: every spell besides Blink is cast and its effect measured (damage dealt, debuff applied, buff in the stat sheet,
/// zone/projectile/portal/shield/parry behaviour). The headless game runs at time scale 0 with spawned enemies dormant, so the tests
/// switch enemies on, place them where the spell will reach and step the spell world and enemy time themselves.
/// </summary>
internal static class Spells2SelfTest
{
    private const float T0 = 60;
    private const int PoolSize = 28;
    private static readonly List<VailActor?> Pool = new();
    private static int _next;
    private static int _castEvents;
    private static PlayerCharacter Pc => PlayerState.Character;

    public static void Register()
    {
        SelfTest.Add(T0, "spells2: spawn the target pool", Spawn);
        SelfTest.Add(T0 + 5, "spells2: cast every spell", RunAll);
    }

    // ---------------------------------------------------------------- harness

    private static void Spawn()
    {
        PlayerState.FromSave(new Core.Saving.PlayerSave { Level = 60 });
        SpellWorld.Reset();
        // Plenty of energy so the 500-energy spells can be cast; the cost itself is checked in the Blink test.
        Pc.Stats.SetSource("test.energy", new[] { new StatModifier(StatIds.MaxEnergy, EffectOp.Add, 3000) });
        // A fresh test save (COS-129) has a 100-health character; spell costs and test hits killed it mid-phase. Plenty of health too.
        Pc.Stats.SetSource("test.tank", new[] { new StatModifier(StatIds.MaxHealth, EffectOp.Add, 1e5) });
        var rules = WorldState.Rules;
        WorldState.SetRulesForTest(rules with { PartyLevelOverride = 1, AllowElites = false });
        try
        {
            var t = LocalPlayer.Transform;
            for (int i = 0; i < PoolSize; i++)
                Pool.Add(ActorTools.Spawn(VailActorTypeId.Carl, t.position + t.right * (80 + i * 3) + t.forward * 5));
        }
        finally
        {
            WorldState.SetRulesForTest(rules);
        }
        SelfTest.Check("spells2: target pool spawned", Pool.All(a => a != null), $"{Pool.Count(a => a != null)}/{PoolSize}");
    }

    private static EnemyRuntime? Rt(VailActor? a) => a == null ? null : EnemyRegistry.Get(a);

    /// <summary>The next unused enemy, parked far away and switched off until a test places it.</summary>
    private static VailActor Foe()
    {
        var a = Pool[_next++ % Pool.Count]!;
        var rt = Rt(a);
        if (rt != null) rt.TestActive = false;
        return a;
    }

    /// <summary>Puts the enemy's body centre at a point and switches it on.</summary>
    private static VailActor Put(VailActor a, Vector3 center)
    {
        a.transform.position = center - Vector3.up;
        var rt = Rt(a);
        if (rt != null) rt.TestActive = true;
        return a;
    }

    private static Vector3 OnRay(float distance) => SpellWorld.Eye.position + SpellWorld.Eye.forward * distance;

    private static Vector3 Feet() => LocalPlayer.Transform.position + Vector3.up * 0.1f;

    /// <summary>True when the enemy is dead, or the check holds (a strong spell may have killed the test enemy).</summary>
    private static bool AliveOr(EnemyRuntime rt, Func<bool> whileAlive) => !rt.IsActive || whileAlive();

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z).normalized;

    private static void Advance(double seconds, float step = 0.1f)
    {
        for (double t = 0; t < seconds - 1e-4; t += step)
        {
            SpellWorld.Step(step);
            EnemyAbilityFeature.Step(step);
            Pc.Spells.Tick(step, Pc.Stats);
        }
    }

    private static SpellCastOutcome CastSpell(string id)
    {
        Pc.Spells.Equip(0, id);
        Pc.Spells.Tick(1e6, Pc.Stats);
        var st = LocalPlayer.Vitals.Stamina;
        st.SetCurrentValue(st.GetMax());
        LocalPlayer.Vitals.Health.SetCurrentValue(LocalPlayer.Vitals.Health.GetMax());
        return SpellCaster.Cast(0);
    }

    private static bool Ok(string id)
    {
        var outcome = CastSpell(id);
        SelfTest.Check($"spells2: {id} casts", outcome == SpellCastOutcome.Cast, outcome.ToString());
        return outcome == SpellCastOutcome.Cast;
    }

    private static float Hp(VailActor a) => a.GetHealth();

    private static float Dist(VailActor a, Vector3 to) => Vector3.Distance(a.transform.position + Vector3.up, to);

    private static void Case(string name, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            SelfTest.Check($"spells2: {name}", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
        // Leave nothing running into the next case.
        var st = LocalPlayer.Vitals?.Stamina;
        if (st != null) st.SetCurrentValue(st.GetMax());
        for (int s = 0; s < SpellBook.Slots; s++)
            if (Pc.Spells.IsActive(s)) { Pc.Spells.Stop(s); SpellEffects.Stopped(s); }
        SpellWorld.Reset();
        // Park every target so the next case only sees the enemy it places.
        foreach (var a in Pool)
        {
            var rt = Rt(a);
            if (rt != null) rt.TestActive = false;
        }
    }

    private static double Stat(string id) => Pc.Stats.Get(id);

    // ---------------------------------------------------------------- the spells

    private static void RunAll()
    {
        var hp = LocalPlayer.Vitals.Health;
        hp.SetCurrentValue(hp.GetMax());
        SpellCaster.Casted += _ => _castEvents++;
        SelfTest.Check("spells2: every spell in the content has an effect", ModContent.Db.Spells.Keys.All(SpellCaster.IsImplemented),
            string.Join(", ", ModContent.Db.Spells.Keys.Where(k => !SpellCaster.IsImplemented(k))));

        // ---- player buffs
        Case("war cry", () =>
        {
            double dmg = Stat(StatIds.AllDamage), atk = Stat(StatIds.AttackSpeed), move = Stat(StatIds.MovementSpeed);
            if (!Ok("spell.war_cry")) return;
            SelfTest.Check("spells2: War Cry raises attack and movement speed, not damage", Stat(StatIds.AttackSpeed) > atk * 1.19 && Stat(StatIds.MovementSpeed) > move * 1.19
                && Math.Abs(Stat(StatIds.AllDamage) - dmg) < 1e-6,
                $"damage {dmg:0.##}->{Stat(StatIds.AllDamage):0.##}, attack speed {atk:0.##}->{Stat(StatIds.AttackSpeed):0.##}, move {move:0.##}->{Stat(StatIds.MovementSpeed):0.##}");
            Advance(SpellTuning.WarCryDuration + 1, 1f);
            SelfTest.Check("spells2: War Cry ends", Math.Abs(Stat(StatIds.AttackSpeed) - atk) < 1e-6);
            Pc.Stats.SetSource("test.give_damage", new[] { new StatModifier("spell_war_cry_give_damage", EffectOp.Enable, 1) });
            CastSpell("spell.war_cry");
            SelfTest.Check("spells2: War Cry gives damage with its perk", Stat(StatIds.AllDamage) > dmg * 1.19, $"{dmg:0.##}->{Stat(StatIds.AllDamage):0.##}");
            Pc.Stats.RemoveSource("test.give_damage");
        });
        Case("berserk", () =>
        {
            double dmg = Stat(StatIds.AllDamage), move = Stat(StatIds.MovementSpeed), taken = Stat(StatIds.AllDamageTaken);
            if (!Ok("spell.berserk")) return;
            SelfTest.Check("spells2: Berserk raises damage and movement speed and doubles damage taken", Stat(StatIds.AllDamage) > dmg * 1.29 && Stat(StatIds.MovementSpeed) > move * 1.34
                && Stat(StatIds.AllDamageTaken) > taken * 1.99,
                $"damage {Stat(StatIds.AllDamage):0.##}, move {Stat(StatIds.MovementSpeed):0.##}, taken {Stat(StatIds.AllDamageTaken):0.##}");
            var st = LocalPlayer.Vitals.Stamina;
            st.SetCurrentValue(st.GetMax() * 0.2f);
            Advance(0.2f);
            SelfTest.Check("spells2: Berserk keeps energy at max", st._currentValue >= st.GetMax() - 0.01f, $"{st._currentValue:0.#} / {st.GetMax():0.#}");
            Advance(Stat("spell_berserk_duration") + 1, 1f);
            SelfTest.Check("spells2: Berserk ends and leaves an energy leak", Math.Abs(Stat(StatIds.MovementSpeed) - move) < 1e-6 && SpellWorld.Buffs.Has("berserk_leak")
                && Stat(CombatStatIds.EnergyPerSecond) < 0, $"energy per second {Stat(CombatStatIds.EnergyPerSecond):0.##}");
            Advance(SpellTuning.BerserkLeakSeconds + 1, 1f);
            SelfTest.Check("spells2: the energy leak ends", !SpellWorld.Buffs.Has("berserk_leak") && Stat(CombatStatIds.EnergyPerSecond) >= 0);
        });
        Case("golden skin", () =>
        {
            double armor = Stat(StatIds.Armor), atk = Stat(StatIds.AttackSpeed), taken = Stat(StatIds.AllDamageTaken);
            if (!Ok("spell.golden_skin")) return;
            SelfTest.Check("spells2: Golden Skin gives stun immunity, debuff resistance and attack speed, no armor and no damage cut",
                Stat(CombatStatIds.StunImmunity) > 0 && Stat(SpellTuning.DebuffResistance) > 0 && Stat(StatIds.AttackSpeed) > atk * 1.19
                && Math.Abs(Stat(StatIds.Armor) - armor) < 1e-6 && Math.Abs(Stat(StatIds.AllDamageTaken) - taken) < 1e-6,
                $"stun immunity {Stat(CombatStatIds.StunImmunity):0}, resistance {Stat(SpellTuning.DebuffResistance):0.##}, attack speed {Stat(StatIds.AttackSpeed):0.##}");
            SelfTest.Check("spells2: Golden Skin stops a stun", !PlayerEffects.Stun(3));
            Advance(SpellTuning.GoldenSkinDuration + 1, 1f);
            SelfTest.Check("spells2: Golden Skin ends", Stat(CombatStatIds.StunImmunity) == 0);
            Pc.Stats.SetSource("test.resolve", new[] { new StatModifier(SpellTuning.GoldenResolve, EffectOp.Enable, 1) });
            CastSpell("spell.golden_skin");
            SelfTest.Check("spells2: Golden Skin halves damage taken with Golden Resolve", Stat(StatIds.AllDamageTaken) < taken * 0.51, $"{Stat(StatIds.AllDamageTaken):0.##}");
            Pc.Stats.RemoveSource("test.resolve");
        });
        Case("focus", () =>
        {
            var foe = Put(Foe(), OnRay(8));
            if (!Ok("spell.focus")) return;
            SelfTest.Check("spells2: Focus speeds attacks up", Stat(StatIds.AttackSpeed) >= 1.29, $"{Stat(StatIds.AttackSpeed):0.##}");
            TestKit.Hit(foe, 5f);
            Advance(0.2f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Focus slows what the player hits", rt.Combat.HasSlow && rt.Combat.SpeedMultiplier < 0.9, $"speed ×{rt.Combat.SpeedMultiplier:0.##}");
        });
        Case("frenzy", () =>
        {
            var foe = Put(Foe(), OnRay(8));
            if (!Ok("spell.frenzy")) return;
            double dmg = Stat(StatIds.AllDamage);
            for (int i = 0; i < 3; i++) TestKit.Hit(foe, 1f);
            SelfTest.Check("spells2: Frenzy stacks on hits", SpellWorld.Frenzy.Stacks >= 3 && Stat(StatIds.AllDamage) > dmg * 1.2,
                $"stacks {SpellWorld.Frenzy.Stacks}, damage ×{Stat(StatIds.AllDamage) / dmg:0.###}");
            Advance(SpellTuning.FrenzyDuration + 1);
            SelfTest.Check("spells2: Frenzy stacks end", SpellWorld.Frenzy.Stacks == 0 && Math.Abs(Stat(StatIds.AllDamage) - dmg) < 1e-6);
        });
        Case("taunt", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 15 + Vector3.up);
            double armor = Stat(StatIds.Armor);
            if (!Ok("spell.taunt")) return;
            Advance(0.2f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Taunt sends enemies rushing and hardens the player", rt.Combat.SpeedMultiplier > 1.9 && Stat(StatIds.Armor) > armor + 90,
                $"enemy speed ×{rt.Combat.SpeedMultiplier:0.##}, armor {Stat(StatIds.Armor):0}");
        });
        Case("purge", () =>
        {
            PlayerEffects.AddPoison(5, 10);
            PlayerEffects.Stun(5);
            SelfTest.Check("spells2: player is poisoned and stunned first", PlayerEffects.PoisonCount > 0 && PlayerEffects.Stunned);
            if (!Ok("spell.purge")) return;
            SelfTest.Check("spells2: Purge cleanses poison and stun", PlayerEffects.PoisonCount == 0 && !PlayerEffects.Stunned);
            SelfTest.Check("spells2: Purge gives three seconds of debuff immunity", !PlayerEffects.Stun(3) && SpellWorld.Buffs.Remaining("purge_immunity") > 2.9);
            Advance(SpellTuning.PurgeImmunitySeconds + 0.5);
            SelfTest.Check("spells2: Purge immunity ends", PlayerEffects.Stun(1));
            PlayerEffects.ClearDebuffs();
            Pc.Stats.SetSource("test.purge_heal", new[] { new StatModifier("spell_purge_heal", EffectOp.Enable, 1) });
            var health = LocalPlayer.Vitals.Health;
            Pc.Spells.Tick(1e6, Pc.Stats);
            SpellCaster.Spend(SpellCaster.MaxEnergy * 0.5);
            health.SetCurrentValue(health.GetMax() * 0.5f);
            float hp0 = health._currentValue;
            double e0 = SpellCaster.Energy;
            SpellEffects.PurgeHeal();
            SelfTest.Check("spells2: Purge perk heals 40% of missing health and 30% of missing energy",
                Math.Abs(health._currentValue - (hp0 + (health.GetMax() - hp0) * 0.4f)) < 1 && SpellCaster.Energy > e0 + (SpellCaster.MaxEnergy - e0) * 0.25,
                $"health {hp0:0.#}->{health._currentValue:0.#}, energy {e0:0.#}->{SpellCaster.Energy:0.#}");
            Pc.Stats.RemoveSource("test.purge_heal");
        });

        // ---- parry and shield (incoming damage)
        Case("parry", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 2 + Vector3.up);
            if (!Ok("spell.parry")) return;
            float hpBefore = Hp(foe);
            var health = LocalPlayer.Vitals.Health;
            health.SetCurrentValue(health.GetMax() * 0.5f);
            float playerBefore = health._currentValue;
            LocalPlayer.Stats.Hit(20f);
            SelfTest.Check("spells2: Parry negates the hit", health._currentValue >= playerBefore - 0.01f, $"{playerBefore:0.#} -> {health._currentValue:0.#}");
            SelfTest.Check("spells2: Parry strikes back", Hp(foe) < hpBefore, $"{hpBefore:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: Parry heals and buffs", health._currentValue > playerBefore + 5 && SpellWorld.Buffs.Has("parry"), $"health {health._currentValue:0.#}");
            float after = health._currentValue;
            LocalPlayer.Stats.Hit(20f);
            SelfTest.Check("spells2: only one hit is parried", health._currentValue < after - 1, $"{after:0.#} -> {health._currentValue:0.#}");
        });
        Case("sustain shield", () =>
        {
            if (!Ok("spell.sustain_shield")) return;
            SelfTest.Check("spells2: shield is channeled", Pc.Spells.IsActive(0));
            double energy = SpellCaster.Energy;
            Advance(3);
            double expect = Math.Min(SpellShield.Cap(Pc.Stats), SpellShield.GainPerSecond(Pc.Stats) * 3);
            SelfTest.Check("spells2: shield fills while channeled by 3 + flat/35 per second", SpellWorld.Shield.Amount > 1 && Math.Abs(SpellWorld.Shield.Amount - expect) < 0.5,
                $"{SpellWorld.Shield.Amount:0.##} (expected {expect:0.##})");
            SelfTest.Check("spells2: upkeep drains energy", SpellCaster.Energy < energy - 100, $"{energy:0} -> {SpellCaster.Energy:0}");
            float soaked = SpellPatches.Incoming(10);
            SelfTest.Check("spells2: shield soaks incoming damage", soaked < 10, $"10 -> {soaked:0.##}");
            SelfTest.Check("spells2: second cast switches it off", SpellCaster.Cast(0) == SpellCastOutcome.Stopped && !SpellWorld.Shield.Channeling && !Pc.Spells.IsActive(0));
        });

        // ---- zones
        Case("healing dome", () =>
        {
            var health = LocalPlayer.Vitals.Health;
            CastSpell("spell.healing_dome");
            health.SetCurrentValue(health.GetMax() * 0.5f);
            float before = health._currentValue;
            SelfTest.Check("spells2: Healing Dome zone exists", SpellWorld.ZoneCount == 1);
            Advance(2);
            SelfTest.Check("spells2: Healing Dome heals the player inside by its formula", health._currentValue > before + SpellTuning.HealingDomeHeal(Pc.Stats) * 1.5, $"{before:0.#} -> {health._currentValue:0.#}");
            Pc.Stats.SetSource("test.dome", new[]
            {
                new StatModifier("spell_healing_dome_damage_increase", EffectOp.Add, 0.2), new StatModifier("spell_healing_dome_damage_resistance", EffectOp.Add, 0.5),
                new StatModifier("spell_healing_dome_gives_immunity", EffectOp.Enable, 1),
            });
            double dmg = Stat(StatIds.AllDamage), taken = Stat(StatIds.AllDamageTaken);
            Advance(1);
            SelfTest.Check("spells2: Healing Dome perks boost damage, cut damage taken and give immunity inside",
                Stat(StatIds.AllDamage) > dmg * 1.19 && Stat(StatIds.AllDamageTaken) < taken * 0.51 && !PlayerEffects.Stun(2),
                $"damage x{Stat(StatIds.AllDamage) / dmg:0.##}, taken x{Stat(StatIds.AllDamageTaken) / taken:0.##}");
            Pc.Stats.RemoveSource("test.dome");
            Advance(11);
            SelfTest.Check("spells2: Healing Dome ends", SpellWorld.ZoneCount == 0);
        });
        Case("sun flare", () =>
        {
            var foe = Put(Foe(), Feet() + Vector3.up + Flat(SpellWorld.Eye.forward) * 4);
            float before = Hp(foe);
            var health = LocalPlayer.Vitals.Health;
            health.SetCurrentValue(health.GetMax() * 0.5f);
            float hp0 = health._currentValue;
            double taken = Stat(StatIds.AllDamageTaken), move = Stat(StatIds.MovementSpeed);
            if (!Ok("spell.sun_flare")) return;
            Advance(2f);
            SelfTest.Check("spells2: Sun Flare waits out its warm-up", Math.Abs(Hp(foe) - before) < 0.01f && Math.Abs(health._currentValue - hp0) < 0.01f, $"hp {before:0.#}->{Hp(foe):0.#}");
            Advance(1.5f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Sun Flare burns and slows what stands in it", Hp(foe) < before && rt.Combat.SpeedMultiplier < 0.6, $"hp {before:0.#}->{Hp(foe):0.#}, speed x{rt.Combat.SpeedMultiplier:0.##}");
            SelfTest.Check("spells2: Sun Flare heals, speeds up and shields the player inside", health._currentValue > hp0 + 5 && Stat(StatIds.MovementSpeed) > move * 1.3
                && Stat(StatIds.AllDamageTaken) < taken * 0.51, $"health {hp0:0.#}->{health._currentValue:0.#}, move x{Stat(StatIds.MovementSpeed) / move:0.##}, taken x{Stat(StatIds.AllDamageTaken) / taken:0.##}");
        });
        Case("black hole", () =>
        {
            var center = SpellWorld.AimGround((float)SpellTuning.BlackHoleRange) + Vector3.up * 1.5f;
            var foe = Put(Foe(), center + Flat(SpellWorld.Eye.right) * 9);
            var second = Put(Foe(), center + Flat(SpellWorld.Eye.right) * -9);
            float d0 = Dist(foe, center), before = Hp(foe), secondD0 = Dist(second, center);
            if (!Ok("spell.black_hole")) return;
            Advance(0.6f);
            SelfTest.Check("spells2: Black Hole's catch radius starts small", Hp(foe) >= before && Dist(foe, center) > d0 - 0.5f, $"{d0:0.##} m -> {Dist(foe, center):0.##} m");
            Advance(3);
            float d1 = Dist(foe, center);
            SelfTest.Check("spells2: Black Hole damages", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: Black Hole pulls enemies in", d1 < d0 - 2, $"{d0:0.##} m -> {d1:0.##} m");
            SelfTest.Check("spells2: Black Hole's second enemy is caught too", Dist(second, center) < secondD0 - 2, $"{secondD0:0.##} m -> {Dist(second, center):0.##} m");
        });
        Case("cataclysm", () =>
        {
            var at = Feet() + Flat(SpellWorld.Eye.forward) * 15 + Vector3.up;
            var foe = Put(Foe(), at);
            float before = Hp(foe);
            double move = Stat(StatIds.MovementSpeed);
            if (!Ok("spell.cataclysm")) return;
            SelfTest.Check("spells2: Cataclysm slows the caster", Stat(StatIds.MovementSpeed) < move * 0.9, $"{move:0.##}->{Stat(StatIds.MovementSpeed):0.##}");
            Advance(1.5f);
            SelfTest.Check("spells2: Cataclysm warms up first", Math.Abs(Hp(foe) - before) < 0.01f);
            Advance(2f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Cataclysm (fire) damages, slows and ignites out to its 18.5 m radius", Hp(foe) < before && AliveOr(rt, () => rt.Combat.SpeedMultiplier < 0.7 && rt.Combat.DotCount > 0),
                $"{before:0.#} -> {Hp(foe):0.#}, speed x{rt.Combat.SpeedMultiplier:0.##}, dots {rt.Combat.DotCount}");
            float fireLoss = before - Hp(foe);
            SpellWorld.Reset();
            Pc.Stats.SetSource("test.arcane", new[] { new StatModifier(PerkStatIds.CataclysmArcane, EffectOp.Enable, 1) });
            rt.TestActive = false;
            var foe2 = Put(Foe(), at);
            var rt2 = Rt(foe2)!;
            float before2 = Hp(foe2);
            CastSpell("spell.cataclysm");
            Advance(3.5f);
            SelfTest.Check("spells2: arcane Cataclysm stops, weakens and hits harder", AliveOr(rt2, () => rt2.Combat.Stunned && rt2.Combat.HasWeaken) && before2 - Hp(foe2) >= fireLoss,
                $"lost {before2 - Hp(foe2):0.#} vs fire {fireLoss:0.#}");
            Pc.Stats.RemoveSource("test.arcane");
        });
        Case("snow storm", () =>
        {
            var at = SpellWorld.AimGround(30);
            var foe = Put(Foe(), at + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.snow_storm")) return;
            Advance(1.5f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Snow Storm damages and slows", Hp(foe) < before && rt.Combat.SpeedMultiplier < 0.9, $"hp {before:0.#}->{Hp(foe):0.#}, speed ×{rt.Combat.SpeedMultiplier:0.##}");
        });
        Case("ball lightning", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 8 + Vector3.up * 1.5f);
            float before = Hp(foe);
            if (!Ok("spell.ball_lightning")) return;
            Advance(2f);
            SelfTest.Check("spells2: Ball Lightning stops on touching an enemy and has not exploded yet", Math.Abs(Hp(foe) - before) < 0.01f && SpellWorld.ZoneCount == 1);
            Advance(SpellTuning.BallLightningFuse);
            SelfTest.Check("spells2: Ball Lightning explodes after its fuse", Hp(foe) < before - 100 && SpellWorld.ZoneCount == 0,
                $"{before:0.#} -> {Hp(foe):0.#}");
        });
        Case("portal", () =>
        {
            var origin = LocalPlayer.Transform.position;
            if (!Ok("spell.portal")) return;
            SelfTest.Check("spells2: Portal opens a pair", SpellPortals.Count == 1);
            var (entrance, exit) = SpellPortals.LastOpened;
            SpellCaster.TeleportPlayer(entrance);
            Advance(0.8f);
            float toExit = Vector3.Distance(LocalPlayer.Transform.position, exit);
            SelfTest.Check("spells2: Portal carries the player to the other gate", toExit < 3, $"{toExit:0.##} m from the exit, gates {Vector3.Distance(entrance, exit):0.#} m apart");
            SpellCaster.TeleportPlayer(origin);
        });

        // ---- bursts and melee
        Case("wide reach", () =>
        {
            var foe = Put(Foe(), SpellWorld.Eye.position + SpellWorld.Eye.forward * 3);
            float before = Hp(foe);
            if (!Ok("spell.wide_reach")) return;
            SelfTest.Check("spells2: Wide Reach is a pickup and deals no damage (COS-85)", Math.Abs(Hp(foe) - before) < 0.01, $"{before:0.#} -> {Hp(foe):0.#}");
        });
        Case("bash", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 2.5f + Vector3.up);
            float before = Hp(foe);
            float d0 = Vector3.Distance(Rt(foe)!.Actor!.transform.position, LocalPlayer.Transform.position);
            if (!Ok("spell.bash")) return;
            var rt = Rt(foe)!;
            Advance(0.2f);
            float d1 = Vector3.Distance(rt.Actor!.transform.position, LocalPlayer.Transform.position);
            SelfTest.Check("spells2: Bash damages, weakens and slows", Hp(foe) < before && rt.Combat.HasWeaken && rt.Combat.HasSlow, $"{before:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: Bash knocks the enemy back", d1 > d0 + 1, $"{d0:0.##} m -> {d1:0.##} m");
        });
        Case("snap freeze", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 8 + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.snap_freeze")) return;
            Advance(0.2f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Snap Freeze damages and stops the enemy completely", Hp(foe) < before && rt.Combat.Stunned && rt.Combat.HasSlow, $"hp {before:0.#}->{Hp(foe):0.#}, speed x{rt.Combat.SpeedMultiplier:0.##}");
            Advance(SpellTuning.SnapFreezeStop + 0.1);
            SelfTest.Check("spells2: Snap Freeze then slows to x0.2", !rt.Combat.Stunned && rt.Combat.SpeedMultiplier < 0.25, $"speed x{rt.Combat.SpeedMultiplier:0.##}");
        });
        Case("roaring cheeks", () =>
        {
            var back = LocalPlayer.Transform.position - Flat(SpellWorld.Eye.forward) * 15 + Vector3.up;
            var foe = Put(Foe(), back);
            float before = Hp(foe);
            double move = Stat(StatIds.MovementSpeed);
            if (!Ok("spell.roaring_cheeks")) return;
            SelfTest.Check("spells2: Roaring Cheeks winds up with the caster slowed", Stat(StatIds.MovementSpeed) < move * 0.5 && Math.Abs(Hp(foe) - before) < 0.01f, $"move x{Stat(StatIds.MovementSpeed) / move:0.##}");
            Advance(SpellTuning.RoarWindup - 0.5, 0.5f);
            SelfTest.Check("spells2: Roaring Cheeks has not fired during the wind-up", Math.Abs(Hp(foe) - before) < 0.01f);
            Advance(1f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Roaring Cheeks hits behind the caster with a DoT and slow", Hp(foe) < before && AliveOr(rt, () => rt.Combat.SpeedMultiplier < 0.9 && rt.Combat.DotCount > 0), $"hp {before:0.#}->{Hp(foe):0.#}, speed x{rt.Combat.SpeedMultiplier:0.##}");
        });

        // ---- projectiles
        Case("firebolt", () =>
        {
            var foe = Put(Foe(), OnRay(10));
            float before = Hp(foe);
            if (!Ok("spell.firebolt")) return;
            SelfTest.Check("spells2: Firebolt is in flight", SpellWorld.BoltCount == 1);
            Advance(0.6f);
            SelfTest.Check("spells2: Firebolt hits and sets the target burning", Hp(foe) < before && Rt(foe)!.Combat.DotCount > 0, $"{before:0.#} -> {Hp(foe):0.#}, dots {Rt(foe)!.Combat.DotCount}");
        });
        Case("magic arrow", () =>
        {
            var foe = Put(Foe(), OnRay(25));
            float before = Hp(foe);
            var behind = Put(Foe(), OnRay(40));
            float beforeBehind = Hp(behind);
            if (!Ok("spell.magic_arrow")) return;
            Advance(1.2f);
            SelfTest.Check("spells2: Magic Arrow hits and slows", Hp(foe) < before && AliveOr(Rt(foe)!, () => Rt(foe)!.Combat.HasSlow && Rt(foe)!.Combat.SpeedMultiplier < 0.65), $"{before:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: Magic Arrow passes through to the enemy behind", Hp(behind) < beforeBehind, $"{beforeBehind:0.#} -> {Hp(behind):0.#}");
        });
        Case("seeking arrow", () =>
        {
            var eye = SpellWorld.Eye;
            var dir = Quaternion.AngleAxis(30, Vector3.up) * eye.forward;
            var foe = Put(Foe(), eye.position + dir * 3);
            float before = Hp(foe);
            if (!Ok("spell.seeking_arrow")) return;
            Advance(2);
            SelfTest.Check("spells2: Seeking Arrow bends to its target and slows it", Hp(foe) < before && Rt(foe)!.Combat.HasSlow, $"{before:0.#} -> {Hp(foe):0.#}");
        });
        Case("blood infused arrow", () =>
        {
            var foe = Put(Foe(), OnRay(15));
            float before = Hp(foe);
            var health = LocalPlayer.Vitals.Health;
            if (!Ok("spell.blood_infused_arrow")) return;
            SelfTest.Check("spells2: Blood Infused Arrow costs health", health._currentValue < health.GetMax() * 0.6f, $"{health._currentValue:0.#} / {health.GetMax():0.#}");
            Advance(0.6f);
            SelfTest.Check("spells2: Blood Infused Arrow hits hard", Hp(foe) < before - 50, $"{before:0.#} -> {Hp(foe):0.#}");
        });

        // ---- toggles
        Case("black flame", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 4 + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.black_flame")) return;
            Advance(2.2f);
            SelfTest.Check("spells2: Black Flame burns nearby enemies each second", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: switching it off starts the cooldown", SpellCaster.Cast(0) == SpellCastOutcome.Stopped && !Pc.Spells.IsReady(0));
        });
        Case("multishot", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 2 + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.multishot")) return;
            Advance(1.6f);
            SelfTest.Check("spells2: Multishot fires at the nearest enemy while on", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
        });

        SelfTest.Check("spells2: casts raised the cast event", _castEvents >= 20, _castEvents.ToString());
        SpellWorld.Reset();
    }
}
