using System.Globalization;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Mod.UI;

internal readonly record struct SheetRow(string Id, string Label, string Value, bool Header = false, string Tip = "");

internal readonly record struct SheetSummary(int Level, double Xp, double Goal, int PerkPoints)
{
    public double Fraction => Goal <= 0 ? 0 : Math.Clamp(Xp / Goal, 0, 1);
}

/// <summary>The character sheet's content, resolved from the stat sheet. Row order and ids are fixed so the view can update text in place.</summary>
internal static class SheetData
{
    public static SheetSummary Summary(PlayerCharacter pc) =>
        new(pc.Progress.Level, pc.Progress.Xp, XpCurve.Goal(pc.Progress.Level), pc.Perks.AvailablePoints(pc.Progress));

    public static List<SheetRow> Build(PlayerCharacter pc)
    {
        var s = pc.Stats;
        var rows = new List<SheetRow>();

        void Header(string id, string label) => rows.Add(new SheetRow(id, UiText.T("ui.sheet." + id, label), "", true));
        // Tooltip text: ui.sheet.tip.<id> (English fallback below, with the live numbers appended where useful).
        // A tip with "{0}" gets tipArg filled in.
        void Row(string id, string label, string value, string tip, string live = "", string? tipArg = null)
        {
            string text = UiText.T("ui.sheet.tip." + id, tip);
            if (tipArg != null) text = string.Format(CultureInfo.InvariantCulture, text, tipArg);
            rows.Add(new SheetRow(id, UiText.T("ui.sheet." + id, label), value, false, text + live));
        }

        Header("attributes", "Attributes");
        Row("strength", "Strength", Int(s.Get(StatIds.Strength)),
            "Raises melee damage (1 + Strength × the melee bonus per point). Each level also adds attribute points.");
        Row("agility", "Agility", Int(s.Get(StatIds.Agility)),
            "Raises ranged damage and max energy.");
        Row("vitality", "Vitality", Int(s.Get(StatIds.Vitality)),
            "Raises max health (base 100 + Vitality × health per point, then the max health multiplier).");
        Row("intelligence", "Intelligence", Int(s.Get(StatIds.Intelligence)),
            "Raises spell damage and energy recovery.");

        Header("vitals", "Vitals");
        Row("max_health", "Max health", Int(Derived.MaxHealth(s)),
            "(100 + Vitality × health per point + flat bonuses) × max health multiplier.");
        Row("max_energy", "Max energy", Int(Derived.MaxEnergy(s)),
            "(100 + Agility × energy per point + flat bonuses) × max energy multiplier. Spells spend it.");
        Row("health_regen", "Health regen", Num(Derived.HealthRegenPerSecond(s, 0), 1) + "/s",
            "(0.25% of max health + flat regen) × regen rate × recovery multiplier, on top of the game's own regeneration.");

        Header("offense", "Offense");
        Row("melee_damage", "Melee damage", Mult(Derived.MeleeMultiplier(s)),
            "All-damage × melee bonus × (1 + Strength × melee damage per Strength). Multiplies the weapon's base damage.");
        Row("ranged_damage", "Ranged damage", Mult(Derived.RangedMultiplier(s)),
            "All-damage × ranged bonus × (1 + Agility × ranged damage per Agility). Bows, guns and thrown weapons.");
        Row("spell_damage", "Spell damage", Mult(Derived.SpellMultiplier(s)),
            "All-damage × spell bonus × (1 + Intelligence × spell damage per Intelligence).");
        Row("crit_chance", "Crit chance", Pct(s.Get(StatIds.CritChance), 1),
            "Chance that a hit is a critical hit.");
        Row("crit_damage", "Crit damage", Pct(s.Get(StatIds.CritDamage), 0, signed: true),
            "Extra damage a critical hit deals on top of the normal hit.");
        Row("attack_speed", "Attack speed", Pct(s.Get(StatIds.AttackSpeed) - 1, 0, signed: true),
            "Change in how fast you swing and shoot.");
        Row("armor_piercing", "Armor piercing", Int(Derived.MeleeArmorPierce(s)),
            "Flat armor your melee hits ignore on the target (ranged hits: {0}).", tipArg: Int(Derived.RangedArmorPierce(s)));
        Row("health_on_hit", "Health on hit", Num(s.Get(StatIds.HealthOnHit), 1),
            "Health you recover every time one of your hits lands.");

        Header("defense", "Defense");
        double armor = s.Get(StatIds.Armor);
        double taken = s.Get(StatIds.AllDamageTaken);
        Row("armor", "Armor", Int(armor),
            "Physical damage reduction from armor = (armor ÷ (armor + 500))², so each point is worth less than the last.",
            "\n" + Pct(ArmorMath.Reduction(armor), 1) + " from armor alone.");
        Row("physical_reduction", "Physical reduction", Pct(1 - taken * (1 - ArmorMath.Reduction(armor)), 1),
            "1 − damage taken multiplier × (1 − armor reduction). What you do not take from physical hits.");
        Row("magic_reduction", "Magic reduction", Pct(1 - taken * s.Get(StatIds.MagicDamageTaken), 1),
            "1 − damage taken multiplier × magic damage taken multiplier. Armor does not apply to magic.");
        Row("dodge", "Dodge", Pct(1 - s.Get(StatIds.GetHitChance), 1),
            "Chance that an incoming hit misses you completely.");
        Row("block", "Block", Num(s.Get(StatIds.Block), 1),
            "Flat damage removed from every hit you take (after armor).");
        Row("thorns", "Thorns", Num(s.Get(StatIds.Thorns), 1),
            "Damage reflected to the enemy that hits you in melee.");

        Header("utility", "Utility");
        Row("magic_find", "Magic find", Pct(s.Get(StatIds.MagicFind) - 1, 0, signed: true),
            "Raises the rarity of items that drop.");
        Row("xp_gain", "XP gain", Pct(s.Get(StatIds.ExpGain) - 1, 0, signed: true),
            "Multiplies all XP you receive.");
        Row("move_speed", "Movement speed", Pct(s.Get(StatIds.MovementSpeed) - 1, 0, signed: true),
            "Change in how fast you walk and run.");
        Row("massacre_time", "Massacre duration", Num(s.Get(StatIds.MaxMassacreTime), 0) + "s",
            "How long a kill streak lasts after a kill (longest wait). Each kill adds {0}s; the banked XP pays out when it runs out.",
            tipArg: Num(s.Get(StatIds.TimeBonusPerKill), 1));

        // COS-77: every player sees the world's difficulty (a client has it from the host).
        Header("world", "World");
        var rules = WorldState.Rules;
        Row("game_difficulty", "Game difficulty", HostRules.HostSettingsPanel.GameDifficultyText(rules.GameDifficulty),
            "Set by the game's own difficulty: it adds enemy levels and elites on top of the players' level.");
        Row("extra_levels", "Extra enemy levels", "+" + rules.ExtraEnemyLevels.ToString(CultureInfo.InvariantCulture),
            "Levels the host added to every enemy.");
        return rows;
    }

