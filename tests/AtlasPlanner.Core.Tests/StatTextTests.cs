using AtlasPlanner.Core.Stats;

namespace AtlasPlanner.Core.Tests;

public class StatTextTests
{
    [Theory]
    [InlineData("Your Maps have no chance to contain [ContainsAbyss|Abysses]", "Your Maps have no chance to contain Abysses")]
    [InlineData("Drops an extra [Scarab]", "Drops an extra Scarab")]
    [InlineData("[A|B] and [C|D]", "B and D")]
    public void Clean_unwraps_inline_markup(string raw, string expected) =>
        Assert.Equal(expected, StatText.Clean(raw));

    [Fact]
    public void Clean_collapses_embedded_newlines()
    {
        var cleaned = StatText.Clean("Your Maps with Ore Deposits have 50% increased chance\nto contain at least two Ore Deposits");

        Assert.Equal("Your Maps with Ore Deposits have 50% increased chance to contain at least two Ore Deposits", cleaned);
    }

    [Fact]
    public void Parse_lifts_the_number_out_and_keeps_the_sign_in_the_template()
    {
        var stat = StatText.Parse("Your Maps have +8% chance to contain Einhar");

        Assert.Equal("Your Maps have +#% chance to contain Einhar", stat.Template);
        Assert.Equal([8d], stat.Numbers);
        Assert.True(stat.IsSummable);
        Assert.Equal(8d, stat.Value);
    }

    [Fact]
    public void Parse_treats_a_tier_range_as_two_numbers_rather_than_a_negative()
    {
        var stat = StatText.Parse("Tier 1-5 Maps found have 20% chance to become 1 tier higher");

        Assert.Equal("Tier #-# Maps found have #% chance to become # tier higher", stat.Template);
        Assert.Equal([1d, 5d, 20d, 1d], stat.Numbers);
        Assert.False(stat.IsSummable);
    }

    [Fact]
    public void Parse_marks_stats_without_numbers_as_not_summable()
    {
        var stat = StatText.Parse("Scarabs cannot be found in Your Maps");

        Assert.Empty(stat.Numbers);
        Assert.False(stat.IsSummable);
        Assert.Equal(stat.Text, stat.Template);
    }

    [Fact]
    public void Parse_handles_decimals()
    {
        var stat = StatText.Parse("2.5% increased something");

        Assert.Equal("#% increased something", stat.Template);
        Assert.Equal([2.5d], stat.Numbers);
    }
}
