using System.Globalization;
using System.Text.RegularExpressions;

namespace AtlasPlanner.Core.Stats;

/// <summary>
/// A single stat line off a node, split into a numberless <see cref="Template"/> and the
/// <see cref="Numbers"/> that were lifted out of it.
/// </summary>
public sealed class ParsedStat
{
    /// <summary>Cleaned display text, e.g. <c>"Your Maps have +2% chance to contain Abysses"</c>.</summary>
    public required string Text { get; init; }

    /// <summary>Text with every number replaced by <c>#</c>, used as the aggregation key.</summary>
    public required string Template { get; init; }

    public required IReadOnlyList<double> Numbers { get; init; }

    /// <summary>
    /// True when the line carries exactly one number, which is the only case where adding values
    /// across nodes is meaningful. Multi-number lines (tier ranges and the like) are counted, not summed.
    /// </summary>
    public bool IsSummable => Numbers.Count == 1;

    public double Value => Numbers.Count == 1 ? Numbers[0] : 0d;

    public override string ToString() => Text;
}

public static class StatText
{
    // Unsigned only, so that templates keep their leading "+"/"-" and "Tier 1-5" stays a range
    // rather than being read as 1 and -5.
    private static readonly Regex NumberRegex = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

    // PoE inline markup: "[ContainsAbyss|Abysses]" renders as "Abysses", "[Scarab]" as "Scarab".
    private static readonly Regex MarkupRegex = new(@"\[(?:[^\[\]|]*\|)?([^\[\]|]*)\]", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = MarkupRegex.Replace(raw, "$1");
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    public static ParsedStat Parse(string raw)
    {
        var text = Clean(raw);
        var numbers = new List<double>();

        var template = NumberRegex.Replace(text, match =>
        {
            numbers.Add(double.Parse(match.Value, CultureInfo.InvariantCulture));
            return "#";
        });

        return new ParsedStat { Text = text, Template = template, Numbers = numbers };
    }
}
