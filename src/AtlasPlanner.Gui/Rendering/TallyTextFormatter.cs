using System.Globalization;
using System.Text;

namespace AtlasPlanner.Gui.Rendering;

public readonly record struct TallyTextSegment(string Text, bool Highlight);

/// <summary>Splits a tally line so mechanic terms and the summed total can be tinted.</summary>
public static class TallyTextFormatter
{
    public static IReadOnlyList<TallyTextSegment> Build(string rendered, string category, double total)
    {
        if (rendered.Length == 0)
            return [];

        var spans = new List<(int Start, int End)>();

        if (total != 0d)
            AddNumberSpans(rendered, total, spans);

        foreach (var term in MechanicPalette.HighlightTermsFor(category))
            AddTermSpans(rendered, term, spans);

        if (spans.Count == 0)
            return [new TallyTextSegment(rendered, Highlight: false)];

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        spans = Merge(spans);

        var segments = new List<TallyTextSegment>();
        var cursor = 0;
        foreach (var (start, end) in spans)
        {
            if (start > cursor)
                segments.Add(new TallyTextSegment(rendered[cursor..start], Highlight: false));

            segments.Add(new TallyTextSegment(rendered[start..end], Highlight: true));
            cursor = end;
        }

        if (cursor < rendered.Length)
            segments.Add(new TallyTextSegment(rendered[cursor..], Highlight: false));

        return segments;
    }

    private static void AddNumberSpans(string text, double total, List<(int Start, int End)> spans)
    {
        var formatted = FormatNumber(total);
        foreach (var candidate in NumberCandidates(formatted))
        {
            var index = 0;
            while ((index = text.IndexOf(candidate, index, StringComparison.Ordinal)) >= 0)
            {
                spans.Add((index, index + candidate.Length));
                index += candidate.Length;
            }
        }
    }

    private static IEnumerable<string> NumberCandidates(string formatted)
    {
        yield return formatted;
        yield return "+" + formatted;
        yield return formatted + "%";
        yield return "+" + formatted + "%";
    }

    private static void AddTermSpans(string text, string term, List<(int Start, int End)> spans)
    {
        if (term.Length == 0)
            return;

        var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            spans.Add((index, index + term.Length));
            index += term.Length;
        }
    }

    private static List<(int Start, int End)> Merge(List<(int Start, int End)> spans)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var span in spans)
        {
            if (merged.Count == 0 || span.Start > merged[^1].End)
            {
                merged.Add(span);
                continue;
            }

            var last = merged[^1];
            merged[^1] = (last.Start, Math.Max(last.End, span.End));
        }

        return merged;
    }

    private static string FormatNumber(double value) =>
        value.ToString(value % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture);
}
