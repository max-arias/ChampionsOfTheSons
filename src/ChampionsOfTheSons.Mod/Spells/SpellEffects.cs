using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using TheForest.Utils;
using UnityEngine;
using static ChampionsOfTheSons.Mod.Spells.SpellWorld;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// What each of the 26 spells besides Blink does when cast. Numbers come from <see cref="SpellTuning"/> and the character's
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
    };

    private static GameObject? _shieldFx, _flameFx;
    private static double _warCryArmor;

    private static float _flameTimer, _multishotTimer;

    public static void ResetState()
    {
        _shieldFx = _flameFx = null;
        _flameTimer = _multishotTimer = 0;
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
        SpellFx.Disc(Feet(), 3f, SpellFx.Holy, 1.2f, LocalPlayer.Transform);
        return true;
    }

    private static bool Berserk()
    {
        Buffs.Apply("berserk", S("spell_berserk_duration"), SpellTuning.Berserk(Stats));
        SpellFx.Disc(Feet(), 2.2f, SpellFx.Blood, 1.2f, LocalPlayer.Transform);
        return true;
    }

    private static bool GoldenSkin()
    {
        Buffs.Apply("golden_skin", SpellTuning.GoldenSkinDuration, SpellTuning.GoldenSkin());
        SpellFx.Disc(Feet(), 2.2f, SpellFx.Holy, 1.2f, LocalPlayer.Transform);
        return true;
    }

    private static bool Focus()
    {
        Buffs.Apply("focus", S("spell_focus_on_atk_speed_duration") > 0 ? S("spell_focus_on_atk_speed_duration") : SpellTuning.FocusDuration, SpellTuning.Focus(Stats));
        SpellFx.Disc(Feet(), 1.6f, SpellFx.Steel, 1f, LocalPlayer.Transform);
        return true;
    }

    private static bool FrenzyCast()
    {
        Frenzy.Cast();
        Buffs.Apply("frenzy", SpellTuning.FrenzyDuration, SpellTuning.Frenzy(Stats, Frenzy.Stacks));
        SpellFx.Disc(Feet(), 1.8f, SpellFx.Fire, 1f, LocalPlayer.Transform);
        return true;
    }

    private static bool Taunt()
    {
        Buffs.Apply("taunt", SpellTuning.TauntDuration, SpellTuning.Taunt());
        float speed = S("spell_taunt_speed_change");
        // Falter (perk): the taunted slow down instead of rushing the taunter.
        if (Has(PerkEffectIds.Falter)) speed = (float)PerkTuning.FalterSpeed;
        foreach (var e in EnemiesWithin(PlayerPos, 30))
            e.Combat.ApplySlow("spell.taunt", speed > 0 ? speed : 2, SpellTuning.TauntDuration); // above 1: they rush the taunter
        SpellFx.Disc(Feet(), 30f, SpellFx.Blood, 0.6f);
        return true;
    }

    private static bool ParryCast()
    {
        Parry.Cast();
        SpellFx.Sphere(PlayerPos + Vector3.up * 1.2f, 1.1f, SpellFx.Steel, (float)SpellTuning.ParryWindow, LocalPlayer.Transform);
        return true;
    }

    /// <summary>An incoming hit was parried: strike back around the player, heal and refund energy, buff attack speed.</summary>
    public static void Riposte()
    {
        float radius = S("spell_parry_radius");
        foreach (var e in EnemiesWithin(PlayerPos, radius))
        {
            Strike(e, Hit(S("spell_parry_damage"), S("spell_parry_damage_scaling")));
            if (Stats.IsOn("spell_parry_ignites")) e.Combat.ApplyDot("player.parry", DotKind.Fire, S("spell_parry_damage") * 0.25, 4);
        }
        PlayerEffects.Heal(S("spell_parry_heal"));
        PlayerEffects.AddEnergy(S("spell_parry_energy"));
        Buffs.Apply("parry", S("spell_parry_buff_duration"), new[]
        {
            new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, S("spell_parry_attack_speed"))),
            new StatModifier(StatIds.AllDamage, EffectOp.Mult, 1 + S("spell_parry_buff_damage")),
        });
        SpellFx.Disc(Feet(), Math.Max(1, radius), SpellFx.Steel, 0.6f, LocalPlayer.Transform);
    }

    private static bool Purge()
    {
        PlayerEffects.ClearDebuffs();
        if (Stats.IsOn("spell_purge_heal")) PlayerEffects.Heal(LocalPlayer.Vitals.Health.GetMax() * SpellTuning.PurgeHealFraction);
        // Purgatory (COS-88): damage rises with missing health, measured as the Purge lands.
        var health = LocalPlayer.Vitals.Health;
        double purge = PerkRules.PurgeDamage(Stats, health.GetMax() > 0 ? health._currentValue / health.GetMax() : 1);
        if (purge > 1) Buffs.Apply("purgatory", PerkRules.PurgeSeconds, new[] { new StatModifier(StatIds.AllDamage, EffectOp.Mult, purge) });
        SpellFx.Disc(Feet(), 3f, SpellFx.Heal, 1f, LocalPlayer.Transform);
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
            Visual = SpellFx.Disc(at, radius, SpellFx.Heal, 1e6f),
            Pulse = z =>
            {
                if (Vector3.Distance(PlayerPos, z.Center) > z.Radius) return;
                PlayerEffects.Heal(SpellTuning.HealingDomeHeal(LocalPlayer.Vitals.Health.GetMax(), Stats) * z.Interval);
                PlayerEffects.AddEnergy(SpellTuning.HealingDomeEnergy(Derived.MaxEnergy(Stats), Stats) * z.Interval); // Humming Ground (COS-94)
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
            Visual = SpellFx.Disc(at, radius, SpellFx.Holy, 1e6f),
            Pulse = z =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius)) e.Combat.ApplySlow("spell.flare", S("spell_flare_slow"), 2);
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
        var core = SpellFx.Sphere(at, 1.5f, SpellFx.Dark, 1e6f);
        var caught = new HashSet<EnemyRuntime>();
        AddZone(new SpellZone
        {
            Id = "black_hole", Center = at, Radius = radius, Left = S("spell_blackhole_duration"), Interval = (float)SpellTuning.BlackHoleTick,
            Visual = core,
            Frame = (z, dt) =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius))
                {
                    var to = z.Center - Center(e);
                    float d = to.magnitude;
                    if (d < 2f) continue;
                    Push(e, to / d * Mathf.Min(pull * dt, d - 2f));
                }
            },
            Pulse = z =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius))
                {
                    Strike(e, Hit(S("spell_blackhole_damage"), S("spell_blackhole_damage_scaling")));
                    caught.Add(e);
                }
            },
            // Last Engineer's Note: a black hole that caught enough enemies releases a Ball Lightning where it was.
            End = z =>
            {
                if (!Stats.IsOn(UniqueFlagIds.Spark) || caught.Count < UniqueTuning.SparkEnemies) return;
                var dir = Eye.forward;
                dir.y = 0;
                LaunchBallLightning(z.Center, dir.normalized);
                SparkBalls++;
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
        float radius = (float)SpellTuning.SnowStormRadius;
        float interval = Math.Max(0.1f, S("spell_snowstorm_hit_delay"));
        AddZone(new SpellZone
        {
            Id = "snow_storm", Center = at, Radius = radius, Left = (float)SpellTuning.SnowStormSeconds, Interval = interval,
            Visual = SpellFx.Disc(at, radius, SpellFx.Ice, 1e6f),
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

    /// <summary>Ball Lightnings released by Last Engineer's Note (self-test).</summary>
    public static int SparkBalls { get; private set; }

    private static void LaunchBallLightning(Vector3 start, Vector3 dir)
    {
        var ball = SpellFx.Sphere(start, 0.8f, SpellFx.Storm, 1e6f);
        AddZone(new SpellZone
        {
            Id = "ball_lightning", Center = start, Radius = (float)SpellTuning.BallLightningRadius, Left = (float)SpellTuning.BallLightningSeconds,
            Interval = (float)SpellTuning.BallLightningTick, Visual = ball,
            Frame = (z, dt) =>
            {
                z.Center += dir * (float)SpellTuning.BallLightningSpeed * dt;
                if (z.Visual != null) z.Visual.transform.position = z.Center;
            },
            Pulse = z =>
            {
                foreach (var e in EnemiesWithin(z.Center, z.Radius)) Strike(e, SpellTuning.BallLightningZap(Stats, Rng.FromTime()));
            },
        });
    }

    // ---------------------------------------------------------------- bursts and melee

    /// <summary>Wide Reach (COS-85): collects the mod's drops in range that pass the rarity rule (through the normal pickup, so the
    /// host's single-pickup rule holds in co-op) and the game's loose resource pickups. No damage. Not while pushing a sled.</summary>
    private static bool WideReach()
    {
        var eye = Eye;
        PerkHooks.WideReachPickup.Cast();
        SpellFx.Sphere(eye.position + eye.forward * 3f, 1.4f, SpellFx.Steel, 0.2f);
        return true;
    }

    private static bool Bash()
    {
        var eye = Eye;
        var flat = new Vector3(eye.forward.x, 0, eye.forward.z).normalized;
        foreach (var e in InCone(PlayerPos + Vector3.up, flat, (float)SpellTuning.BashRange, (float)SpellTuning.BashAngle / 2))
        {
            if (!e.IsActive) continue;
            Strike(e, Hit(SpellTuning.BashDamage, SpellTuning.BashScaling));
            BashItems(e);
            if (!e.IsActive) continue;
            e.Combat.ApplyWeaken("spell.bash", S("spell_bash_damage_debuff_amount"), S("spell_bash_duration"));
            e.Combat.ApplySlow("spell.bash", S("spell_bash_slow_amount"), S("spell_bash_duration"));
            var away = Center(e) - PlayerPos;
            away.y = 0;
            Push(e, away.normalized * (float)SpellTuning.BashKnockback);
        }
        SpellFx.Sphere(PlayerPos + flat * 2.2f + Vector3.up, 1.2f, SpellFx.Steel, 0.2f);
        return true;
    }

    /// <summary>Jagged Edge and Bloodthirster (COS-88): Bash may bleed the enemy, and heals and restores energy from the damage dealt.</summary>
    private static void BashItems(EnemyRuntime e)
    {
        double dealt = EnemyPatches.LastHit?.After ?? 0;
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
            Push(e, away.normalized * S("spell_fart_knockback") * (float)SpellTuning.RoarKnockbackScale);
        }
        SpellFx.Disc(Feet(), radius, SpellFx.Dark, 1f);
        return true;
    }

    private static bool Portal()
    {
        SpellPortals.Open(S("spell_portal_duration"));
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
                if (e.IsActive) e.Combat.ApplyDot("player.firebolt", DotKind.Fire, hit.Amount * SpellTuning.FireboltBurnFraction, SpellTuning.FireboltBurnSeconds);
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
                Id = "magic_arrow", Pos = from + dir, Dir = dir, Speed = (float)SpellTuning.MagicArrowSpeed, Range = (float)SpellTuning.MagicArrowRange, Radius = 1f,
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
                var hit = Hit(SpellTuning.SeekingArrowDamage, SpellTuning.SeekingArrowScaling);
                Strike(e, hit with { Amount = hit.Amount * (1 + S("spell_seeking_arrow_damage_bonus")) });
                if (e.IsActive) e.Combat.ApplySlow("spell.seeking_arrow", S("spell_seeking_arrow_slow_amount"), S("spell_seeking_arrow_slow_duration"));
                SpellFx.Sphere(b.Pos, 0.7f, SpellFx.Steel, 0.3f);
            },
        });
        return true;
    }

    private static bool BloodInfusedArrow()
    {
        var health = LocalPlayer.Vitals.Health;
        var (cost, hit) = SpellTuning.BloodArrow(Stats, health._currentValue, Rng.FromTime());
        health.SetCurrentValue(Mathf.Max(1, health._currentValue - (float)cost));
        var eye = Eye;
        // Withered Crown: the one cast (one health cost) fires extra arrows in a fan.
        int arrows = 1 + (Stats.IsOn(UniqueFlagIds.WitheredCrown) ? UniqueTuning.CrownExtraArrows : 0);
        for (int i = 0; i < arrows; i++)
        {
            float yaw = arrows == 1 ? 0 : (i - (arrows - 1) / 2f) * 5f;
            FireBloodArrow(eye.position, Quaternion.AngleAxis(yaw, Vector3.up) * eye.forward, hit);
        }
        BloodArrowsFired += arrows;
        return true;
    }

    /// <summary>Blood Infused Arrows fired (self-test: Withered Crown).</summary>
    public static int BloodArrowsFired { get; private set; }

    private static void FireBloodArrow(Vector3 from, Vector3 dir, OutgoingHit hit)
    {
        AddBolt(new SpellBolt
        {
            Id = "blood_arrow", Pos = from + dir, Dir = dir, Speed = (float)SpellTuning.BloodArrowSpeed, Range = (float)SpellTuning.BloodArrowRange, Radius = 1f,
            Visual = SpellFx.Sphere(from + dir, 0.3f, SpellFx.Blood, 1e6f),
            OnHit = (b, e) =>
            {
                Strike(e, hit);
                if (e.IsActive && Stats.IsOn("spell_bia_stun")) e.Combat.ApplyStun(2);
                SpellFx.Sphere(b.Pos, 0.9f, SpellFx.Blood, 0.3f);
            },
        });
    }

    // ---------------------------------------------------------------- channeled and toggle spells

    private static bool StartShield()
    {
        Shield.Channeling = true;
        _shieldFx = SpellFx.Sphere(PlayerPos + Vector3.up * 1.1f, 1.3f, SpellFx.Steel, 1e6f, LocalPlayer.Transform);
        return true;
    }

    private static bool Has(string perkId) => Pc.Perks.TimesBought(perkId) > 0;

    private static bool StartBlackFlame()
    {
        _flameTimer = 0;
        // Enchant Weapon (perk): all damage x1.33 for as long as the flame burns.
        if (Has(PerkEffectIds.EnchantWeapon))
            Buffs.Apply("enchant_weapon", 1e6, new[] { new StatModifier(StatIds.AllDamage, EffectOp.Mult, PerkTuning.EnchantWeaponDamage) });
        _flameFx = SpellFx.Disc(Feet(), (float)SpellTuning.BlackFlameRadius, SpellFx.Dark, 1e6f, LocalPlayer.Transform);
        return true;
    }

    /// <summary>Extra Multishot projectiles fired by Grip of Sora (self-test).</summary>
    public static int SoraExtras { get; private set; }

    private static bool StartMultishot()
    {
        _multishotTimer = 0;
        return true;
    }

    /// <summary>A channeled or toggle spell in <paramref name="slot"/> was switched off or ran out of energy.</summary>
    public static void Stopped(int slot)
    {
        switch (Pc.Spells.Loadout[slot])
        {
            case "spell.sustain_shield":
                Shield.Channeling = false;
                SpellFx.Remove(_shieldFx);
                _shieldFx = null;
                break;
            case "spell.black_flame":
                SpellFx.Remove(_flameFx);
                _flameFx = null;
                Buffs.Remove("enchant_weapon");
                break;
        }
    }

    /// <summary>Per-frame work of a running channeled or toggle spell.</summary>
    public static void Sustained(string id, float dt)
    {
        switch (id)
        {
            case "spell.black_flame":
                _flameTimer += dt;
                while (_flameTimer >= SpellTuning.BlackFlameTick)
                {
                    _flameTimer -= (float)SpellTuning.BlackFlameTick;
                    double flame = SpellTuning.BlackFlameDamage * (Has(PerkEffectIds.Netherflame) ? PerkTuning.NetherflameDamage : 1);
                    bool afterburn = Has(PerkEffectIds.Afterburn);
                    foreach (var e in EnemiesWithin(PlayerPos, (float)SpellTuning.BlackFlameRadius))
                    {
                        Strike(e, Hit(flame, S("spell_black_flame_damage_scaling")));
                        if (afterburn && UnityEngine.Random.value < PerkTuning.AfterburnChance)
                            e.Combat.ApplyWeaken(PerkEffectIds.Afterburn, PerkTuning.AfterburnDamageTaken, PerkTuning.AfterburnSeconds);
                    }
                }
                break;
            case "spell.multishot":
                _multishotTimer += dt;
                while (_multishotTimer >= SpellTuning.MultishotInterval)
                {
                    _multishotTimer -= (float)SpellTuning.MultishotInterval;
                    var target = Nearest(PlayerPos, (float)SpellTuning.MultishotRange);
                    if (target == null) break;
                    Strike(target, Hit(SpellTuning.MultishotDamage, SpellTuning.MultishotScaling));
                    SpellFx.Sphere(Center(target), 0.5f, SpellFx.Steel, 0.2f);
                    // Grip of Sora: extra projectiles go to the next nearest enemies, the rest to the main target.
                    if (Stats.IsOn(UniqueFlagIds.Sora))
                    {
                        var others = EnemiesWithin(PlayerPos, (float)SpellTuning.MultishotRange).Where(e => e != target)
                            .OrderBy(e => (Center(e) - PlayerPos).sqrMagnitude).Take(UniqueTuning.SoraExtraProjectiles).ToList();
                        for (int k = 0; k < UniqueTuning.SoraExtraProjectiles; k++)
                            Strike(k < others.Count ? others[k] : target, Hit(SpellTuning.MultishotDamage, SpellTuning.MultishotScaling));
                        SoraExtras += UniqueTuning.SoraExtraProjectiles;
                    }
                }
                break;
        }
    }

    /// <summary>A player hit landed on an enemy (not one of our own spell hits): Frenzy stacks up, Focus slows the target.</summary>
    public static void OnPlayerHit(EnemyRuntime enemy)
    {
        if (Striking) return;
        if (Frenzy.OnHit(Stats)) Buffs.Apply("frenzy", SpellTuning.FrenzyDuration, SpellTuning.Frenzy(Stats, Frenzy.Stacks));
        if (Buffs.Has("focus") && enemy.IsActive)
            enemy.Combat.ApplySlow("spell.focus", S("spell_focus_slow_amount"), S("spell_focus_slow_duration"));
    }

    private static Vector3 Feet() => PlayerPos + Vector3.up * 0.1f;
}
