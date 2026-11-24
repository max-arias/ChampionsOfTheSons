using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using HarmonyLib;
using RedLoader;
using Sons.Items.Core;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.PerkHooks;

/// <summary>
/// Which ranged weapon the local player last fired, and the ammo refund of the no-consume perks (COS-86).
/// Spike (2026-11-20, interop): every player projectile, thrown spears included (<c>SpearWeaponController.GetRangedWeapon</c>), leaves
/// through <see cref="RangedWeapon"/>, which knows its item (<c>_weaponItemId</c>) and owner (<c>_isLocalPlayer</c>); the item name
/// gives the kind (<see cref="RangedRules.KindOf"/>). Damage arrives later in <c>VailActor.ReceivedDamage</c> without the weapon, so a
/// projectile hit is credited to the last shot. <c>RangedWeaponController.OnAmmoSpent</c> runs when a round is used.
/// </summary>
[HarmonyPatch]
internal static class ShotTracker
{
    /// <summary>A projectile hit later than this after the last shot is not credited to it.</summary>
    public const float ShotMemorySeconds = 15f;

    private static readonly Rng Rng = Rng.FromTime();

    public static RangedKind LastKind { get; private set; }
    private static float _lastShotAt = -1000f;

    /// <summary>The weapon kind a projectile hit arriving now came from.</summary>
    public static RangedKind KindForHit() => Time.time - _lastShotAt <= ShotMemorySeconds ? LastKind : RangedKind.Other;

    public static void Reset()
    {
        LastKind = RangedKind.Other;
        _lastShotAt = -1000f;
    }

    /// <summary>The weapon kind of the game item <paramref name="itemId"/> (Other when the item database cannot say).</summary>
    public static RangedKind KindOfItem(int itemId)
    {
        string? name = null;
        try
        {
            if (ItemDatabaseManager.TryFindItemById(itemId, out var data) && data != null) name = data.Name;
        }
        catch
        {
            // the item database is not ready: the shot stays unclassified
        }
        return RangedRules.KindOf(name);
    }

    /// <summary>Self-tests: the last shot came from a weapon of this kind.</summary>
    public static void RecordKind(RangedKind kind)
    {
        LastKind = kind;
        _lastShotAt = Time.time;
    }

    /// <summary>Records a shot from the game item <paramref name="itemId"/> (also used by self-tests).</summary>
    public static void Record(int itemId)
    {
        LastKind = KindOfItem(itemId);
        _lastShotAt = Time.time;
    }

    private static void Record(RangedWeapon? weapon)
    {
        try
        {
            if (weapon == null || !weapon._isLocalPlayer) return;
            Record(weapon._weaponItemId);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: shot tracking failed: {e}");
        }
    }

    [HarmonyPatch(typeof(RangedWeapon), nameof(RangedWeapon.Shoot), new Type[0])]
    [HarmonyPrefix]
    private static void BeforeShoot(RangedWeapon __instance) => Record(__instance);

    [HarmonyPatch(typeof(RangedWeapon), nameof(RangedWeapon.Shoot), new[] { typeof(Vector3), typeof(float), typeof(int) })]
    [HarmonyPrefix]
    private static void BeforeShootDirection(RangedWeapon __instance) => Record(__instance);

    [HarmonyPatch(typeof(RangedWeapon), nameof(RangedWeapon.ShootInternal))]
    [HarmonyPrefix]
    private static void BeforeShootInternal(RangedWeapon __instance) => Record(__instance);

    [HarmonyPatch(typeof(RangedWeaponController), nameof(RangedWeaponController.OnAmmoSpent))]
    [HarmonyPostfix]
    private static void AfterAmmoSpent(RangedWeaponController __instance)
    {
        try
        {
            var weapon = __instance._rangedWeapon;
            if (weapon == null || !weapon._isLocalPlayer) return;
            int ammo = weapon.GetAmmo()?._type ?? 0;
            TryRefund(ammo);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: ammo refund failed: {e}");
        }
    }

    /// <summary>With the no-consume chance, puts one round of <paramref name="ammoItemId"/> back in the game inventory. True when it did.</summary>
    public static bool TryRefund(int ammoItemId)
    {
        var inv = LocalPlayer.Inventory;
        if (ammoItemId <= 0 || inv == null || !RangedRules.KeepsAmmo(PlayerState.Character.Stats, Rng)) return false;
        if (!inv.AddItem(ammoItemId, 1, false, false, null)) return false;
        return true;
    }
}
