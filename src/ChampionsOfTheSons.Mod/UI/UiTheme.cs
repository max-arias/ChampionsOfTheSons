using ChampionsOfTheSons.Core;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Colors shared by the panels. All values are hex strings (#RRGGBB or #RRGGBBAA) so the text builders stay free of Unity types.</summary>
internal static class UiTheme
{
    /// <summary>Rarity 0..3 (COS-109): Common white, Magic blue, Rare yellow, Legendary red-orange. Readable on the dark window.</summary>
    private static readonly string[] RarityHexes =
    {
        "#E8E8E8", // Common: white
        "#4D9DFF", // Magic: blue
        "#FFD94A", // Rare: yellow
        "#FF6A2E", // Legendary: red-orange
    };

    public static string RarityHex(Rarity r) => RarityHexes[Math.Clamp((int)r, 0, RarityHexes.Length - 1)];

    public const string Text = "#E6E9EE";
    public const string TextDim = "#8A93A0";
    public const string TextFaint = "#707A89";
    public const string Description = "#A3ACB9";
    public const string Gold = "#E3B341";
    public const string Good = "#6FE07A";
    public const string Bad = "#FF6B6B";
    public const string Stat = "#9CCBFF";
    public const string Unique = "#FF8A4C";

    public const string Dim = "#000000A8";
    public const string Window = "#12161CF2";
    public const string Panel = "#1A2029";
    public const string SlotInner = "#0B0E12";
    public const string SlotFrameEmpty = "#2B333E";
    public const string Divider = "#2B333E";
    public const string BarBack = "#232A33";
    public const string TooltipInner = "#0D1015F7";

    // Rune & bone action bar (COS-151). Fallbacks when the ui.hud.* art is missing, and the tints laid over it.
    public const string Bone = "#E8DCC0";
    public const string BoneDark = "#7A6246";
    public const string Hide = "#2A1B12";
    public const string Ink = "#2A1A10";
    public const string Ash = "#0A0604C8";
    public const string RuneGlow = "#7FDCFFB0";
    public const string EmberGlow = "#FF9A3CE0";
    public const string PickGlow = "#FFF1C890";
    public const string Starved = "#FF8070";
    public const string EnergyFill = "#6F8FE8";
    public const string BoneTint = "#D2C5AA";
    public const string XpFill = "#E3B341";
}
