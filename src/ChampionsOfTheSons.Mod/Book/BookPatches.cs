using System.Reflection;
using HarmonyLib;
using Sons.Crafting;
using Sons.Weapon;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// While one of our tabs is selected the game's book keeps running (hand, animation, cursor) but its page actions
/// are skipped: a click never picks a blueprint and the page never flips under our page (COS-62). A click on one of
/// the game's own tabs hands the page back to the game and runs normally.
/// </summary>
[HarmonyPatch]
internal static class BookPatches
{
    /// <summary>Set by <see cref="BookHost"/> while a mod page shows.</summary>
    public static bool Block;
    public static int Blocked;

    private static readonly string[] PageActions =
    {
        nameof(BlueprintBookController.StartBlueprintSelectionMode),
        nameof(BlueprintBookController.OnCycleForward), nameof(BlueprintBookController.OnCycleBackward),
        nameof(BlueprintBookController.FlipPageForward), nameof(BlueprintBookController.FlipPageBackward),
        nameof(BlueprintBookController.JumpToCategory), nameof(BlueprintBookController.OnActivateTutorials),
        nameof(BlueprintBookController.SwapPageTexture), nameof(BlueprintBookController.UpdatePageObjects),
    };

    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(BlueprintBookController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => PageActions.Contains(m.Name));

    private static bool Prefix()
    {
        if (!Block) return true;
        Blocked++;
        return false;
    }

}

/// <summary>Layout clicks: a game tab while our page shows gives the page back to the game; anything else is skipped.</summary>
[HarmonyPatch(typeof(BlueprintBookController), nameof(BlueprintBookController.OnInteractWithLayout))]
internal static class BookInteractPatch
{
    private static bool Prefix(HeldBookInteraction heldBookInteraction)
    {
        if (!BookPatches.Block) return true;
        try
        {
            if (heldBookInteraction != null && BookTabs.IsGameTab(heldBookInteraction))
            {
                BookHost.ShowGamePage();
                return true;
            }
        }
        catch (Exception e)
        {
            RedLoader.RLog.Error($"Champions of the Sons book: tab check failed: {e.Message}");
        }
        BookPatches.Blocked++;
        return false;
    }
}
