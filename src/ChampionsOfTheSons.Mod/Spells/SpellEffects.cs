using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using TheForest.Utils;
using UnityEngine;
using static ChampionsOfTheSons.Mod.Spells.SpellWorld;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// What each of the 26 spells besides Blink does when cast. No spell needs a weapon hit (COS-171, Max 2026-11-23): damage and area
/// spells act on their own when cast or while they run; only Frenzy, Focus, Berserk and Multishot buff the player's own attacks. Numbers come from <see cref="SpellTuning"/> and the character's
/// <c>spell_*</c> stats, so perks that edit a spell change it here. Visuals are placeholders (<see cref="SpellFx"/>).
/// Enemy damage goes through <see cref="SpellWorld.Strike"/>, the bridge's forced-hit path, so armor, shields and caps apply.
/// </summary>
internal static class SpellEffects
{
    public static readonly Dictionary<string, Func<bool>> Table = new()
    {
        ["spell.war_cry"] = WarCry,
        ["spell.berserk"] = Berserk,
        ["spell.golden_skin"] = GoldenSkin,
        ["spell.focus"] = Focus,
        ["spell.frenzy"] = FrenzyCast,
        ["spell.taunt"] = Taunt,
        ["spell.healing_dome"] = HealingDome,
        ["spell.sun_flare"] = SunFlare,
        ["spell.black_hole"] = BlackHole,
        ["spell.sustain_shield"] = StartShield,
        ["spell.wide_reach"] = WideReach,
        ["spell.black_flame"] = StartBlackFlame,
        ["spell.portal"] = Portal,
        ["spell.magic_arrow"] = MagicArrow,
        ["spell.multishot"] = StartMultishot,
        ["spell.purge"] = Purge,
        ["spell.snap_freeze"] = SnapFreeze,
        ["spell.ball_lightning"] = BallLightning,
        ["spell.bash"] = Bash,
        ["spell.seeking_arrow"] = SeekingArrow,
        ["spell.cataclysm"] = Cataclysm,
        ["spell.blood_infused_arrow"] = BloodInfusedArrow,
        ["spell.roaring_cheeks"] = RoaringCheeks,
        ["spell.firebolt"] = Firebolt,
        ["spell.snow_storm"] = SnowStorm,
        ["spell.parry"] = ParryCast,
        ["spell.flicker_strike"] = () => FlickerRun.Start(FlickerRun.Reachable()),
    };

    private static GameObject? _shieldFx, _multishotFx;
    private static Visuals.FlameRing? _flameRing;
    private static float _flameTimer;
    private static double _warCryArmor;

    public static void ResetState()
    {
        _shieldFx = _multishotFx = null;
        _flameRing?.Destroy();
        _flameRing = null;
        _flameTimer = 0;
    }

    // ---------------------------------------------------------------- player buffs

