using ChampionsOfTheSons.Core.Rules;
using HarmonyLib;
using RedLoader;
using Sons.Weapon;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Game hooks of the weapon types (COS-93): trees, the spear throw, and the swing, hit and shot events that drive the
/// per-type sounds and the swing trail.
/// </summary>
[HarmonyPatch]
internal static class WeaponPatches
{
    /// <summary>Swords and hammers don't fell trees: the type's game weapon does no tree damage.</summary>
    [HarmonyPatch(typeof(MeleeWeaponController), nameof(MeleeWeaponController.GetTreeCutDamageMultiplier))]
    [HarmonyPostfix]
    private static void TreeCut(MeleeWeaponController __instance, ref float __result)
    {
        if (OnLocalType(__instance, out var type) && !WeaponTypes.CanCutTrees(type)) __result = 0;
    }

    /// <summary>A spear type that can't be thrown never enters the aim (right button) state, so attacks stay stabs.</summary>
    [HarmonyPatch(typeof(SpearWeaponController), nameof(SpearWeaponController.BlockStart))]
    [HarmonyPrefix]
    private static bool SpearAimStart(SpearWeaponController __instance) => !NoThrow(__instance);

    [HarmonyPatch(typeof(SpearWeaponController), nameof(SpearWeaponController.UpdateBlock))]
    [HarmonyPrefix]
    private static bool SpearAimUpdate(SpearWeaponController __instance) => !NoThrow(__instance);

    [HarmonyPatch(typeof(MeleeWeaponController), nameof(MeleeWeaponController.OnSwingWeapon))]
    [HarmonyPostfix]
    private static void Swing(MeleeWeaponController __instance)
    {
        if (__instance != null && RealWeapons.Registered) RealWeapons.OnSwing(__instance);
        Event(__instance);
    }

    [HarmonyPatch(typeof(SpearWeaponController), nameof(SpearWeaponController.OnMeleeAttackEvent))]
    [HarmonyPostfix]
    private static void SpearSwing(SpearWeaponController __instance) => Event(__instance);

    /// <summary>A bow type's release: its swing sound is the shot.</summary>
    [HarmonyPatch(typeof(BowWeaponController), nameof(BowWeaponController.FireWeapon))]
    [HarmonyPostfix]
    private static void Shot(BowWeaponController __instance) => Event(__instance);

    private static void Event(Il2CppSystem.Object? instance)
    {
        if (instance == null || WeaponFx.Skins.Count == 0) return;
        IntPtr p = instance.Pointer;
        try { WeaponFx.Swing(p); }
        catch (Exception e) { RLog.Error($"Champions of the Sons: weapon event failed: {e}"); }
    }

    internal static bool NoThrow(SpearWeaponController spear) => OnLocalType(spear, out var type) && !WeaponTypes.CanThrow(type);

    private static bool OnLocalType(MeleeWeaponController weapon, out Core.Content.WeaponTypeDef? type)
    {
        type = WeaponModels.Active;
        return type != null && weapon != null && WeaponModels.Base != null && WeaponModels.Base.Pointer == weapon.Pointer;
    }

}
