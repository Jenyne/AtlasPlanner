using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Cli.Commands;

internal static class SearchCommand
{
    public static int Run(CommandLine cli)
    {
        var context = Context.Load(cli);
        var term = cli.Positional0 ?? cli.Value("name");
        var region = cli.Value("region");
        var kindFilter = cli.Value("kind");
        var searchStats = cli.Has("stats");

        if (term is null && region is null && kindFilter is null)
            throw new ArgumentException("Provide a search term, --region <name>, or --kind <notable|keystone|normal>.");

        var matches = context.Tree.Nodes.Values.Where(node =>
        {
            if (region is not null && !node.Region.Contains(region, StringComparison.OrdinalIgnoreCase))
                return false;

            if (kindFilter is not null && !node.Kind.ToString().Equals(kindFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (term is null)
                return true;

            if (node.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            return searchStats && node.Stats.Any(s => s.Text.Contains(term, StringComparison.OrdinalIgnoreCase));
        });

        var results = matches
            .OrderBy(n => n.Region, StringComparer.Ordinal)
            .ThenBy(n => n.Kind)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"{results.Count} match(es)");

        foreach (var node in results)
        {
            Console.WriteLine();
            Console.WriteLine($"{node.Id,6}  {node.Kind,-8} {node.Region,-20} {node.Name}");
            foreach (var stat in node.Stats)
                Console.WriteLine($"          {stat.Text}");
        }

        return 0;
    }
}
