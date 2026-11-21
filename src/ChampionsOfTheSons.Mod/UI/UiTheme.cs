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

    /// <summary>Stat lines by the stat's own rarity (COS-96): the rarity colours mixed 35% toward grey, so a card of
    /// stats stays calm next to the item name in full colour.</summary>
    private static readonly string[] StatHexes = RarityHexes.Select(h => Mix(h, "#9AA3AF", 0.35f)).ToArray();

    public static string StatHex(Rarity r) => StatHexes[Math.Clamp((int)r, 0, StatHexes.Length - 1)];

    private static string Mix(string a, string b, float t)
    {
        int C(string h, int i) => Convert.ToInt32(h.Substring(1 + i * 2, 2), 16);
        return "#" + string.Concat(Enumerable.Range(0, 3).Select(i => ((int)Math.Round(C(a, i) * (1 - t) + C(b, i) * t)).ToString("X2")));
    }

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
}
