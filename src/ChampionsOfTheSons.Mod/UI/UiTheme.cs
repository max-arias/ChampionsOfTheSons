using ChampionsOfTheSons.Core;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Colors shared by the panels. All values are hex strings (#RRGGBB or #RRGGBBAA) so the text builders stay free of Unity types.</summary>
internal static class UiTheme
{
    /// <summary>Rarity 0..7: Common, Uncommon, Magic, Rare, Epic, Legendary, Mythic, Primal. Picked to stay readable on the dark window.</summary>
    private static readonly string[] RarityHexes =
    {
        "#C9CED6", // Common: light gray
        "#5BD75B", // Uncommon: green
        "#4D9DFF", // Magic: blue
        "#FFD94A", // Rare: yellow
        "#B86BFF", // Epic: purple
        "#FF9A2E", // Legendary: orange
        "#FF5A5A", // Mythic: red
        "#3DE8D6", // Primal: cyan
    };

    public static string RarityHex(Rarity r) => RarityHexes[Math.Clamp((int)r, 0, RarityHexes.Length - 1)];

    public const string Text = "#E6E9EE";
    public const string TextDim = "#8A93A0";
    public const string TextFaint = "#5B6470";
    public const string Gold = "#E3B341";
    public const string Good = "#6FE07A";
    public const string Bad = "#FF6B6B";
    public const string Stat = "#9CCBFF";
    public const string Unique = "#FFB86B";

    public const string Dim = "#000000A8";
    public const string Window = "#12161CF2";
    public const string Panel = "#1A2029";
    public const string SlotInner = "#0B0E12";
    public const string SlotFrameEmpty = "#2B333E";
    public const string Divider = "#2B333E";
    public const string BarBack = "#232A33";
    public const string TooltipInner = "#0D1015F7";
}
