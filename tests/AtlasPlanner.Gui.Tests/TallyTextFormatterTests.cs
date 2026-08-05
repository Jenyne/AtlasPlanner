using AtlasPlanner.Gui.Rendering;

namespace AtlasPlanner.Gui.Tests;

public sealed class TallyTextFormatterTests
{
    [Fact]
    public void Highlights_total_and_mechanic_terms()
    {
        const string line = "Your Maps have +97% chance to contain Breaches";
        var segments = TallyTextFormatter.Build(line, "Breach", 97);

        Assert.Contains(segments, s => s.Highlight && s.Text == "+97%");
        Assert.Contains(segments, s => s.Highlight && s.Text.Equals("Breaches", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(segments, s => !s.Highlight && s.Text.Contains("Your Maps"));
    }

    [Fact]
    public void Abyss_uses_green_palette()
    {
        Assert.Equal("#6BCE6B", MechanicPalette.ColorFor("Abyss"));
    }

    /// <summary>
    /// Accents must clear WCAG AA against the panel background, and stay dimmer than body text
    /// (13:1) so no single mechanic dominates the panel.
    /// </summary>
    [Theory]
    [InlineData("Beyond")]
    [InlineData("Breach")]
    [InlineData("Atlas Memories")]
    [InlineData("Currency")]
    [InlineData("Harvest")]
    [InlineData("Gateways")]
    public void Accent_colours_stay_in_the_readable_band(string category)
    {
        var contrast = ContrastAgainstPanel(MechanicPalette.ColorFor(category));

        Assert.True(contrast >= 4.5, $"{category} is too dim: {contrast:0.00}:1");
        Assert.True(contrast <= 11.0, $"{category} is too bright: {contrast:0.00}:1");
    }

    private const string PanelBackground = "#1A1713";

    private static double ContrastAgainstPanel(string hex)
    {
        var accent = RelativeLuminance(hex);
        var panel = RelativeLuminance(PanelBackground);
        var (lighter, darker) = accent > panel ? (accent, panel) : (panel, accent);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = hex.TrimStart('#');
        var r = Channel(value[..2]);
        var g = Channel(value.Substring(2, 2));
        var b = Channel(value.Substring(4, 2));
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);

        static double Channel(string pair)
        {
            var raw = Convert.ToInt32(pair, 16) / 255d;
            return raw <= 0.03928 ? raw / 12.92 : Math.Pow((raw + 0.055) / 1.055, 2.4);
        }
    }
}
