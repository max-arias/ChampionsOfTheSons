using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class WeaponGlowTests
{
    private static ItemBaseDef Weapon(bool unique = false, GlowDef? glow = null) => new()
    {
        Id = "item.x", WeaponModel = "long_sword", Glow = glow,
        UniqueEffects = unique ? new() { new StatEffect { Stat = "s", Op = EffectOp.Add, Value = 1 } } : new(),
    };

    [Theory]
    [InlineData(Rarity.Common, GlowLevel.Common)]
    [InlineData(Rarity.Magic, GlowLevel.Magic)]
    [InlineData(Rarity.Rare, GlowLevel.Rare)]
    [InlineData(Rarity.Legendary, GlowLevel.Legendary)]
    public void Each_rarity_has_its_own_look(Rarity rarity, GlowLevel level) => Assert.Equal(level, WeaponGlow.Level(Weapon(), rarity));

    [Fact]
    public void Glow_grows_with_the_look_and_uniques_are_always_legendary()
    {
        Assert.Equal(0, WeaponGlow.Strength(GlowLevel.Common));
        Assert.True(WeaponGlow.Strength(GlowLevel.Magic) < WeaponGlow.Strength(GlowLevel.Rare));
        Assert.True(WeaponGlow.Strength(GlowLevel.Rare) < WeaponGlow.Strength(GlowLevel.Legendary));
        Assert.Equal(GlowLevel.Legendary, WeaponGlow.Level(Weapon(unique: true), Rarity.Common));
        Assert.Equal(GlowLevel.Common, WeaponGlow.Level(new ItemBaseDef { Id = "item.ring" }, Rarity.Legendary));
        Assert.False(WeaponGlow.HasExtras(GlowLevel.Rare));
        Assert.True(WeaponGlow.HasExtras(GlowLevel.Legendary));
    }

    [Fact]
    public void Only_a_unique_may_set_its_own_tint_and_style()
    {
        var frost = Weapon(true, new GlowDef { Tint = "#ff8000", Style = "frost" });
        Assert.Equal("frost", WeaponGlow.Style(frost));
        Assert.Equal((1f, 128 / 255f, 0f), WeaponGlow.Color(frost, GlowLevel.Legendary));
        Assert.Equal(WeaponGlow.ParseHex(WeaponGlow.Hex(GlowLevel.Legendary)), WeaponGlow.Color(Weapon(unique: true), GlowLevel.Legendary));
        Assert.Equal(WeaponGlow.ParseHex(WeaponGlow.Hex(GlowLevel.Rare)), WeaponGlow.Color(Weapon(glow: new GlowDef { Tint = "#ff8000" }), GlowLevel.Rare));
    }

    [Fact]
    public void Bad_glow_tint_or_style_is_a_content_error()
    {
        var db = ContentDb.Load(new Dictionary<string, string>
        {
            ["player_stats.json"] = """{ "playerStats": [] }""", ["item_stats.json"] = """{ "itemStats": [] }""",
            ["items.json"] = """{ "items": [ { "id": "item.x", "name": "X", "type": "weapon", "statPools": [], "glow": { "tint": "red", "style": "plasma" } } ] }""",
        });
        Assert.Contains(db.Errors, e => e.Contains("red"));
        Assert.Contains(db.Errors, e => e.Contains("plasma"));
    }
}
