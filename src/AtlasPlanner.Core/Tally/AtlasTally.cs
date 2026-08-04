using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tally;

/// <summary>
/// What a set of allocated nodes adds up to.
/// </summary>
/// <remarks>
/// Atlas passives are additive against independent buckets, so summing single-number stat lines is
/// genuinely correct here — unlike character-tree stats, no calculation engine is needed. Lines that
/// cannot be honestly summed are kept apart rather than blended:
/// <list type="bullet">
/// <item><see cref="Summed"/> — one number per line, added up.</item>
/// <item><see cref="Repeated"/> — several numbers per line (tier ranges), counted by exact text.</item>
/// <item><see cref="Flags"/> — no numbers, listed by exact text.</item>
/// </list>
/// </remarks>
public sealed class AtlasTally
{
    public required IReadOnlyList<TallyEntry> Summed { get; init; }
    public required IReadOnlyList<TallyEntry> Repeated { get; init; }
    public required IReadOnlyList<TallyEntry> Flags { get; init; }

    public required int PointsSpent { get; init; }
    public required int PointsGranted { get; init; }

    /// <summary>Notables taken that switch a mechanic off, e.g. Dimensional Barrier.</summary>
    public required IReadOnlyList<AtlasNode> ExclusionsTaken { get; init; }

    public required IReadOnlyList<AtlasNode> Keystones { get; init; }
    public required IReadOnlyList<AtlasNode> Notables { get; init; }

    public IEnumerable<TallyEntry> AllEntries => Summed.Concat(Repeated).Concat(Flags);

    public IEnumerable<string> Categories =>
        AllEntries.Select(e => e.Category).Distinct().Order(StringComparer.Ordinal);

    public IEnumerable<IGrouping<string, TallyEntry>> ByCategory() =>
        AllEntries.GroupBy(e => e.Category).OrderBy(g => g.Key, StringComparer.Ordinal);

    public static AtlasTally Compute(AtlasTree tree, IEnumerable<int> allocatedIds, ScoreTable scores)
    {
        var nodes = allocatedIds
            .Distinct()
            .Select(id => tree.TryGet(id, out var node) ? node : null)
            .Where(node => node is not null)
            .Select(node => node!)
            .ToList();

        var summed = new Dictionary<string, TallyAccumulator>(StringComparer.Ordinal);
        var repeated = new Dictionary<string, TallyAccumulator>(StringComparer.Ordinal);
        var flags = new Dictionary<string, TallyAccumulator>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            foreach (var stat in node.Stats)
            {
                if (scores.IsIgnored(stat))
                    continue;

                var category = scores.Classify(stat, node);

                if (stat.IsSummable)
                {
                    Accumulate(summed, stat.Template, stat.Template, category, node, stat.Value);
                }
                else if (stat.Numbers.Count > 1)
                {
                    // Keyed by exact text so different tier ranges never merge.
                    Accumulate(repeated, stat.Text, stat.Text, category, node, 0d);
                }
                else
                {
                    Accumulate(flags, stat.Text, stat.Text, category, node, 0d);
                }
            }
        }

        return new AtlasTally
        {
            Summed = Finish(summed, byValue: true),
            Repeated = Finish(repeated, byValue: false),
            Flags = Finish(flags, byValue: false),
            PointsSpent = nodes.Sum(n => n.Cost),
            PointsGranted = nodes.Sum(n => n.GrantedPoints),
            ExclusionsTaken = nodes.Where(n => n.IsExclusionNotable).OrderBy(n => n.Name, StringComparer.Ordinal).ToArray(),
            Keystones = nodes.Where(n => n.Kind == NodeKind.Keystone).OrderBy(n => n.Name, StringComparer.Ordinal).ToArray(),
            Notables = nodes.Where(n => n.Kind == NodeKind.Notable).OrderBy(n => n.Name, StringComparer.Ordinal).ToArray(),
        };
    }

    private static void Accumulate(
        Dictionary<string, TallyAccumulator> into,
        string key,
        string display,
        string category,
        AtlasNode node,
        double value)
    {
        if (!into.TryGetValue(key, out var accumulator))
        {
            accumulator = new TallyAccumulator { Display = display, Category = category };
            into[key] = accumulator;
        }

        accumulator.Total += value;
        accumulator.Nodes.Add(node);
    }

    private static TallyEntry[] Finish(Dictionary<string, TallyAccumulator> source, bool byValue) =>
        source.Values
            .Select(a => new TallyEntry
            {
                Text = a.Display,
                Category = a.Category,
                Total = a.Total,
                NodeCount = a.Nodes.Count,
                Nodes = a.Nodes.Select(n => n.Id).Order().ToArray(),
            })
            .OrderBy(e => e.Category, StringComparer.Ordinal)
            .ThenByDescending(e => byValue ? e.Total : e.NodeCount)
            .ThenBy(e => e.Text, StringComparer.Ordinal)
            .ToArray();

    private sealed class TallyAccumulator
    {
        public required string Display { get; init; }
        public required string Category { get; init; }
        public double Total { get; set; }
        public List<AtlasNode> Nodes { get; } = [];
    }
}

public sealed class TallyEntry
{
    /// <summary>Template for summed entries (numbers as <c>#</c>), exact text otherwise.</summary>
    public required string Text { get; init; }

    public required string Category { get; init; }

    /// <summary>Sum of values for summable lines; zero for repeated and flag lines.</summary>
    public required double Total { get; init; }

    public required int NodeCount { get; init; }

    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>Template with <c>#</c> replaced by the summed total, e.g. "240% increased Scarabs...".</summary>
    public string Rendered =>
        Total == 0d && !Text.Contains('#')
            ? Text
            : ReplaceFirstHash(Text, Total.ToString(Total % 1 == 0 ? "0" : "0.##"));

    private static string ReplaceFirstHash(string text, string value)
    {
        var at = text.IndexOf('#');
        return at < 0 ? text : string.Concat(text.AsSpan(0, at), value, text.AsSpan(at + 1));
    }

    public override string ToString() => Rendered;
}
