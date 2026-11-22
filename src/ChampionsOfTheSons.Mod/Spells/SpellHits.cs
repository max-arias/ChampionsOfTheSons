using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;
using static ChampionsOfTheSons.Mod.Spells.SpellWorld;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// The spells that work through the player's real weapon hits and shots (COS-84): Black Flame, Bash, Frenzy, Focus, Parry, Multishot,
/// Seeking Arrow and Blood Infused Arrow. <see cref="Modify"/> sits in the damage hook's pipeline (<c>EnemyPatches</c>) and changes a hit
/// before it lands; <see cref="AfterHit"/> runs when it has (the bridge's <c>EnemyHit</c> event) and applies what the hit sets on the enemy.
/// Self-tests drive the same two calls the hook makes. The passive spells run while they sit in the six-slot loadout.
/// </summary>
internal static class SpellHits
{
    public static readonly StoredBonus Focus = new(), ParryBonus = new(), BloodArrows = new(), SeekArmed = new();
    private static readonly RepeatTracker Repeat = new();
    private static readonly Dictionary<EnemyRuntime, int> BashStacks = new();
    private static readonly Queue<int> VolleyFalloff = new();
    private static readonly Rng Rng = Rng.FromTime();
    private static EnemyRuntime? _seekTarget;
    private static double _seekLeft, _volleyLeft;
    private static bool _pending, _pendingBlood;
    private static WeaponClass _pendingWeapon;
    private static float _lastParryAt = -10f;

    /// <summary>The enemy Seeking Arrow marked, while the mark lasts.</summary>
    public static EnemyRuntime? SeekTarget => _seekTarget is { IsActive: true } && _seekLeft > 0 ? _seekTarget : null;

    /// <summary>Volley arrows that have not hit yet (their falloff is still queued).</summary>
    public static int VolleyPending => VolleyFalloff.Count;

    public static bool Slotted(string spellId) => Pc.Spells.Loadout.Contains(spellId);

    public static bool Running(string spellId)
    {
        for (int i = 0; i < SpellBook.Slots; i++)
            if (Pc.Spells.Loadout[i] == spellId && Pc.Spells.IsActive(i)) return true;
        return false;
    }

    public static void Tick(float dt)
    {
        Focus.Tick(dt);
        ParryBonus.Tick(dt);
        BloodArrows.Tick(dt);
        SeekArmed.Tick(dt);
        if (_seekLeft > 0 && (_seekLeft -= dt) <= 0) _seekTarget = null;
        if (_volleyLeft > 0 && (_volleyLeft -= dt) <= 0) VolleyFalloff.Clear();
    }

    public static void Reset()
    {
        Focus.Clear();
        ParryBonus.Clear();
        BloodArrows.Clear();
        SeekArmed.Clear();
        Repeat.Reset();
        BashStacks.Clear();
        VolleyFalloff.Clear();
        _seekTarget = null;
        _seekLeft = _volleyLeft = 0;
        _pending = _pendingBlood = false;
        _lastParryAt = -10f;
    }

    // ---------------------------------------------------------------- the hit pipeline

