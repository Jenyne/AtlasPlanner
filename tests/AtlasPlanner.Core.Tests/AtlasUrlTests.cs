using AtlasPlanner.Core.Io;

namespace AtlasPlanner.Core.Tests;

public class AtlasUrlTests
{
    [Fact]
    public void Encode_then_decode_preserves_the_node_set()
    {
        int[] ids = [29045, 63311, 42692, 65225, 1, 65535];

        var decoded = AtlasUrl.Decode(AtlasUrl.Encode(ids));

        Assert.Equal(ids.Order(), decoded.Order());
    }

    [Fact]
    public void Encode_produces_a_recognised_atlas_url()
    {
        var url = AtlasUrl.Encode([29045, 63311]);

        Assert.StartsWith("https://www.pathofexile.com/fullscreen-atlas-skill-tree/", url);
        Assert.True(AtlasUrl.IsAtlasUrl(url));
    }

    [Fact]
    public void Encode_survives_a_set_larger_than_a_byte_count_field()
    {
        var ids = Enumerable.Range(1000, 400).ToArray();

        var decoded = AtlasUrl.Decode(AtlasUrl.Encode(ids));

        Assert.Equal(ids.Length, decoded.Count);
    }

    [Theory]
    [InlineData("https://www.pathofexile.com/fullscreen-atlas-skill-tree/3.26.0/AAAABgAA")]
    [InlineData("https://www.pathofexile.com/fullscreen-atlas-skill-tree/3.29.0/AAAABgAA")]
    [InlineData("pathofexile.com/atlas-skill-tree/AAAABgAA")]
    public void Decode_accepts_league_prefixes_and_bare_hosts(string url) =>
        Assert.Empty(AtlasUrl.Decode(url));

    [Fact]
    public void IsAtlasUrl_rejects_a_character_tree_url() =>
        Assert.False(AtlasUrl.IsAtlasUrl("https://www.pathofexile.com/fullscreen-passive-skill-tree/AAAABgAA"));

    [Fact]
    public void Decode_rejects_input_that_is_not_a_tree_code() =>
        Assert.Throws<FormatException>(() => AtlasUrl.Decode("not a tree code at all"));
}
