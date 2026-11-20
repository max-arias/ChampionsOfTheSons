using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Mod.Audio;

/// <summary>Which cue each event plays. Spells and enemy abilities go by family (fire, ice, lightning, projectile, buff, dark, warp).</summary>
public static class SoundCues
{
    public const string LevelUp = "level_up", Pickup = "pickup", PerkBought = "perk_bought", Craft = "craft",
        UiClick = "ui_click", UiOpen = "ui_open", LoopWind = "loop_wind", LoopFire = "loop_fire";

    private static readonly Dictionary<string, string> Spells = new()
    {
        ["spell.firebolt"] = "cast_fire",
        ["spell.black_flame"] = "cast_fire",
        ["spell.sun_flare"] = "cast_fire",
        ["spell.cataclysm"] = "cast_fire",
        ["spell.snap_freeze"] = "cast_ice",
        ["spell.snow_storm"] = "cast_ice",
        ["spell.ball_lightning"] = "cast_lightning",
        ["spell.magic_arrow"] = "cast_projectile",
        ["spell.multishot"] = "cast_projectile",
        ["spell.seeking_arrow"] = "cast_projectile",
        ["spell.blood_infused_arrow"] = "cast_projectile",
        ["spell.black_hole"] = "cast_debuff",
        ["spell.taunt"] = "cast_debuff",
        ["spell.blink"] = "cast_warp",
        ["spell.portal"] = "cast_warp",
        ["spell.bash"] = "cast_physical",
        ["spell.parry"] = "impact_metal",
    };

    /// <summary>The cast cue of a spell; buffs and anything unlisted get the buff cue.</summary>
    public static string Spell(string spellId) => Spells.GetValueOrDefault(spellId, "cast_buff");

    /// <summary>The drop cue by rarity: brighter for rarer items, heard from far (COTF picked the drop sound by item type).</summary>
    public static string Drop(Rarity rarity) => rarity switch
    {
        Rarity.Common => "drop_low",
        Rarity.Magic => "drop_mid",
        Rarity.Rare => "drop_high",
        _ => "drop_top",
    };

    /// <summary>The wind-up cue of an enemy cast, played at the enemy.</summary>
    public static string Telegraph(EnemyAbility a) => a switch
    {
        EnemyAbility.Meteor or EnemyAbility.Flare or EnemyAbility.FireCataclysm => "cast_fire",
        EnemyAbility.BlackHole or EnemyAbility.Chains or EnemyAbility.ArcaneCataclysm => "cast_debuff",
        EnemyAbility.Warp => "cast_warp",
        EnemyAbility.Trapper => "trap_set",
        EnemyAbility.Blizzard => "cast_ice",
        _ => "cast_lightning",
    };

    /// <summary>The cue when an enemy cast lands, played where it lands.</summary>
    public static string Impact(EnemyAbility a) => a switch
    {
        EnemyAbility.Meteor or EnemyAbility.Flare or EnemyAbility.FireCataclysm => "impact_fire",
        EnemyAbility.BlackHole or EnemyAbility.ArcaneCataclysm => "cast_debuff",
        EnemyAbility.Chains or EnemyAbility.Trapper => "impact_metal",
        EnemyAbility.Warp => "cast_warp",
        EnemyAbility.Blizzard => "impact_ice",
        _ => "impact_fire",
    };

    /// <summary>The loop of an always-on aura, or null.</summary>
    public static string? AuraLoop(EnemyAbility a) => a switch
    {
        EnemyAbility.Blizzard => LoopWind,
        EnemyAbility.Radiance => LoopFire,
        _ => null,
    };
}