    /// <summary>COS-81: the party list for the Character page in co-op: every player's level and magic find, and the team
    /// magic find the host rolls drops with. Empty outside co-op.</summary>
    public static string PartyText(string headHex, string faintHex)
    {
        if (!Net.CoopSession.Active) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append($"\n\n<color={headHex}><b>{UiText.T("ui.coop.party", "Party").ToUpperInvariant()}</b></color>");
        string lv = UiText.T("ui.hud.level", "Lv");
        string mfLabel = UiText.T("ui.sheet.magic_find", "Magic find");
        void Line(string name, int level, double mf) =>
            sb.Append($"\n{name}  <b>{lv} {level}</b>  <color={faintHex}>{mfLabel} {Pct(mf - 1, 0, signed: true)}</color>");
        Line(Net.CoopParty.LocalName, PlayerState.Character.Progress.Level, Net.CoopParty.LocalMagicFind);
        double team = Net.CoopParty.LocalMagicFind;
        foreach (var m in Net.CoopParty.Others)
        {
            Line(m.Name, m.Level, m.MagicFind);
            team += m.MagicFind > 0 ? m.MagicFind : 1;
        }
        sb.Append($"\n<color={faintHex}>{UiText.T("ui.coop.team_magic_find", "Team magic find (drops)")}: {Pct(team - 1, 0, signed: true)}</color>");
        return sb.ToString();
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
