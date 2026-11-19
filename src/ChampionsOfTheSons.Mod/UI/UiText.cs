using System.Globalization;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>
/// Player-facing strings for the panels. Content strings (item names, stat names, set bonuses) come from
/// <see cref="ModContent.Text"/>; UI chrome uses "ui.*" keys with an English fallback so the panels work
/// before the translation files carry those keys.
/// </summary>
internal static class UiText
{
    public static string T(string key, string fallback) => ModContent.Text.Get(key, fallback);

    public static string RarityName(Rarity r) => T("ui.rarity." + r.ToString().ToLowerInvariant(), r.ToString());

    public static string TypeName(string type) => T("ui.type." + type, type switch
    {
        "chest_armor" => "Chest Armor",
        "shoulder_armor" => "Shoulders",
        "boot" => "Boots",
        "glove" => "Gloves",
        "spell_scroll" => "Spell Scroll",
        _ => Humanize(type),
    });

    /// <summary>"equip.ring1" -> "Ring".</summary>
    public static string SlotName(string slot)
    {
        var id = slot.StartsWith("equip.", StringComparison.Ordinal) ? slot.Substring(6) : slot;
        return T("ui.slot." + id, id switch
        {
            "ring1" or "ring2" => "Ring",
            "offhand" => "Offhand",
            _ => Humanize(id),
        });
    }

    public static string ItemName(ItemInstance item) =>
        ModContent.Text.Get(item.BaseId + ".name", ModContent.Db.Items.TryGetValue(item.BaseId, out var d) ? d.Name : item.BaseId);

    public static string StatName(string itemStatId) =>
        ModContent.Text.Get(itemStatId + ".name", ModContent.Db.ItemStats.TryGetValue(itemStatId, out var d) ? d.Name : itemStatId);

    /// <summary>A rolled item stat formatted per its definition: percent stats as %, rounded to the stat's digits.</summary>
    public static string StatValue(string itemStatId, double value, bool signed = true)
    {
        ModContent.Db.ItemStats.TryGetValue(itemStatId, out var def);
        return Number(value, def?.Percent ?? false, def?.Rounding ?? 0, signed);
    }

    public static string Number(double value, bool percent, int digits, bool signed)
    {
        digits = Math.Clamp(digits, 0, 4);
        double v = percent ? value * 100 : value;
        v = Math.Round(v, digits, MidpointRounding.AwayFromZero);
        string num = Math.Abs(v).ToString("F" + digits, CultureInfo.InvariantCulture);
        string sign = v < 0 ? "-" : signed ? "+" : "";
        return sign + num + (percent ? "%" : "");
    }

    /// <summary>Delta that rounds to zero at the stat's own precision (so it is not shown).</summary>
    public static bool IsNegligible(string itemStatId, double delta)
    {
        ModContent.Db.ItemStats.TryGetValue(itemStatId, out var def);
        int digits = Math.Clamp(def?.Rounding ?? 0, 0, 4);
        double v = (def?.Percent ?? false) ? delta * 100 : delta;
        return Math.Round(v, digits, MidpointRounding.AwayFromZero) == 0;
    }

    private static string Humanize(string id)
    {
        var words = id.Split('_', ' ');
        return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }
}
