using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class PadInputTests
{
    [Fact]
    public void Binding_with_modifier_fires_only_when_the_modifier_is_held()
    {
        Assert.True(PadBinding.TryParse("LeftShoulder+DpadUp", out var b));
        Assert.Equal(PadButton.DpadUp, b.Main);
        Assert.Equal(new[] { PadButton.LeftShoulder }, b.Modifiers);
        Assert.True(b.IsTriggered(x => x == PadButton.LeftShoulder, x => x == PadButton.DpadUp));
        Assert.False(b.IsTriggered(_ => false, x => x == PadButton.DpadUp));
        Assert.False(b.IsTriggered(x => x == PadButton.LeftShoulder, _ => false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nope")]
    [InlineData("LeftShoulder+Nope")]
    [InlineData("99")]
    public void Bad_bindings_do_not_parse(string text) => Assert.False(PadBinding.TryParse(text, out _));

    [Fact]
    public void Binding_round_trips_through_text() =>
        Assert.Equal("RightShoulder+West", PadBinding.TryParse("rightshoulder + west", out var b) ? b.ToString() : "");

    [Fact]
    public void Focus_moves_along_rows_and_columns()
    {
        // 2x2 grid of 10x10 cells with a gap
        var grid = new[] { new FocusRect(0, 0, 10, 10), new FocusRect(20, 0, 10, 10), new FocusRect(0, 20, 10, 10), new FocusRect(20, 20, 10, 10) };
        Assert.Equal(1, FocusNav.Move(grid, 0, FocusDirection.Right));
        Assert.Equal(2, FocusNav.Move(grid, 0, FocusDirection.Down));
        Assert.Equal(2, FocusNav.Move(grid, 3, FocusDirection.Left));
        Assert.Equal(-1, FocusNav.Move(grid, 0, FocusDirection.Up));
        Assert.Equal(-1, FocusNav.Move(grid, 0, FocusDirection.Left));
    }

    [Fact]
    public void Focus_prefers_the_same_row_over_a_nearer_cell_in_another_row()
    {
        var items = new[] { new FocusRect(0, 0, 10, 10), new FocusRect(60, 0, 10, 10), new FocusRect(15, 30, 10, 10) };
        Assert.Equal(1, FocusNav.Move(items, 0, FocusDirection.Right));
    }
}
