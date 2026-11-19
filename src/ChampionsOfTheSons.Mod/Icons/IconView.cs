using SUI;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Icons;

/// <summary>SUI image widgets for content icons. They never take pointer input and hide themselves when the icon file is missing.</summary>
internal static class IconView
{
    /// <summary>A hidden, non-interactive image; call <see cref="Show"/> to fill it.</summary>
    public static SImageOptions Create()
    {
        var img = SImage;
        img.ImageObject.raycastTarget = false;
        img.ImageObject.color = UnityEngine.Color.white;
        img.Active(false);
        return img;
    }

    /// <summary>Shows the icon <paramref name="id"/> (null/missing hides the image). Returns whether an icon is shown.</summary>
    public static bool Show(SImageOptions img, string? id)
    {
        var tex = id == null ? null : IconCache.GetTexture(id);
        if (tex != null) img.Texture(tex);
        img.Active(tex != null);
        return tex != null;
    }

    public static bool IsShown(SImageOptions img) => img.Root.activeSelf && img.ImageObject.texture != null;
}