    /// <summary>A real weapon hit of the local player is about to land: the stored and stacking bonuses change its damage.</summary>
    public static OutgoingHit Modify(EnemyRuntime enemy, WeaponClass weapon, RangedShot? shot, bool head, OutgoingHit hit)
    {
        _pending = false;
        if (Striking || weapon is not (WeaponClass.Melee or WeaponClass.Ranged)) return hit;
        var s = Stats;
        bool melee = weapon == WeaponClass.Melee;
        double amount = hit.Amount;
        bool crit = hit.Crit;
        _pending = true;
        _pendingWeapon = weapon;
        _pendingBlood = false;

        // Multishot: each arrow of the volley takes its place in the falloff (arrows land in the order they were queued).
        if (!melee && VolleyFalloff.Count > 0) amount *= SpellTuning.MultishotCopyDamage(s, VolleyFalloff.Dequeue());

        // Frenzy: every attack is a stack; Repeat Offender adds flat damage for repeated hits on one target.
        if (Slotted("spell.frenzy"))
        {
            if (Frenzy.Active) amount += SpellTuning.FuryDamage(s, melee, Repeat.Hit(enemy));
            else Repeat.Hit(enemy);
            Frenzy.OnAttack(s);
            Buffs.Apply("frenzy", SpellTuning.FrenzyStackSeconds, SpellTuning.Frenzy(s, Frenzy.Stacks));
        }

        // Focus: the shot spends what the last one stored, then stores its own by where it hit.
        if (!melee && Slotted("spell.focus"))
        {
            if (Focus.Take() is double stored) amount *= 1 + stored;
            Focus.Store(SpellTuning.FocusBonus(s, head), SpellTuning.FocusStoredSeconds);
            if (head)
            {
                if (enemy.IsActive) enemy.Combat.ApplySlow("spell.focus", S("spell_focus_slow_amount"), S("spell_focus_slow_duration"));
            }
            else
            {
                Buffs.Apply("focus_haste", S("spell_focus_on_atk_speed_duration") > 0 ? S("spell_focus_on_atk_speed_duration") : SpellTuning.FocusDuration,
                    new[] { new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, s.Get("spell_focus_on_atk_speed"))) });
            }
        }

        // Parry's counter strike waits for the next melee hit.
        if (melee && ParryBonus.Take() is double counter) amount *= counter;

        // Bash (passive): hits on an enemy Bash already weakened hit harder with each stack.
        if (melee && Slotted("spell.bash"))
            amount *= SpellTuning.BashStackFactor(s, BashStacks.GetValueOrDefault(enemy));

        if (!melee)
        {
            // Seeking Arrow: the first arrow after the cast marks its target; while the mark lasts arrows lose a little damage.
            if (SeekArmed.Has && SeekArmed.Take() != null)
            {
                _seekTarget = enemy;
                _seekLeft = S("spell_seeking_arrow_duration");
                SpellFx.Sphere(Center(enemy) + Vector3.up, 0.6f, SpellFx.Steel, (float)_seekLeft);
            }
            if (SeekTarget != null)
            {
                amount *= SpellTuning.SeekingRangedMult * (1 + S("spell_seeking_arrow_damage_bonus"));
                if (head) amount *= SpellTuning.SeekingHeadMult(s);
                else if (enemy.IsActive) enemy.Combat.ApplySlow("spell.seeking_arrow", S("spell_seeking_arrow_slow_amount"), S("spell_seeking_arrow_slow_duration"));
            }

            // Blood Infused Arrow: the stored damage rides the next arrows (flat, so it adds to the hit's own damage).
            if ((shot?.Kind is RangedKind.Bow or RangedKind.Crossbow) && BloodArrows.Take() is double flat)
            {
                amount += flat;
                _pendingBlood = true;
                if (!crit && s.IsOn("spell_bia_crit") && Rng.Chance(Math.Clamp(s.Get(StatIds.CritChance), 0, 1)))
                {
                    crit = true;
                    amount *= 1 + s.Get(StatIds.CritDamage);
                }
            }
        }
        return hit with { Amount = amount, Crit = crit };
    }

    /// <summary>The hit that went through <see cref="Modify"/> landed for <paramref name="damage"/>: Black Flame ignites, Bash slows and weakens.</summary>
    public static void AfterHit(EnemyRuntime enemy, double damage)
    {
        if (!_pending) return;
        _pending = false;
        bool blood = _pendingBlood;
        _pendingBlood = false;
        if (damage <= 0 || !enemy.IsActive) return;
        var s = Stats;
        bool melee = _pendingWeapon == WeaponClass.Melee;

        if (Running("spell.black_flame") && Rng.Chance(SpellTuning.BlackFlameIgnite(_pendingWeapon)))
        {
            var (ps, sec) = FireRules.Burn(s, SpellTuning.BlackFlameBurn(s, Has(PerkEffectIds.Netherflame)), SpellTuning.BlackFlameBurnSeconds);
            enemy.Combat.ApplyDot("spell.black_flame", DotKind.Fire, ps, sec);
            if (Has(PerkEffectIds.Afterburn) && Rng.Chance(PerkTuning.AfterburnChance))
                enemy.Combat.ApplyWeaken(PerkEffectIds.Afterburn, PerkTuning.AfterburnDamageTaken, PerkTuning.AfterburnSeconds);
        }

        if (melee && Slotted("spell.bash"))
        {
            enemy.Combat.ApplyWeaken("spell.bash", S("spell_bash_damage_debuff_amount"), S("spell_bash_duration"));
            enemy.Combat.ApplySlow("spell.bash", S("spell_bash_slow_amount"), S("spell_bash_duration"));
            BashStacks[enemy] = Math.Min(SpellTuning.BashMaxStacks, BashStacks.GetValueOrDefault(enemy) + 1);
            SpellEffects.BashItems(enemy, damage);
        }

        if (blood && s.IsOn("spell_bia_stun")) enemy.Combat.ApplyStun(2);
    }

    private static bool Has(string perkId) => Pc.Perks.TimesBought(perkId) > 0;

    // ---------------------------------------------------------------- Parry

    /// <summary>The game parried an enemy attack (a timed block), or with Parry Anything any block: a slotted Parry strikes back.</summary>
    public static void OnParried(bool timedParry)
    {
        if (!Slotted("spell.parry") || Time.time - _lastParryAt < 0.5f) return;
        if (!timedParry && !Stats.IsOn(SpellTuning.ParryAnything)) return;
        _lastParryAt = Time.time;
        SpellEffects.Riposte();
    }

    // ---------------------------------------------------------------- Multishot

    private static bool _inVolley;

    /// <summary>A real bow, crossbow or slingshot shot of the local player left: with Multishot running it pays its energy and ammo and fires the extra arrows.
    /// Returns how many extra arrows went out (0 when it could not pay; the toggle then switches off).</summary>
    public static int OnShot(RangedWeapon weapon)
    {
        if (_inVolley || !LocalPlayer.IsInWorld) return 0;
        int slot = Array.IndexOf(Pc.Spells.Loadout.ToArray(), "spell.multishot");
        if (slot < 0 || !Pc.Spells.IsActive(slot)) return 0;
        var kind = PerkHooks.ShotTracker.KindOfItem(weapon._weaponItemId);
        if (kind is not (RangedKind.Bow or RangedKind.Crossbow or RangedKind.Slingshot)) return 0;

        var s = Stats;
        int extras = SpellTuning.MultishotExtras(s);
        int ammoId = weapon.GetAmmo()?._type ?? 0;
        var inv = LocalPlayer.Inventory;
        if (ammoId > 0 && inv != null) extras = Math.Min(extras, inv.AmountOf(ammoId, true, false)); // each arrow uses ammo
        double cost = SpellTuning.MultishotShotCost(s, extras);
        if (extras <= 0 || SpellCaster.Energy < cost)
        {
            SpellControl.StopSlot(slot); // a shot that cannot be paid turns Multishot off
            return 0;
        }
        SpellCaster.Spend(cost);
        if (ammoId > 0 && inv != null) inv.RemoveItem(ammoId, extras, false, false, false, null, false);
        FireVolley(weapon, extras);
        return extras;
    }

    private static void FireVolley(RangedWeapon weapon, int extras)
    {
        VolleyFalloff.Clear();
        for (int i = 0; i <= extras; i++) VolleyFalloff.Enqueue(i);
        _volleyLeft = 6;
        var eye = Eye;
        _inVolley = true;
        try
        {
            for (int i = 1; i <= extras; i++)
            {
                float yaw = (float)SpellTuning.MultishotSpreadDegrees * ((i + 1) / 2) * (i % 2 == 1 ? -1 : 1);
                weapon.Shoot(Quaternion.AngleAxis(yaw, eye.up) * eye.forward, 0f, i);
            }
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: multishot volley failed: {e}");
        }
        finally
        {
            _inVolley = false;
        }
    }

    /// <summary>Self-tests: queues a volley's falloff the way a shot does, without firing.</summary>
    public static void QueueVolleyForTest(int extras)
    {
        VolleyFalloff.Clear();
        for (int i = 0; i <= extras; i++) VolleyFalloff.Enqueue(i);
        _volleyLeft = 6;
    }

    // ---------------------------------------------------------------- Seeking Arrow

    /// <summary>The cast arms the mark: the next real arrow that hits an enemy marks it.</summary>
    public static void ArmSeeking()
    {
        SeekArmed.Store(1, SpellTuning.ArrowChargeSeconds);
        _seekTarget = null;
        _seekLeft = 0;
    }

    /// <summary>While a mark is up the arrow leaves aimed at it (if it is within 60 degrees of where the player looks).</summary>
    public static Quaternion? SeekAim(Vector3 from)
    {
        var target = SeekTarget;
        if (target == null) return null;
        var to = Center(target) - from;
        if (to.sqrMagnitude < 1e-4f || Vector3.Angle(Eye.forward, to) > 60f) return null;
        return Quaternion.LookRotation(to);
    }
}

