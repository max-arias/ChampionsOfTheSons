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
        // COS-178: objects are destroyed at the end of a frame, so the scene is counted a moment after the leak case ran.
        SelfTest.Add(T0 + 20, "spells2: nothing left in the scene", () =>
        {
            int after = LeftoverRoots();
            SelfTest.Check("spells2: no cots effect object is left in the scene", after <= _rootsBefore, $"{_rootsBefore} -> {after}: {string.Join(", ", LeftoverNames().Take(8))}");
        });
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

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z).normalized;

    /// <summary>Scene roots our effects create, not riding on the player (COS-178).</summary>
    private static IEnumerable<string> LeftoverNames() =>
        UnityEngine.Object.FindObjectsOfType<Transform>()
            .Where(t => t.parent == null && (t.name.StartsWith("cots-fx-", StringComparison.Ordinal) || t.name.StartsWith("cots-flame-ring", StringComparison.Ordinal)))
            .Select(t => t.name);

    private static int LeftoverRoots() => LeftoverNames().Count();

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

    private static int _rootsBefore;

    private static void RunAll()
    {
        // COS-183: spell damage grows with the caster's level; the cases' small test numbers hold at the level-1 scale (an earlier phase
        // can leave the character at a high level, where every spell kills the test enemy before its slow or burn can be read).
        // Only the scale is neutralised: changing the level would refund perks and spells that later checks rely on.
        Pc.Stats.SetSource("spell_level", new[] { new StatModifier(StatIds.SpellBaseScale, EffectOp.Mult, 1) });
        var hp = LocalPlayer.Vitals.Health;
        hp.SetCurrentValue(hp.GetMax());
        SpellCaster.Casted += _ => _castEvents++;
        SelfTest.Check("spells2: every spell in the content has an effect", ModContent.Db.Spells.Keys.All(SpellCaster.IsImplemented),
            string.Join(", ", ModContent.Db.Spells.Keys.Where(k => !SpellCaster.IsImplemented(k))));

        // ---- player buffs
        Case("war cry", () =>
        {
            double dmg = Stat(StatIds.AllDamage), atk = Stat(StatIds.AttackSpeed);
            if (!Ok("spell.war_cry")) return;
            SelfTest.Check("spells2: War Cry raises damage and attack speed", Stat(StatIds.AllDamage) > dmg * 1.19 && Stat(StatIds.AttackSpeed) > atk * 1.19,
                $"damage {dmg:0.##}->{Stat(StatIds.AllDamage):0.##}, attack speed {atk:0.##}->{Stat(StatIds.AttackSpeed):0.##}");
            Advance(SpellTuning.WarCryDuration + 1);
            SelfTest.Check("spells2: War Cry ends", Math.Abs(Stat(StatIds.AllDamage) - dmg) < 1e-6);
        });
        Case("berserk", () =>
        {
            double dmg = Stat(StatIds.AllDamage), move = Stat(StatIds.MovementSpeed);
            if (!Ok("spell.berserk")) return;
            SelfTest.Check("spells2: Berserk raises damage and movement speed", Stat(StatIds.AllDamage) > dmg * 1.29 && Stat(StatIds.MovementSpeed) > move * 1.34,
                $"damage {Stat(StatIds.AllDamage):0.##}, move {Stat(StatIds.MovementSpeed):0.##}");
            Advance(31);
            SelfTest.Check("spells2: Berserk ends", Math.Abs(Stat(StatIds.MovementSpeed) - move) < 1e-6);
        });
        Case("golden skin", () =>
        {
            double armor = Stat(StatIds.Armor);
            if (!Ok("spell.golden_skin")) return;
            SelfTest.Check("spells2: Golden Skin adds armor", Stat(StatIds.Armor) >= armor + SpellTuning.GoldenSkinArmor - 0.5, $"armor {armor:0}->{Stat(StatIds.Armor):0}");
        });
        Case("focus", () =>
        {
            var foe = Put(Foe(), OnRay(8));
            if (!Ok("spell.focus")) return;
            SelfTest.Check("spells2: Focus speeds attacks up", Stat(StatIds.AttackSpeed) >= 1.29, $"{Stat(StatIds.AttackSpeed):0.##}");
            // Passive (COS-84): the spell sits in the loadout, so a real shot stores a bonus for the next one and a body shot hastens the player.
            PerkHooks.ShotTracker.RecordKind(RangedKind.Bow);
            TestKit.Hit(foe, 5f, ImpactType.Projectile);
            SelfTest.Check("spells2: Focus: a body shot stores a bonus and gives attack speed", SpellHits.Focus.Has && SpellWorld.Buffs.Has("focus_haste"),
                $"stored {SpellHits.Focus.Value:0.##}");
            TestKit.Hit(foe, 5f, ImpactType.Projectile);
            SelfTest.Check("spells2: Focus: the next shot spends the stored bonus and stores its own", SpellHits.Focus.Has && Math.Abs(SpellHits.Focus.Value - SpellTuning.FocusBonus(Pc.Stats, false)) < 1e-6);
        });
        Case("frenzy", () =>
        {
            var foe = Put(Foe(), OnRay(8));
            if (!Ok("spell.frenzy")) return;
            double dmg = Stat(StatIds.AllDamage);
            // Passive (COS-84): real attacks stack while the spell is in the loadout.
            for (int i = 0; i < 3; i++) TestKit.Hit(foe, 1f);
            SelfTest.Check("spells2: Frenzy stacks on hits", SpellWorld.Frenzy.Stacks >= 3 && Stat(StatIds.AllDamage) > dmg * 1.2,
                $"stacks {SpellWorld.Frenzy.Stacks}, damage ×{Stat(StatIds.AllDamage) / dmg:0.###}");
            Advance(SpellTuning.FrenzyStackSeconds + 1);
            SelfTest.Check("spells2: Frenzy stacks end", SpellWorld.Frenzy.Stacks == 0 && Math.Abs(Stat(StatIds.AllDamage) - dmg) < 1e-6);
            // Active: the cast spends the stacks for energy regeneration.
            for (int i = 0; i < 3; i++) TestKit.Hit(foe, 1f);
            CastSpell("spell.frenzy");
            SelfTest.Check("spells2: casting Frenzy spends the stacks for energy regeneration", SpellWorld.Frenzy.Stacks == 0 && SpellWorld.Buffs.Has("frenzy_burst") && Math.Abs(Stat(StatIds.AllDamage) - dmg) < 1e-6);
        });
        Case("taunt", () =>
        {
            // Aimed (COS-84): enemies within the radius of the aim point, not of the caster.
            var foe = Put(Foe(), SpellWorld.AimGround((float)SpellTuning.TauntAimRange) + Vector3.up + Flat(SpellWorld.Eye.right) * 20);
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
        });

        // ---- parry and shield (incoming damage)
        Case("parry", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 2 + Vector3.up);
            var far = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 12 + Vector3.up);
            float hpBefore = Hp(foe), farBefore = Hp(far);
            var health = LocalPlayer.Vitals.Health;
            health.SetCurrentValue(health.GetMax() * 0.5f);
            float playerBefore = health._currentValue;
            // COS-171: the cast itself strikes; no blow, block or weapon needed.
            if (!Ok("spell.parry")) return;
            SelfTest.Check("spells2: Parry strikes around the player when cast", Hp(foe) < hpBefore, $"{hpBefore:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: Parry leaves enemies out of range alone", Math.Abs(Hp(far) - farBefore) < 0.01, $"{farBefore:0.#} -> {Hp(far):0.#}");
            SelfTest.Check("spells2: Parry heals, buffs and gives stun immunity",
                health._currentValue > playerBefore + 5 && SpellWorld.Buffs.Has("parry") && PlayerDebuffs.Buffs.Has(PlayerDebuffs.StunImmune), $"health {health._currentValue:0.#}");
            float healed = health._currentValue;
            LocalPlayer.Stats.Hit(20f);
            SelfTest.Check("spells2: Parry negates the next blow", health._currentValue >= healed - 0.01f, $"{healed:0.#} -> {health._currentValue:0.#}");
            float after = health._currentValue;
            LocalPlayer.Stats.Hit(20f);
            SelfTest.Check("spells2: only one blow is negated", health._currentValue < after - 1, $"{after:0.#} -> {health._currentValue:0.#}");
        });
        Case("sustain shield", () =>
        {
            if (!Ok("spell.sustain_shield")) return;
            SelfTest.Check("spells2: shield is channeled", Pc.Spells.IsActive(0));
            double energy = SpellCaster.Energy;
            Advance(3);
            SelfTest.Check("spells2: shield fills while channeled", SpellWorld.Shield.Amount > 1, $"{SpellWorld.Shield.Amount:0.##}");
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
            SelfTest.Check("spells2: Healing Dome heals the player inside", health._currentValue > before + 3, $"{before:0.#} -> {health._currentValue:0.#}");
            // Perks that act while the player is inside (the dome lasts 10 s: checked at 2-3 s): damage up, damage taken down, stun and debuff immunity.
            SelfTest.Check("spells2: Healing Dome perks give nothing without the perks", !SpellWorld.Buffs.Has("healing_dome") && !PlayerDebuffs.Buffs.Has(PlayerDebuffs.DebuffImmune));
            Pc.Stats.SetSource("test.dome", new[]
            {
                new StatModifier("spell_healing_dome_damage_increase", EffectOp.Add, 0.2),
                new StatModifier("spell_healing_dome_damage_resistance", EffectOp.Add, 0.7),
                new StatModifier("spell_healing_dome_gives_immunity", EffectOp.Enable, 1),
            });
            Advance(1);
            SelfTest.Check("spells2: Healing Dome gives damage up and less damage taken while inside",
                SpellWorld.Buffs.Has("healing_dome") && Stat(StatIds.AllDamageTaken) < 0.5, $"taken x{Stat(StatIds.AllDamageTaken):0.##}");
            SelfTest.Check("spells2: Healing Dome gives stun and debuff immunity while inside",
                PlayerDebuffs.Buffs.Has(PlayerDebuffs.StunImmune) && PlayerDebuffs.Buffs.Has(PlayerDebuffs.DebuffImmune));
            Advance(10);
            Pc.Stats.RemoveSource("test.dome");
            SelfTest.Check("spells2: Healing Dome ends", SpellWorld.ZoneCount == 0);
        });
        Case("sun flare", () =>
        {
            var at = SpellWorld.AimGround(30);
            var foe = Put(Foe(), at + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.sun_flare")) return;
            Advance(1.2f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Sun Flare burns and slows what stands in it", Hp(foe) < before && rt.Combat.SpeedMultiplier < 0.6, $"hp {before:0.#}->{Hp(foe):0.#}, speed ×{rt.Combat.SpeedMultiplier:0.##}");
        });
        Case("black hole", () =>
        {
            var center = SpellWorld.AimGround(40) + Vector3.up * 1.5f;
            var foe = Put(Foe(), center + Flat(SpellWorld.Eye.right) * 9);
            float d0 = Dist(foe, center), before = Hp(foe);
            if (!Ok("spell.black_hole")) return;
            Advance(0.5f); // the catch radius is still small: an enemy 9 m out is not caught or pulled yet
            SelfTest.Check("spells2: Black Hole catches nothing outside its growing radius", Hp(foe) == before && Dist(foe, center) > d0 - 0.5f, $"{d0:0.##} m -> {Dist(foe, center):0.##} m");
            Advance(2.5f);
            float d1 = Dist(foe, center);
            SelfTest.Check("spells2: Black Hole damages", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: Black Hole pulls enemies in", d1 < d0 - 2, $"{d0:0.##} m -> {d1:0.##} m");
        });
        Case("cataclysm", () =>
        {
            var at = SpellWorld.AimGround(50);
            var foe = Put(Foe(), at + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.cataclysm")) return;
            Advance(2);
            SelfTest.Check("spells2: Cataclysm damages over time", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
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
            Advance(3.2f);
            SelfTest.Check("spells2: Ball Lightning zaps what it passes", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
        });
        Case("portal", () =>
        {
            var origin = LocalPlayer.Transform.position;
            // COS-84: one cast places one gate; the second cast links them and starts the timer.
            if (!Ok("spell.portal")) return;
            SelfTest.Check("spells2: the first Portal cast places one waiting gate", SpellPortals.Count == 0 && SpellPortals.HasPending);
            SpellCaster.TeleportPlayer(origin + Flat(SpellWorld.Eye.forward) * 14);
            if (!Ok("spell.portal")) return;
            SelfTest.Check("spells2: the second Portal cast links the pair", SpellPortals.Count == 1 && !SpellPortals.HasPending);
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
            // COS-171: Bash has no passive on weapon hits any more.
            var other = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 6 + Vector3.up);
            TestKit.Hit(other, 5f);
            var rt2 = Rt(other)!;
            SelfTest.Check("spells2: a melee hit with Bash slotted does not weaken or slow", !rt2.Combat.HasWeaken && !rt2.Combat.HasSlow);
        });
        Case("snap freeze", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 8 + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.snap_freeze")) return;
            Advance(0.2f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Snap Freeze damages and freezes", Hp(foe) < before && rt.Combat.SpeedMultiplier < 0.3, $"hp {before:0.#}->{Hp(foe):0.#}, speed ×{rt.Combat.SpeedMultiplier:0.##}");
        });
        Case("roaring cheeks", () =>
        {
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 10 + Vector3.up);
            float before = Hp(foe);
            if (!Ok("spell.roaring_cheeks")) return;
            Advance(0.2f);
            var rt = Rt(foe)!;
            SelfTest.Check("spells2: Roaring Cheeks damages and slows", Hp(foe) < before && rt.Combat.SpeedMultiplier < 0.9, $"hp {before:0.#}->{Hp(foe):0.#}, speed ×{rt.Combat.SpeedMultiplier:0.##}");
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
            var behind = Put(Foe(), OnRay(45));
            float before = Hp(foe), beforeBehind = Hp(behind);
            if (!Ok("spell.magic_arrow")) return;
            Advance(0.8f);
            SelfTest.Check("spells2: Magic Arrow hits", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
            SelfTest.Check("spells2: Magic Arrow pierces on to the enemy behind", Hp(behind) < beforeBehind, $"{beforeBehind:0.#} -> {Hp(behind):0.#}");
        });
        Case("seeking arrow", () =>
        {
            // COS-171: its own homing bolt, off to the side of the view (it turns to find the target).
            var eye = SpellWorld.Eye;
            var dir = Quaternion.AngleAxis(30, Vector3.up) * eye.forward;
            var foe = Put(Foe(), eye.position + dir * 8);
            float before = Hp(foe);
            if (!Ok("spell.seeking_arrow")) return;
            SelfTest.Check("spells2: Seeking Arrow fires its own bolt", SpellWorld.BoltCount == 1);
            Advance(1.5f);
            SelfTest.Check("spells2: the seeking bolt finds the target, hurts and slows it", Hp(foe) < before && Rt(foe)!.Combat.HasSlow, $"{before:0.#} -> {Hp(foe):0.#}");
        });
        Case("blood infused arrow", () =>
        {
            var foe = Put(Foe(), OnRay(15));
            var health = LocalPlayer.Vitals.Health;
            float before = Hp(foe);
            if (!Ok("spell.blood_infused_arrow")) return;
            SelfTest.Check("spells2: Blood Infused Arrow costs health", health._currentValue < health.GetMax() * 0.6f, $"{health._currentValue:0.#} / {health.GetMax():0.#}");
            SelfTest.Check("spells2: it fires its own bolt", SpellWorld.BoltCount >= 1);
            Advance(0.6f);
            SelfTest.Check("spells2: the blood bolt hits hard", Hp(foe) < before - 50, $"{before:0.#} -> {Hp(foe):0.#}");
        });

        // ---- toggles
        Case("black flame", () =>
        {
            // COS-171: an aura; no weapon hit needed.
            var foe = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 4 + Vector3.up);
            var far = Put(Foe(), LocalPlayer.Transform.position + Flat(SpellWorld.Eye.forward) * 14 + Vector3.up);
            float before = Hp(foe), farBefore = Hp(far);
            if (!Ok("spell.black_flame")) return;
            Advance(2.2f);
            SelfTest.Check("spells2: Black Flame burns an enemy in its radius", Hp(foe) < before, $"{before:0.#} -> {Hp(foe):0.#}");
            TestKit.Screenshot("spells2-black-flame-ring");
            SelfTest.Check("spells2: Black Flame stands a ring of flames on the ground (COS-179)", !Visuals.FxKit.Available || SpellEffects.FlameRingFlames == Visuals.FlameRing.Count,
                $"{SpellEffects.FlameRingFlames} flames");
            SelfTest.Check("spells2: Black Flame leaves an enemy outside its radius alone", Math.Abs(Hp(far) - farBefore) < 0.01, $"{farBefore:0.#} -> {Hp(far):0.#}");
            SelfTest.Check("spells2: switching it off starts the cooldown", SpellCaster.Cast(0) == SpellCastOutcome.Stopped && !Pc.Spells.IsReady(0));
        });
        // COS-178: every spell, cast back to back with nothing reset between, leaves nothing behind once its time is up.
        Case("nothing lingers", () =>
        {
            int before = SpellFx.LiveCount, roots = LeftoverRoots();
            foreach (var id in ModContent.Db.Spells.Keys.Where(SpellCaster.IsImplemented).OrderBy(i => i, StringComparer.Ordinal))
            {
                if (id is "spell.flicker_strike" or "spell.sustain_shield") continue; // a run with targets / a held channel
                CastSpell(id);
                for (int s = 0; s < SpellBook.Slots; s++)
                    if (Pc.Spells.IsActive(s)) { Pc.Spells.Stop(s); SpellEffects.Stopped(s); } // toggles off again
            }
            Advance(200, 0.25f);
            SelfTest.Check("spells2: no spell effect is still alive after its time", SpellFx.LiveCount <= before && SpellWorld.ZoneCount == 0 && SpellWorld.BoltCount == 0
                && SpellPortals.Count == 0 && !SpellPortals.HasPending, $"fx {before} -> {SpellFx.LiveCount} [{SpellFx.LiveNames}], zones {SpellWorld.ZoneCount}, bolts {SpellWorld.BoltCount}, portals {SpellPortals.Count}, pending {SpellPortals.HasPending}");
            _rootsBefore = roots;
        });
        Case("multishot", () =>
        {
            if (!Ok("spell.multishot")) return;
            // The volley itself needs the game's weapon (RangedWeapon.Shoot); the falloff of its arrows is a real-hit rule.
            SpellHits.QueueVolleyForTest(2);
            var foe = Put(Foe(), OnRay(8));
            PerkHooks.ShotTracker.RecordKind(RangedKind.Bow);
            SelfTest.Check("spells2: Multishot queues the volley's arrows", SpellHits.VolleyPending == 3);
            TestKit.Hit(foe, 5f, ImpactType.Projectile);
            TestKit.Hit(foe, 5f, ImpactType.Projectile);
            SelfTest.Check("spells2: each arrow that lands takes its place in the volley", SpellHits.VolleyPending == 1, $"{SpellHits.VolleyPending} left");
        });

        SelfTest.Check("spells2: casts raised the cast event", _castEvents >= 20, _castEvents.ToString());
        Pc.Stats.SetSource("spell_level", new[] { new StatModifier(StatIds.SpellBaseScale, EffectOp.Mult, SpellTuning.SpellLevelScale(Pc.Progress.Level)) });
        SpellWorld.Reset();
    }
}
