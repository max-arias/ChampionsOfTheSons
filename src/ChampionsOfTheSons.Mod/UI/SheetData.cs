using System.Globalization;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Mod.UI;

internal readonly record struct SheetRow(string Id, string Label, string Value, bool Header = false);

internal readonly record struct SheetSummary(int Level, double Xp, double Goal)
{
    public double Fraction => Goal <= 0 ? 0 : Math.Clamp(Xp / Goal, 0, 1);
}

/// <summary>The character sheet's content, resolved from the stat sheet. Row order and ids are fixed so the view can update text in place.</summary>
internal static class SheetData
{
    public static SheetSummary Summary(PlayerCharacter pc) =>
        new(pc.Progress.Level, pc.Progress.Xp, XpCurve.Goal(pc.Progress.Level));

    public static List<SheetRow> Build(PlayerCharacter pc)
    {
        var s = pc.Stats;
        var rows = new List<SheetRow>();

        void Header(string id, string label) => rows.Add(new SheetRow(id, UiText.T("ui.sheet." + id, label), "", true));
        void Row(string id, string label, string value) => rows.Add(new SheetRow(id, UiText.T("ui.sheet." + id, label), value));

        Header("attributes", "Attributes");
        Row("strength", "Strength", Int(s.Get(StatIds.Strength)));
        Row("agility", "Agility", Int(s.Get(StatIds.Agility)));
        Row("vitality", "Vitality", Int(s.Get(StatIds.Vitality)));
        Row("intelligence", "Intelligence", Int(s.Get(StatIds.Intelligence)));

        Header("vitals", "Vitals");
        Row("max_health", "Max health", Int(Derived.MaxHealth(s)));
        Row("max_energy", "Max energy", Int(Derived.MaxEnergy(s)));
        Row("health_regen", "Health regen", Num(Derived.HealthRegenPerSecond(s, 0), 1) + "/s");

        Header("offense", "Offense");
        Row("melee_damage", "Melee damage", Mult(Derived.MeleeMultiplier(s)));
        Row("ranged_damage", "Ranged damage", Mult(Derived.RangedMultiplier(s)));
        Row("spell_damage", "Spell damage", Mult(Derived.SpellMultiplier(s)));
        Row("crit_chance", "Crit chance", Pct(s.Get(StatIds.CritChance), 1));
        Row("crit_damage", "Crit damage", Pct(s.Get(StatIds.CritDamage), 0, signed: true));
        Row("attack_speed", "Attack speed", Pct(s.Get(StatIds.AttackSpeed) - 1, 0, signed: true));

        Header("defense", "Defense");
        double armor = s.Get(StatIds.Armor);
        double taken = s.Get(StatIds.AllDamageTaken);
        Row("armor", "Armor", Int(armor));
        Row("physical_reduction", "Physical reduction", Pct(1 - taken * (1 - ArmorMath.Reduction(armor)), 1));
        Row("magic_reduction", "Magic reduction", Pct(1 - taken * s.Get(StatIds.MagicDamageTaken), 1));
        Row("dodge", "Dodge", Pct(1 - s.Get(StatIds.GetHitChance), 1));
        Row("block", "Block", Num(s.Get(StatIds.Block), 1));

        Header("utility", "Utility");
        Row("magic_find", "Magic find", Pct(s.Get(StatIds.MagicFind) - 1, 0, signed: true));
        Row("xp_gain", "XP gain", Pct(s.Get(StatIds.ExpGain) - 1, 0, signed: true));
        Row("move_speed", "Movement speed", Pct(s.Get(StatIds.MovementSpeed) - 1, 0, signed: true));
        return rows;
    }

    private static string Int(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Fixed digits with trailing zeros trimmed ("12.50" -> "12.5", "10.0" -> "10").</summary>
    private static string Num(double v, int digits)
    {
        var s = v.ToString("F" + digits, CultureInfo.InvariantCulture);
        return s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
    }

    private static string Mult(double v) => "×" + v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Pct(double fraction, int digits, bool signed = false) => UiText.Number(fraction, true, digits, signed);
}