/// <summary>Game hooks of the weapon-linked spells (COS-84): shots leaving a ranged weapon, and the player parrying or blocking an enemy attack.</summary>
[HarmonyPatch]
internal static class SpellWeaponPatches
{
    [HarmonyPatch(typeof(RangedWeapon), nameof(RangedWeapon.Shoot), new Type[0])]
    [HarmonyPostfix]
    private static void AfterShoot(RangedWeapon __instance)
    {
        try
        {
            if (__instance != null && __instance._isLocalPlayer) SpellHits.OnShot(__instance);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: multishot hook failed: {e}");
        }
    }

    [HarmonyPatch(typeof(RangedWeapon), nameof(RangedWeapon.ShootInternal))]
    [HarmonyPrefix]
    private static void BeforeShootInternal(RangedWeapon __instance, ref Quaternion lookRotation)
    {
        try
        {
            if (__instance == null || !__instance._isLocalPlayer) return;
            var kind = PerkHooks.ShotTracker.KindOfItem(__instance._weaponItemId);
            if (kind is not (RangedKind.Bow or RangedKind.Crossbow)) return;
            if (SpellHits.SeekAim(Eye.position) is Quaternion aim) lookRotation = aim;
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: seeking arrow hook failed: {e}");
        }
    }

    /// <summary>The game's own timed parry: an enemy attack was parried (single player: by us).</summary>
    [HarmonyPatch(typeof(VailActor), nameof(VailActor.OnAttackParried))]
    [HarmonyPostfix]
    private static void AfterAttackParried()
    {
        try
        {
            SpellHits.OnParried(timedParry: true);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: parry hook failed: {e}");
        }
    }

    /// <summary>Any block of an enemy attack (Parry Anything counts it as a parry).</summary>
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.OnBlockHit))]
    [HarmonyPostfix]
    private static void AfterBlockHit()
    {
        try
        {
            SpellHits.OnParried(timedParry: false);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: block hook failed: {e}");
        }
    }
}