    private static bool WarCry()
    {
        var mods = SpellTuning.WarCry(Stats).ToList();
        // Iron horns (COS-88): War Cry also gives armor (10% of yours, measured before the buff); the greater horn also damage resistance.
        if (Stats.IsOn(PerkStatIds.WarCryArmor))
        {
            if (!Buffs.Has("war_cry")) _warCryArmor = Stats.Get(StatIds.Armor) * PerkRules.WarCryArmorShare;
            mods.Add(new StatModifier(StatIds.Armor, EffectOp.Add, _warCryArmor));
        }
        if (Stats.IsOn(PerkStatIds.WarCryResistance)) mods.Add(new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, PerkRules.WarCryDamageTaken));
        Buffs.Apply("war_cry", SpellTuning.WarCryDuration, mods);
        // Teammates in range get the damage and attack speed part (COS-23); the armor perks stay the caster's own.
        Net.CoopExtras.SendTeamSpell("spell.war_cry", Feet(), TeamSpells.WarCryRadius, (float)SpellTuning.WarCryDuration,
            Math.Max(1, Stats.Get("spell_war_cry_damage")), Math.Max(1, Stats.Get("spell_war_cry_atk_speed")));
        SpellFx.Disc(Feet(), (float)TeamSpells.WarCryRadius, SpellFx.Holy, 1.2f, LocalPlayer.Transform, SpellFx.Look.Ring);
        return true;
    }

    private static bool Berserk()
    {
        Buffs.Apply("berserk", S("spell_berserk_duration"), SpellTuning.Berserk(Stats));
        return true;
    }

    private static bool GoldenSkin()
    {
        Buffs.Apply("golden_skin", SpellTuning.GoldenSkinDuration, SpellTuning.GoldenSkin(Stats));
        return true;
    }

    private static bool Focus()
    {
        Buffs.Apply("focus", S("spell_focus_on_atk_speed_duration") > 0 ? S("spell_focus_on_atk_speed_duration") : SpellTuning.FocusDuration, SpellTuning.Focus(Stats));
        return true;
    }

    /// <summary>Frenzy's cast (the passive stacks build from real hits, <see cref="SpellHits"/>): spends the stacks for energy regeneration
    /// over <see cref="SpellTuning.FrenzyDuration"/> s, plus the crit chance items give while it runs.</summary>
    private static bool FrenzyCast()
    {
        int stacks = Frenzy.Spend();
        Buffs.Remove("frenzy");
        var mods = new List<StatModifier>();
        if (stacks > 0) mods.Add(new StatModifier(CombatStatIds.EnergyPerSecond, EffectOp.Add, SpellTuning.FrenzyBurst(stacks, SpellCaster.MaxEnergy)));
        double crit = Stats.Get(PerkStatIds.FrenzyCrit);
        if (crit > 0) mods.Add(new StatModifier(StatIds.CritChance, EffectOp.Add, crit));
        if (mods.Count > 0) Buffs.Apply("frenzy_burst", SpellTuning.FrenzyDuration, mods);
        return true;
    }

    private static bool Taunt()
    {
        Buffs.Apply("taunt", SpellTuning.TauntDuration, SpellTuning.Taunt());
        // Aimed (cast on release): everything within the radius of the point. The game has no safe way to force an enemy's target
        // (COS-84 spike), so they rush the caster as before, and Gathering Call (spell_taunt_pull_enemies_in) drags them to the point.
        var at = AimGround((float)SpellTuning.TauntAimRange);
        float speed = S("spell_taunt_speed_change");
        // Falter (perk): the taunted slow down instead of rushing the taunter.
        if (Has(PerkEffectIds.Falter)) speed = (float)PerkTuning.FalterSpeed;
        bool pull = Stats.IsOn("spell_taunt_pull_enemies_in");
        foreach (var e in EnemiesWithin(at, (float)SpellTuning.TauntRadius))
        {
            e.Combat.ApplySlow("spell.taunt", speed > 0 ? speed : 2, SpellTuning.TauntDuration); // above 1: they rush the taunter
            if (pull)
            {
                var to = at - Center(e);
                to.y = 0;
                Push(e, to);
            }
        }
        SpellFx.Disc(at, (float)SpellTuning.TauntRadius, SpellFx.Blood, 0.6f);
        return true;
    }

    /// <summary>Parry (COS-171): the cast strikes everything around the player at once (no block or weapon needed), and the next
    /// blow within the window is negated.</summary>
    private static bool ParryCast()
    {
        double window = SpellTuning.ParryWindowFor(Stats);
        Parry.Cast(window);
        SpellFx.Sphere(PlayerPos + Vector3.up * 1.2f, 1.1f, SpellFx.Steel, (float)window, LocalPlayer.Transform);
        Riposte();
        return true;
    }

    /// <summary>Parry's strike: hits around the player, heals and refunds energy, buffs attack speed.</summary>
    public static void Riposte()
    {
        float radius = S("spell_parry_radius");
        foreach (var e in EnemiesWithin(PlayerPos, radius))
        {
            Strike(e, Hit(S("spell_parry_damage"), S("spell_parry_damage_scaling")));
            if (Stats.IsOn("spell_parry_ignites")) { var (ps, sec) = FireRules.Burn(Stats, S("spell_parry_damage") * 0.25, 4); e.Combat.ApplyDot("player.parry", DotKind.Fire, ps, sec); }
        }
        // A parry gives stun immunity, and (with the counter-strike perks) stores a bonus for the next melee hit.
        PlayerDebuffs.Buffs.Apply(PlayerDebuffs.StunImmune, "spell.parry", SpellTuning.ParryStunImmunitySeconds);
        if (S("spell_parry_dmg_bonus") > 0) SpellHits.ParryBonus.Store(SpellTuning.ParryStoredMult(Stats), SpellTuning.ParryStoredSeconds);
        PlayerEffects.Heal(S("spell_parry_heal"));
        PlayerEffects.AddEnergy(S("spell_parry_energy"));
        Buffs.Apply("parry", S("spell_parry_buff_duration"), new[]
        {
            new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, S("spell_parry_attack_speed"))),
            new StatModifier(StatIds.AllDamage, EffectOp.Mult, 1 + S("spell_parry_buff_damage")),
        });
        SpellFx.Disc(Feet(), Math.Max(1, radius), SpellFx.Steel, 0.6f, LocalPlayer.Transform, SpellFx.Look.Ring);
    }

    private static bool Purge()
    {
        PlayerEffects.ClearDebuffs();
        if (Stats.IsOn("spell_purge_heal")) PlayerEffects.Heal(LocalPlayer.Vitals.Health.GetMax() * SpellTuning.PurgeHealFraction);
        // Purgatory (COS-88): damage rises with missing health, measured as the Purge lands.
        var health = LocalPlayer.Vitals.Health;
        double purge = PerkRules.PurgeDamage(Stats, health.GetMax() > 0 ? health._currentValue / health.GetMax() : 1);
        if (purge > 1) Buffs.Apply("purgatory", PerkRules.PurgeSeconds, new[] { new StatModifier(StatIds.AllDamage, EffectOp.Mult, purge) });
        Net.CoopExtras.SendTeamSpell("spell.purge", Feet(), TeamSpells.PurgeRadius, 0, Stats.IsOn("spell_purge_heal") ? SpellTuning.PurgeHealFraction : 0);
        SpellFx.Disc(Feet(), 3f, SpellFx.Heal, 1f, LocalPlayer.Transform, SpellFx.Look.Glyph);
        SpellFx.Disc(Feet(), (float)TeamSpells.PurgeRadius, SpellFx.Heal, 1f, LocalPlayer.Transform, SpellFx.Look.Ring);
        return true;
    }

    // ---------------------------------------------------------------- zones

    private static bool HealingDome()
    {
        var at = Feet();
        float radius = (float)SpellTuning.HealingDomeRadius;
        float interval = 0.5f;
        AddZone(new SpellZone
        {
            Id = "healing_dome", Center = at, Radius = radius, Left = S("spell_healing_dome_duration"), Interval = interval,
            Visual = SpellFx.Disc(at, radius, SpellFx.Heal, 1e6f, look: SpellFx.Look.Dome),
            Pulse = z =>
            {
                // Teammates inside heal by the caster's dome numbers, as a share of their own max health (COS-23).
                Net.CoopExtras.SendTeamSpell("spell.healing_dome", z.Center, z.Radius, z.Interval,
                    SpellTuning.HealingDomeHeal(1, Stats), SpellTuning.HealingDomeEnergy(1, Stats));
                if (Vector3.Distance(PlayerPos, z.Center) > z.Radius) return;
                PlayerEffects.Heal(SpellTuning.HealingDomeHeal(LocalPlayer.Vitals.Health.GetMax(), Stats) * z.Interval);
                PlayerEffects.AddEnergy(SpellTuning.HealingDomeEnergy(Derived.MaxEnergy(Stats), Stats) * z.Interval); // Humming Ground (COS-94)
                // Perks that act while the player stands inside: short buffs, refreshed every pulse.
                var boost = SpellTuning.HealingDomeBoost(Stats);
                if (boost.Length > 0) Buffs.Apply("healing_dome", (float)SpellTuning.HealingDomeBuffSeconds, boost);
                if (Stats.IsOn("spell_healing_dome_gives_immunity"))
                {
                    PlayerDebuffs.Buffs.Apply(PlayerDebuffs.StunImmune, "spell.healing_dome", SpellTuning.HealingDomeBuffSeconds);
                    PlayerDebuffs.Buffs.Apply(PlayerDebuffs.DebuffImmune, "spell.healing_dome", SpellTuning.HealingDomeBuffSeconds);
                }
            },
        });
        return true;
    }

    private static bool SunFlare()
    {
        var at = AimGround(30);
        float radius = S("spell_flare_radius");
        foreach (var e in EnemiesWithin(at, radius)) Strike(e, Hit(S("spell_flare_damage"), S("spell_flare_damage_scaling")));
        AddZone(new SpellZone
        {
            Id = "sun_flare", Center = at, Radius = radius, Left = S("spell_flare_duration"), Interval = (float)SpellTuning.SunFlareTick,
            Visual = SpellFx.Disc(at, radius, SpellFx.Holy, 1e6f, look: SpellFx.Look.SunFlare),
            Pulse = z =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius)) e.Combat.ApplySlow("spell.flare", S("spell_flare_slow"), 2);
                Net.CoopExtras.SendTeamSpell("spell.sun_flare", z.Center, z.Radius, 2, S("spell_flare_heal"), S("spell_flare_boost"));
                if (Vector3.Distance(PlayerPos, z.Center) > z.Radius) return;
                PlayerEffects.Heal(S("spell_flare_heal"));
                Buffs.Apply("flare_boost", 2, SpellTuning.Speed(Math.Max(1, S("spell_flare_boost"))));
            },
        });
        SpellFx.Sphere(at + Vector3.up * 3, 1.2f, SpellFx.Holy, 1f);
        return true;
    }

    private static bool BlackHole()
    {
        var at = AimGround(40) + Vector3.up * 1.5f;
        float radius = S("spell_blackhole_radius"), pull = S("spell_blackhole_pullforce") / (float)SpellTuning.BlackHolePullDivisor;
        var core = SpellFx.Sphere(at, 1.5f, SpellFx.Dark, 1e6f, look: SpellFx.Look.BlackHole);
        var caught = new HashSet<EnemyRuntime>();
        float age = 0;
        AddZone(new SpellZone
        {
            Id = "black_hole", Center = at, Radius = radius, Left = S("spell_blackhole_duration"), Interval = (float)SpellTuning.BlackHoleTick,
            Visual = core,
            Frame = (z, dt) =>
            {
                // The catch radius grows from 0 to the full radius; whatever it caught stays caught until the hole ends.
                age += dt;
                foreach (var e in EnemiesWithin(z.Center, (float)SpellTuning.BlackHoleCatchRadius(z.Radius, age))) caught.Add(e);
                caught.RemoveWhere(e => !e.IsActive);
                foreach (var e in caught)
                {
                    var to = z.Center - Center(e);
                    float d = to.magnitude;
                    if (d < 2f) continue;
                    Push(e, to / d * Mathf.Min(pull * dt, d - 2f));
                }
            },
            Pulse = z =>
            {
                foreach (var e in caught.ToList())
                    if (e.IsActive) Strike(e, Hit(S("spell_blackhole_damage"), S("spell_blackhole_damage_scaling")));
            },
            // Last Engineer's Note: a black hole that caught enough enemies releases a Ball Lightning where it was.
            End = z =>
            {
                if (!Stats.IsOn(UniqueFlagIds.Spark) || caught.Count < UniqueTuning.SparkEnemies) return;
                var dir = Eye.forward;
                dir.y = 0;
                LaunchBallLightning(z.Center, dir.normalized);
            },
        });
        SpellFx.Disc(at - Vector3.up * 1.4f, radius, SpellFx.Dark, 0.6f);
        return true;
    }

    private static bool Cataclysm()
    {
        var at = AimGround(50);
        float radius = S("spell_cataclysm_radius");
        var color = Stats.IsOn("spell_cataclysm_arcane") ? SpellFx.Arcane : SpellFx.Fire;
        // Mysterious Robe (COS-88): the arcane vortex strikes harder and freezes what is inside.
        bool arcane = Stats.IsOn(PerkStatIds.CataclysmArcane);
        AddZone(new SpellZone
        {
            Id = "cataclysm", Center = at, Radius = radius, Left = S("spell_cataclysm_duration"), Interval = (float)SpellTuning.CataclysmTick,
            Visual = SpellFx.Column(at, radius, 14f, color, 1e6f),
            Pulse = z =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius))
                {
                    var hit = Hit(S("spell_cataclysm_damage"), S("spell_cataclysm_damage_scaling"));
                    Strike(e, arcane ? hit with { Amount = hit.Amount * PerkRules.ArcaneCataclysmDamage } : hit);
                    if (arcane && e.IsActive) e.Combat.ApplySlow("spell.cataclysm.arcane", PerkRules.ArcaneCataclysmSlow, 1);
                }
            },
        });
        return true;
    }

    private static bool SnowStorm()
    {
        var at = AimGround(30);
        double scale = SpellTuning.SnowStormScale(Stats); // max charge makes it bigger and longer
        float radius = (float)(SpellTuning.SnowStormRadius * Math.Sqrt(scale));
        float interval = Math.Max(0.1f, S("spell_snowstorm_hit_delay"));
        AddZone(new SpellZone
        {
            Id = "snow_storm", Center = at, Radius = radius, Left = (float)(SpellTuning.SnowStormSeconds * scale), Interval = interval,
            Visual = SpellFx.Disc(at, radius, SpellFx.Ice, 1e6f, look: SpellFx.Look.Snow),
            Pulse = z =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius))
                {
                    var hit = Hit(SpellTuning.SnowStormDamage, S("spell_snowstorm_dmg_scaling"));
                    Strike(e, hit with { Amount = hit.Amount * S("spell_snowstorm_damage_mult") });
                    e.Combat.ApplySlow("spell.snow_storm", SpellTuning.SnowStormSlow, 1.5);
                    if (Stats.IsOn("spell_snowstorm_pull_enemies_in")) Push(e, (z.Center - Center(e)).normalized * 0.4f);
                }
            },
        });
        return true;
    }

    private static bool BallLightning()
    {
        var dir = Eye.forward;
        dir.y = 0;
        dir.Normalize();
        LaunchBallLightning(PlayerPos + Vector3.up * 1.5f + dir * 2f, dir);
        return true;
    }

    private static void LaunchBallLightning(Vector3 start, Vector3 dir)
    {
        var ball = SpellFx.Sphere(start, 0.8f, SpellFx.Storm, 1e6f);
        var ring = ball != null ? SpellFx.Disc(start, (float)SpellTuning.BallLightningRadius, SpellFx.Storm, 1e6f, ball.transform, SpellFx.Look.Ring) : null;
        AddZone(new SpellZone
        {
            Id = "ball_lightning", Center = start, Radius = (float)SpellTuning.BallLightningRadius, Left = (float)SpellTuning.BallLightningSeconds,
            Interval = (float)SpellTuning.BallLightningTick, Visual = ball,
            Frame = (z, dt) =>
            {
                z.Center += dir * (float)SpellTuning.BallLightningSpeed * dt;
                if (z.Visual != null) z.Visual.transform.position = z.Center;
            },
            End = z => SpellFx.Remove(ring), // the radius ring rides the ball and goes with it (COS-178)
            Pulse = z =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius)) Strike(e, SpellTuning.BallLightningZap(Stats, Rng.FromTime()));
            },
        });
        // Teammates fly their copy along the same line for the same time, so it ends at the same place (COS-80).
        SpellNet.NoteMotion(ball, (float)SpellTuning.BallLightningSeconds, dir * (float)SpellTuning.BallLightningSpeed);
    }

    // ---------------------------------------------------------------- bursts and melee

    /// <summary>Wide Reach (COS-85): collects the mod's drops in range that pass the rarity rule (through the normal pickup, so the
    /// host's single-pickup rule holds in co-op) and the game's loose resource pickups. No damage. Not while pushing a sled.</summary>
    private static bool WideReach()
    {
        var eye = Eye;
        PerkHooks.WideReachPickup.Cast();
        SpellFx.Disc(Feet(), PerkHooks.WideReachPickup.Radius, SpellFx.Steel, 0.5f, LocalPlayer.Transform, SpellFx.Look.Ring);
        return true;
    }

    private static bool Bash()
    {
        var eye = Eye;
        var flat = new Vector3(eye.forward.x, 0, eye.forward.z).normalized;
        int struck = 0;
        foreach (var e in InCone(PlayerPos + Vector3.up, flat, (float)SpellTuning.BashRange, (float)SpellTuning.BashAngle / 2))
        {
            if (!e.IsActive) continue;
            struck++;
            Strike(e, Hit(SpellTuning.BashDamage, SpellTuning.BashScaling));
            BashItems(e, EnemyPatches.LastHit?.After ?? 0);
            if (!e.IsActive) continue;
            e.Combat.ApplyWeaken("spell.bash", S("spell_bash_damage_debuff_amount"), S("spell_bash_duration"));
            e.Combat.ApplySlow("spell.bash", S("spell_bash_slow_amount"), S("spell_bash_duration"));
            var away = Center(e) - PlayerPos;
            away.y = 0;
            Knock(e, away.normalized * (float)SpellTuning.BashKnockback);
        }
        // Lucky Bashes (perk): a Bash that hit raises crit damage for a moment.
        if (struck > 0 && SpellTuning.LuckyBash(Stats) is StatModifier[] lucky) Buffs.Apply("lucky_bashes", SpellTuning.LuckyBashSeconds, lucky);
        SpellFx.Sphere(PlayerPos + flat * 2.2f + Vector3.up, 1.2f, SpellFx.Steel, 0.2f);
        SpellFx.Disc(Feet(), (float)SpellTuning.BashRange, SpellFx.Steel, 0.4f, LocalPlayer.Transform, SpellFx.Look.Ring);
        return true;
    }

    /// <summary>Jagged Edge and Bloodthirster (COS-88): Bash may bleed the enemy, and heals and restores energy from the damage dealt.</summary>
    internal static void BashItems(EnemyRuntime e, double dealt)
    {
        if (dealt <= 0) return;
        if (e.IsActive && UnityEngine.Random.value < S(PerkStatIds.BashBleedChance))
            e.Combat.ApplyDot("spell.bash.bleed", DotKind.Bleed, dealt * PerkRules.BashBleedShare, S("spell_bash_duration"));
        double leech = S(PerkStatIds.BashLifesteal) * dealt;
        if (leech <= 0) return;
        PlayerEffects.Heal(leech);
        PlayerEffects.AddEnergy(leech);
    }

    private static bool SnapFreeze()
    {
        float dist = S("spell_snap_freeze_dist");
        foreach (var e in EnemiesWithin(PlayerPos, dist))
        {
            Strike(e, Hit(SpellTuning.SnapFreezeDamage, S("spell_snap_damage_scaling")));
            if (e.IsActive) e.Combat.ApplySlow("spell.snap_freeze", S("spell_snap_float_amount"), S("spell_snap_freeze_duration"));
        }
        SpellFx.Disc(Feet(), dist, SpellFx.Ice, 1.2f);
        return true;
    }

    private static bool RoaringCheeks()
    {
        float radius = S("spell_fart_radius");
        foreach (var e in EnemiesWithin(PlayerPos, radius))
        {
            Strike(e, Hit(S("spell_fart_base_dmg"), S("spell_fart_damage_scaling")));
            if (!e.IsActive) continue;
            e.Combat.ApplySlow("spell.roaring_cheeks", S("spell_fart_slow"), S("spell_fart_debuff_duration"));
            var away = Center(e) - PlayerPos;
            away.y = 0;
            Knock(e, away.normalized * S("spell_fart_knockback") * (float)SpellTuning.RoarKnockbackScale);
        }
        SpellFx.Disc(Feet(), radius, SpellFx.Dark, 1f, look: SpellFx.Look.Gas);
        return true;
    }

    private static bool Portal()
    {
        SpellPortals.Place(S("spell_portal_duration"));
        return true;
    }

    // ---------------------------------------------------------------- projectiles

    private static bool Firebolt()
    {
        var eye = Eye;
        AddBolt(new SpellBolt
        {
            Id = "firebolt", Pos = eye.position + eye.forward, Dir = eye.forward, Speed = (float)SpellTuning.ProjectileSpeed, Range = 60, Radius = 1f,
            Visual = SpellFx.Sphere(eye.position + eye.forward, 0.3f, SpellFx.Fire, 1e6f),
            OnHit = (b, e) =>
            {
                var hit = Hit(SpellTuning.FireboltDamage, S("spell_firebolt_damage_scaling"));
                Strike(e, hit);
                if (e.IsActive) { var (ps, sec) = FireRules.Burn(Stats, hit.Amount * SpellTuning.FireboltBurnFraction, SpellTuning.FireboltBurnSeconds); e.Combat.ApplyDot("player.firebolt", DotKind.Fire, ps, sec); }
                SpellFx.Sphere(b.Pos, 0.9f, SpellFx.Fire, 0.3f);
            },
        });
        return true;
    }

    private static bool MagicArrow()
    {
        var eye = Eye;
        int volley = 1 + Mathf.Max(0, Mathf.RoundToInt(S("spell_magic_arrow_volley_count")));
        for (int i = 0; i < volley; i++)
        {
            float yaw = volley == 1 ? 0 : (i - (volley - 1) / 2f) * 6f;
            FireMagicArrow(eye.position, Quaternion.AngleAxis(yaw, Vector3.up) * eye.forward);
        }
        return true;
    }

    private static void FireMagicArrow(Vector3 from, Vector3 dir)
    {
        {
            AddBolt(new SpellBolt
            {
                Id = "magic_arrow", Pos = from + dir, Dir = dir, Speed = (float)SpellTuning.MagicArrowSpeed, Range = (float)SpellTuning.MagicArrowRange, Radius = 1f, Pierces = true,
                Visual = SpellFx.Sphere(from + dir, 0.25f, SpellFx.Arcane, 1e6f),
                OnHit = (b, e) =>
                {
                    Strike(e, Hit(SpellTuning.MagicArrowDamage, S("spell_magic_arrow_damage_scaling"), Stats.IsOn("spell_magic_arrow_crit")));
                    if (!e.IsActive) return;
                    if (Stats.IsOn("spell_magic_arrow_dmg_debuff")) e.Combat.ApplyWeaken("spell.magic_arrow", 1.25, S("spell_magic_arrow_duration"));
                    if (Stats.IsOn("spell_magic_arrow_double_slow")) e.Combat.ApplySlow("spell.magic_arrow", 0.5, S("spell_magic_arrow_duration"));
                    SpellFx.Sphere(b.Pos, 0.8f, SpellFx.Arcane, 0.3f);
                },
            });
        }
    }

    // ---------------------------------------------------------------- unique items that drive spells (COS-42)

    private static float _crossfireReadyAt, _greedTimer;

    /// <summary>Free Magic Arrows Crossfire has fired, Wide Reach casts Greed has made (self-test).</summary>
    public static int CrossfireArrows { get; private set; }
    public static int GreedCasts { get; private set; }

    /// <summary>Crossfire: a projectile hit fires a free Magic Arrow at the target, at most once per gap.</summary>
    public static void OnWeaponHit(EnemyRuntime enemy, WeaponClass weapon)
    {
        if (weapon != WeaponClass.Ranged || !Stats.IsOn(UniqueFlagIds.Crossfire) || Time.time < _crossfireReadyAt) return;
        _crossfireReadyAt = Time.time + (float)UniqueTuning.CrossfireGapFor(Stats);
        var from = Eye.position;
        FireMagicArrow(from, (Center(enemy) - from).normalized);
        CrossfireArrows++;
    }

    /// <summary>Per-frame item effects: Greed casts Wide Reach once a second.</summary>
    public static void ItemTick(float dt)
    {
        if (!Stats.IsOn(UniqueFlagIds.Greed)) { _greedTimer = 0; return; }
        _greedTimer += dt;
        if (_greedTimer < UniqueTuning.GreedInterval) return;
        _greedTimer = 0;
        WideReach();
        GreedCasts++;
    }

    /// <summary>Seeking Arrow (COS-171): its own homing bolt at the nearest enemy within 60 degrees of the view; it hunts for
    /// <c>spell_seeking_arrow_duration</c> s, then slows what it hits.</summary>
    private static bool SeekingArrow()
    {
        var eye = Eye;
        var target = Nearest(eye.position, (float)SpellTuning.SeekingArrowRange, e => Vector3.Angle(eye.forward, Center(e) - eye.position) < 60);
        AddBolt(new SpellBolt
        {
            Id = "seeking_arrow", Pos = eye.position + eye.forward, Dir = eye.forward, Speed = (float)SpellTuning.SeekingArrowSpeed,
            Range = 1000, Life = S("spell_seeking_arrow_duration"), Homing = true, Target = target, Radius = 1f,
            Visual = SpellFx.Sphere(eye.position + eye.forward, 0.25f, SpellFx.Steel, 1e6f),
            OnHit = (b, e) =>
            {
                Strike(e, SpellTuning.SeekingArrowHit(Hit(SpellTuning.SeekingArrowDamage, SpellTuning.SeekingArrowScaling), Stats));
                if (e.IsActive) e.Combat.ApplySlow("spell.seeking_arrow", S("spell_seeking_arrow_slow_amount"), S("spell_seeking_arrow_slow_duration"));
                SpellFx.Sphere(b.Pos, 0.7f, SpellFx.Steel, 0.3f);
            },
        });
        return true;
    }

    /// <summary>Blood Infused Arrow (COS-171): pays its health and fires its own bolt with the damage (the Withered Crown fans out extra
    /// bolts for the one cost); with the triple perk it is tripled and an energy leak follows.</summary>
    private static bool BloodInfusedArrow()
    {
        var health = LocalPlayer.Vitals.Health;
        var rng = Rng.FromTime();
        var (cost, hit) = SpellTuning.BloodArrow(Stats, health._currentValue, rng);
        health.SetCurrentValue(Mathf.Max(1, health._currentValue - (float)cost));
        hit = hit with { Amount = SpellTuning.BloodArrowStored(Stats, hit) };
        if (!hit.Crit && Stats.IsOn("spell_bia_crit")) hit = hit with { Crit = true, Amount = hit.Amount * (1 + Stats.Get(StatIds.CritDamage)) };
        int arrows = 1 + (Stats.IsOn(UniqueFlagIds.WitheredCrown) ? UniqueTuning.CrownExtraArrows : 0);
        var eye = Eye;
        for (int i = 0; i < arrows; i++)
        {
            float yaw = arrows == 1 ? 0 : (i - (arrows - 1) / 2f) * 5f;
            FireBloodArrow(eye.position, Quaternion.AngleAxis(yaw, Vector3.up) * eye.forward, hit);
        }
        if (Stats.IsOn("spell_bia_triple_dmg"))
            PlayerDebuffs.Buffs.Apply(PlayerDebuffs.EnergyLeak, "spell.blood_arrow", SpellTuning.BloodArrowLeakSeconds, SpellTuning.BloodArrowLeakPerSecond);
        BloodArrowsFired += arrows;
        return true;
    }

    private static void FireBloodArrow(Vector3 from, Vector3 dir, OutgoingHit hit)
    {
        AddBolt(new SpellBolt
        {
            Id = "blood_arrow", Pos = from + dir, Dir = dir, Speed = (float)SpellTuning.BloodArrowSpeed, Range = (float)SpellTuning.BloodArrowRange, Radius = 1f,
            Visual = SpellFx.Sphere(from + dir, 0.3f, SpellFx.Blood, 1e6f),
            OnHit = (b, e) =>
            {
                Strike(e, hit);
                if (e.IsActive && hit.Crit && Stats.IsOn("spell_bia_stun")) e.Combat.ApplyStun(SpellTuning.BloodArrowStunSeconds);
                SpellFx.Sphere(b.Pos, 0.9f, SpellFx.Blood, 0.3f);
            },
        });
    }

    /// <summary>Blood Infused Arrows fired (self-test: Withered Crown).</summary>
    public static int BloodArrowsFired { get; private set; }

    // ---------------------------------------------------------------- channeled and toggle spells

    private static bool StartShield()
    {
        Shield.Channeling = true;
        _shieldFx = SpellFx.Sphere(PlayerPos + Vector3.up * 1.1f, 1.3f, SpellFx.Steel, 1e6f, LocalPlayer.Transform);
        return true;
    }

    private static bool Has(string perkId) => Pc.Perks.TimesBought(perkId) > 0;

    /// <summary>Black Flame (COS-171): while it burns, everything within <see cref="SpellTuning.BlackFlameRadius"/> of the player takes fire
    /// damage every <see cref="SpellTuning.BlackFlameTick"/> s; no weapon hit needed. The ring shows the radius.</summary>
    private static bool StartBlackFlame()
    {
        _flameTimer = 0;
        // Enchant Weapon (perk): all damage x1.33 for as long as the flame burns.
        if (Has(PerkEffectIds.EnchantWeapon))
            Buffs.Apply("enchant_weapon", 1e6, new[] { new StatModifier(StatIds.AllDamage, EffectOp.Mult, PerkTuning.EnchantWeaponDamage) });
        // COS-179: flames along the edge that follow the ground, no disc (teammates draw the same ring, StateTints).
        _flameRing?.Destroy();
        _flameRing = new Visuals.FlameRing();
        _flameRing.Update(PlayerPos, (float)SpellTuning.BlackFlameRadius);
        return true;
    }

    /// <summary>Flames in the local Black Flame ring (self-tests).</summary>
    public static int FlameRingFlames => _flameRing?.Flames ?? 0;

    /// <summary>Per-frame work of the running toggle spells (SpellWorld.Step).</summary>
    public static void Sustained(float dt)
    {
        if (!SpellHits.Running("spell.black_flame")) { _flameTimer = 0; return; }
        _flameRing?.Update(PlayerPos, (float)SpellTuning.BlackFlameRadius);
        _flameTimer += dt;
        while (_flameTimer >= SpellTuning.BlackFlameTick)
        {
            _flameTimer -= (float)SpellTuning.BlackFlameTick;
            bool afterburn = Has(PerkEffectIds.Afterburn);
            foreach (var e in EnemiesWithin(PlayerPos, (float)SpellTuning.BlackFlameRadius))
            {
                Strike(e, Hit(SpellTuning.BlackFlameTickDamage(Has(PerkEffectIds.Netherflame)), S("spell_black_flame_damage_scaling")));
                if (afterburn && e.IsActive && UnityEngine.Random.value < PerkTuning.AfterburnChance)
                    e.Combat.ApplyWeaken(PerkEffectIds.Afterburn, PerkTuning.AfterburnDamageTaken, PerkTuning.AfterburnSeconds);
            }
        }
    }

    private static bool StartMultishot()
    {
        // A ring at the feet while it runs, so teammates see it on and off (COS-80).
        _multishotFx = SpellFx.Disc(Feet(), 1.2f, SpellFx.Steel, 1e6f, LocalPlayer.Transform, SpellFx.Look.Ring);
        return true;
    }

    /// <summary>A channeled or toggle spell in <paramref name="slot"/> was switched off or ran out of energy.</summary>
    public static void Stopped(int slot)
    {
        if (Pc.Spells.Loadout[slot] is string stoppedId) SpellNet.ToggleOff(stoppedId);
        switch (Pc.Spells.Loadout[slot])
        {
            case "spell.multishot":
                SpellFx.Remove(_multishotFx);
                _multishotFx = null;
                break;
            case "spell.sustain_shield":
                Shield.Channeling = false;
                SpellFx.Remove(_shieldFx);
                _shieldFx = null;
                break;
            case "spell.black_flame":
                _flameRing?.Destroy();
                _flameRing = null;
                Buffs.Remove("enchant_weapon");
                break;
        }
    }

    private static Vector3 Feet() => PlayerPos + Vector3.up * 0.1f;
}
