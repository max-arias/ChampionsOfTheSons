using System.Reflection;
using HarmonyLib;
using Sons.Weapon;

namespace ChampionsOfTheSons.Mod.BookSpike;

/// <summary>
/// COS-62: while a mod page shows in the guide book, the game's book keeps running (hand, animation, cursor) but its
/// page actions are skipped, so a click never picks a blueprint and the page never flips under us.
/// </summary>
[HarmonyPatch]
internal static class BookBlockPatches
{
    /// <summary>Set while a mod page is on the book.</summary>
    public static bool ModPageActive;
    public static int Blocked;

    private static readonly string[] Methods =
    {
        nameof(BlueprintBookController.OnInteractWithLayout), nameof(BlueprintBookController.StartBlueprintSelectionMode),
        nameof(BlueprintBookController.OnCycleForward), nameof(BlueprintBookController.OnCycleBackward),
        nameof(BlueprintBookController.FlipPageForward), nameof(BlueprintBookController.FlipPageBackward),
        nameof(BlueprintBookController.JumpToCategory), nameof(BlueprintBookController.OnActivateTutorials),
        nameof(BlueprintBookController.SwapPageTexture), nameof(BlueprintBookController.UpdatePageObjects),
    };

    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(BlueprintBookController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => Methods.Contains(m.Name));

    private static bool Prefix()
    {
        if (!ModPageActive) return true;
        Blocked++;
        return false;
    }
}
