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
/// The spells that buff the player's own attacks (COS-84; since COS-171 only these, every other spell acts without a weapon hit):
/// Frenzy, Focus, Multishot and Parry's stored counter bonus. <see cref="Modify"/> sits in the damage hook's pipeline (<c>EnemyPatches</c>)
/// and changes a hit before it lands; <see cref="AfterHit"/> runs when it has. Self-tests drive the same two calls the hook makes.
/// </summary>
internal static class SpellHits
{
    public static readonly StoredBonus Focus = new(), ParryBonus = new();
    private static readonly RepeatTracker Repeat = new();
    private static readonly Queue<int> VolleyFalloff = new();
    private static double _volleyLeft;

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
        if (_volleyLeft > 0 && (_volleyLeft -= dt) <= 0) VolleyFalloff.Clear();
    }

    public static void Reset()
    {
        Focus.Clear();
        ParryBonus.Clear();
        Repeat.Reset();
        VolleyFalloff.Clear();
        _volleyLeft = 0;
    }

    // ---------------------------------------------------------------- the hit pipeline

    /// <summary>A real weapon hit of the local player is about to land: the stored and stacking bonuses change its damage.</summary>
    public static OutgoingHit Modify(EnemyRuntime enemy, WeaponClass weapon, RangedShot? shot, bool head, OutgoingHit hit)
    {
        if (Striking || weapon is not (WeaponClass.Melee or WeaponClass.Ranged)) return hit;
        var s = Stats;
        bool melee = weapon == WeaponClass.Melee;
        double amount = hit.Amount;
        bool crit = hit.Crit;

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

        return hit with { Amount = amount, Crit = crit };
    }

    /// <summary>The hit that went through <see cref="Modify"/> landed. Nothing rides a landed hit since COS-171; kept so the hook and
    /// self-tests keep one shape.</summary>
    public static void AfterHit(EnemyRuntime enemy, double damage) { }

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
}

/// <summary>Game hook of Multishot (COS-84): shots leaving a ranged weapon.</summary>
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
}
