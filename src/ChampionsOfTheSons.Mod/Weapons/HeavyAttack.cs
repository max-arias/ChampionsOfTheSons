using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// The game's heavy (charged) melee attack: its animation raises <c>AnimationEventReceiver.HeavyMeleeAttackEvent</c> as the blow
/// lands. Hits within a short window after it count as charged (heavy attack and smash damage, COS-148).
/// </summary>
[HarmonyPatch]
internal static class HeavyAttack
{
    /// <summary>How long after the event a hit still belongs to that blow (s).</summary>
    private const float Window = 0.6f;

    private static float _until = -1;

    /// <summary>The local player's heavy blows seen so far (self-test).</summary>
    public static int Count { get; private set; }

    public static bool Active => Time.time < _until;

    [HarmonyPatch(typeof(Sons.Animation.AnimationEventReceiver), nameof(Sons.Animation.AnimationEventReceiver.HeavyMeleeAttackEvent))]
    [HarmonyPostfix]
    private static void AfterHeavy(Sons.Animation.AnimationEventReceiver __instance)
    {
        try
        {
            // Compare objects, not methods: IL2CPP can fold identical methods, so this may run for other receivers.
            var player = LocalPlayer.Transform;
            if (__instance == null || player == null || !__instance.transform.IsChildOf(player)) return;
            _until = Time.time + Window;
            if (Count++ == 0) RedLoader.RLog.Msg("Champions of the Sons: first heavy blow this session (heavy attack and smash damage apply)");
        }
        catch
        {
            // never break the game's animation events
        }
    }
}
