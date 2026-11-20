using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>The four weapon looks (COS-108, Max 2026-11-20): the planned four-rarity scale.</summary>
public enum GlowLevel
{
    /// <summary>White, no glow.</summary>
    Common,
    /// <summary>Bluish, a small glow.</summary>
    Magic,
    /// <summary>Yellow, a medium glow.</summary>
    Rare,
    /// <summary>Red-orange, a strong glow plus embers along the blade, a pulsing light, a flame trail and sparks on hit.</summary>
    Legendary,
}

/// <summary>
/// The glow look of a weapon item (COS-108). <see cref="LevelOf"/> is the one place that maps today's eight rarities onto
/// the four looks, so collapsing the rarities later only changes it. Uniques (item bases with unique effects) always get
/// the Legendary look; a unique may set its own tint and particle style.
/// </summary>
public static class WeaponGlow
{
    public const string DefaultStyle = "ember";
    public static readonly IReadOnlyList<string> Styles = new[] { "ember", "frost", "wisp" };

    /// <summary>Today's rarities onto the four looks.</summary>
    public static GlowLevel LevelOf(Rarity rarity) => rarity switch
    {
        Rarity.Common or Rarity.Uncommon => GlowLevel.Common,
        Rarity.Magic => GlowLevel.Magic,
        Rarity.Rare or Rarity.Epic => GlowLevel.Rare,
        _ => GlowLevel.Legendary,
    };

    public static bool IsUnique(ItemBaseDef def) => def.UniqueEffects.Count > 0;

    /// <summary>A weapon's look: its rarity's, or Legendary for a unique. Non-weapons don't glow.</summary>
    public static GlowLevel Level(ItemBaseDef? def, Rarity rarity)
    {
        if (def == null || def.WeaponModel == null) return GlowLevel.Common;
        return IsUnique(def) ? GlowLevel.Legendary : LevelOf(rarity);
    }

    /// <summary>The look's colour as "#rrggbb".</summary>
    public static string Hex(GlowLevel level) => level switch
    {
        GlowLevel.Magic => "#5AA8FF",
        GlowLevel.Rare => "#FFD94A",
        GlowLevel.Legendary => "#FF5A1E",
        _ => "#FFFFFF",
    };

    /// <summary>Edge brightness: 0 (none), small, medium, strong.</summary>
    public static double Strength(GlowLevel level) => level switch
    {
        GlowLevel.Magic => 0.2,
        GlowLevel.Rare => 0.45,
        GlowLevel.Legendary => 1.3,
        _ => 0,
    };

    /// <summary>Particles, light, flame trail and hit sparks: Legendary only.</summary>
    public static bool HasExtras(GlowLevel level) => level == GlowLevel.Legendary;

    public static string Style(ItemBaseDef? def) => def?.Glow?.Style ?? DefaultStyle;

    /// <summary>The colour as 0..1 RGB: a unique's own tint if it sets one, else the look's colour.</summary>
    public static (float R, float G, float B) Color(ItemBaseDef? def, GlowLevel level) =>
        (def != null && IsUnique(def) ? ParseHex(def.Glow?.Tint) : null) ?? ParseHex(Hex(level))!.Value;

    public static (float R, float G, float B)? ParseHex(string? hex)
    {
        if (hex == null || hex.Length != 7 || hex[0] != '#') return null;
        if (!int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int v)) return null;
        return (((v >> 16) & 0xff) / 255f, ((v >> 8) & 0xff) / 255f, (v & 0xff) / 255f);
    }
}
