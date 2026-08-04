using AtlasPlanner.Core.Categorization;

namespace AtlasPlanner.Cli.Commands;

/// <summary>
/// Dumps how every stat template in the tree classifies, so the score table can be reviewed and
/// corrected before any of it is used to steer the solver.
/// </summary>
internal static class CategoriesCommand
{
    public static int Run(CommandLine cli)
    {
        var context = Context.Load(cli);
        var onlyUncategorised = cli.Has("uncategorised") || cli.Has("uncategorized");

        var rows = context.Tree.Nodes.Values
            .SelectMany(node => node.Stats.Select(stat => (Node: node, Stat: stat)))
            .Select(pair => new
            {
                Category = context.Scores.Classify(pair.Stat, pair.Node),
                pair.Stat.Template,
                Summable = pair.Stat.IsSummable,
                pair.Node.Region,
            })
            .GroupBy(row => (row.Category, row.Template))
            .Select(group => new
            {
                group.Key.Category,
                group.Key.Template,
                Count = group.Count(),
                group.First().Summable,
                Regions = group.Select(r => r.Region).Where(r => r.Length > 0).Distinct().Order(StringComparer.Ordinal).ToArray(),
            })
            .Where(row => !onlyUncategorised || row.Category == ScoreTable.Uncategorised)
            .OrderBy(row => row.Category, StringComparer.Ordinal)
            .ThenByDescending(row => row.Count)
            .ThenBy(row => row.Template, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"Scores    {context.ScoresPath}");
        Console.WriteLine($"{rows.Count} distinct (category, template) pair(s)");

        string? currentCategory = null;
        foreach (var row in rows)
        {
            if (row.Category != currentCategory)
            {
                currentCategory = row.Category;
                Console.WriteLine();
                Console.WriteLine($"== {currentCategory} ==");
            }

            var marker = row.Summable ? "+" : " ";
            Console.WriteLine($" {marker}{row.Count,4}x  {row.Template}");

            if (cli.Has("regions") && row.Regions.Length > 0)
                Console.WriteLine($"            regions: {string.Join(", ", row.Regions)}");
        }

        Console.WriteLine();
        Console.WriteLine("'+' marks templates with a single number, which are the ones that get summed.");
        return 0;
    }
}
