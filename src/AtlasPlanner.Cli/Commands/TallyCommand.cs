using System.Text.Json;
using AtlasPlanner.Core.Tally;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Cli.Commands;

internal static class TallyCommand
{
    public static int Run(CommandLine cli)
    {
        var context = Context.Load(cli);
        var nodes = context.ResolveNodeSet(cli);
        var tally = AtlasTally.Compute(context.Tree, nodes, context.Scores);

        if (cli.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(tally, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var budget = context.Tree.TotalPoints + tally.PointsGranted;
        Console.WriteLine($"Tree      {context.Tree.TreeName}  ({context.Tree.NodeCount} allocatable nodes)");
        Console.WriteLine($"Scores    {context.ScoresPath}");
        Console.WriteLine($"Points    {tally.PointsSpent} spent of {budget} available" +
                          (tally.PointsGranted > 0 ? $" (base {context.Tree.TotalPoints} +{tally.PointsGranted} granted)" : string.Empty));

        var disconnected = context.Tree.DisconnectedNodes(nodes);
        Console.WriteLine(disconnected.Count == 0
            ? "Connected yes"
            : $"Connected NO - {disconnected.Count} node(s) unreachable from the start node: {string.Join(", ", disconnected.Take(10))}");

        if (tally.Keystones.Count > 0)
            Console.WriteLine($"Keystones {string.Join(", ", tally.Keystones.Select(k => k.Name))}");

        if (tally.ExclusionsTaken.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Mechanics switched off:");
            foreach (var node in tally.ExclusionsTaken)
                Console.WriteLine($"  {node.Name,-24} {node.ExclusionStat?.Text}");
        }

        var filter = cli.Value("category");

        foreach (var group in tally.ByCategory())
        {
            if (filter is not null && !group.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            Console.WriteLine();
            Console.WriteLine($"== {group.Key} ==");
            foreach (var entry in group)
            {
                var prefix = entry.CountLabel.Length > 0 ? entry.CountLabel.PadLeft(10) : "          ";
                Console.WriteLine($"  {prefix}  {entry.Rendered}");
            }
        }

        return 0;
    }
}
