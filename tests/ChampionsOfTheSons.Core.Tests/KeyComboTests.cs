using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class KeyComboTests
{
    [Fact]
    public void Parses_modifiers_in_any_spelling_and_writes_one_form()
    {
        var c = KeyCombo.Parse("leftAlt+RightCtrl+1");
        Assert.True(c.Alt && c.Ctrl && !c.Shift);
        Assert.Equal("1", c.Key);
        Assert.Equal("ctrl+alt+1", c.ToString());
        Assert.True(KeyCombo.Parse("ALT+1").SameAs(KeyCombo.Parse("leftAlt+1")));
    }

    [Fact]
    public void Plain_keys_and_odd_values_stay_plain()
    {
        Assert.False(KeyCombo.Parse("leftShift").HasModifiers);
        Assert.Equal("leftShift", KeyCombo.Parse("leftShift").Key);
        Assert.Equal("foo+1", KeyCombo.Parse("foo+1").Key);
        Assert.Equal("", KeyCombo.Parse(null).Key);
    }

    [Fact]
    public void A_combo_beats_the_plain_key_only_while_its_modifier_is_held()
    {
        var plain = KeyCombo.Parse("1");
        var alt = KeyCombo.Parse("alt+1");
        var all = new[] { plain, alt };
        // Alt held: only Alt+1 fires.
        Assert.True(alt.Fires(all, ctrl: false, shift: false, alt: true));
        Assert.False(plain.Fires(all, ctrl: false, shift: false, alt: true));
        // No Alt: only the plain key.
        Assert.False(alt.Fires(all, false, false, false));
        Assert.True(plain.Fires(all, false, false, false));
    }

    [Fact]
    public void Extra_held_modifiers_do_not_block_a_key_with_no_rival()
    {
        var f = KeyCombo.Parse("f");
        Assert.True(f.Fires(new[] { f, KeyCombo.Parse("alt+1") }, ctrl: false, shift: true, alt: false));
        Assert.True(KeyCombo.Parse("alt+1").Fires(new[] { KeyCombo.Parse("alt+1") }, ctrl: false, shift: true, alt: true));
    }
}
