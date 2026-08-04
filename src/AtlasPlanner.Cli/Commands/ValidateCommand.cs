using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Cli.Commands;

/// <summary>
/// Checks the assumptions the planner is built on, so a league tree update that breaks one of them
/// is caught here rather than showing up as a nonsense route.
/// </summary>
internal static class ValidateCommand
{
    public static int Run(CommandLine cli)
    {
        var context = Context.Load(cli);
        var tree = context.Tree;
        var failures = 0;

        Console.WriteLine($"Tree    {context.TreePath}");
        Console.WriteLine($"Name    {tree.TreeName}");
        Console.WriteLine($"Nodes   {tree.NodeCount} allocatable, {tree.Masteries.Count} mastery labels");
        Console.WriteLine($"Start   {tree.StartNodeId} (cost {tree.Start.Cost})");
        Console.WriteLine($"Budget  {tree.TotalPoints} base points");
        Console.WriteLine();

        var distances = tree.Distances(tree.StartNodeId);
        var unreachable = Enumerable.Range(0, tree.NodeCount)
            .Where(i => distances[i] == TreeGraph.Unreachable)
            .Select(tree.IdAt)
            .ToList();

        Report(ref failures, unreachable.Count == 0,
            "every allocatable node is reachable from the start node",
            $"{unreachable.Count} unreachable: {string.Join(", ", unreachable.Take(20))}");

        var edges = tree.Nodes.Values.Sum(n => n.Neighbours.Length) / 2;
        var isolated = tree.Nodes.Values.Where(n => n.Neighbours.Length == 0).ToList();
        Console.WriteLine($"        {edges} undirected edges, max degree {tree.Nodes.Values.Max(n => n.Neighbours.Length)}");
        Report(ref failures, isolated.Count == 0,
            "no isolated allocatable nodes",
            $"{isolated.Count} isolated: {string.Join(", ", isolated.Take(10).Select(n => n.Id))}");

        var pointGranters = tree.Nodes.Values.Where(n => n.GrantedPoints > 0).ToList();
        Report(ref failures, pointGranters.Count <= 1,
            $"at most one point-granting node ({string.Join(", ", pointGranters.Select(n => $"{n.Name} +{n.GrantedPoints}"))})",
            $"{pointGranters.Count} point-granting nodes, so the budget can no longer be resolved by solving twice: " +
            string.Join(", ", pointGranters.Select(n => $"{n.Name} +{n.GrantedPoints}")));

        var gateways = tree.Nodes.Values.Where(n => n.IsGateway).ToList();
        var unpairedGateways = gateways.Where(g => !g.Neighbours.Any(n => tree[n].IsGateway)).ToList();
        Console.WriteLine($"        {gateways.Count} gateway nodes in {gateways.Count / 2} pair(s)");
        Report(ref failures, unpairedGateways.Count == 0,
            "every gateway is linked to its partner",
            $"{unpairedGateways.Count} gateway(s) with no gateway neighbour: {string.Join(", ", unpairedGateways.Select(g => g.Id))}");

        var markupLeftovers = tree.Nodes.Values
            .SelectMany(n => n.Stats)
            .Where(s => s.Text.Contains('[') || s.Text.Contains('|'))
            .Take(5)
            .ToList();
        Report(ref failures, markupLeftovers.Count == 0,
            "all inline stat markup was unwrapped",
            $"markup survived cleaning, e.g. \"{markupLeftovers.FirstOrDefault()?.Text}\"");

        var allIds = tree.Nodes.Keys.ToHashSet();
        var roundTripped = AtlasUrl.Decode(AtlasUrl.Encode(allIds));
        Report(ref failures, roundTripped.SetEquals(allIds),
            $"URL round-trip preserves all {allIds.Count} node ids",
            $"round-trip lost {allIds.Except(roundTripped).Count()} and gained {roundTripped.Except(allIds).Count()}");

        var uncategorised = tree.Nodes.Values
            .SelectMany(n => n.Stats.Select(s => (n, s)))
            .Count(pair => context.Scores.Classify(pair.s, pair.n) == ScoreTable.Uncategorised);
        Console.WriteLine($"        {uncategorised} stat line(s) fall through to '{ScoreTable.Uncategorised}'");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} check(s) FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void Report(ref int failures, bool ok, string okMessage, string failMessage)
    {
        if (ok)
        {
            Console.WriteLine($"  ok    {okMessage}");
        }
        else
        {
            failures++;
            Console.WriteLine($"  FAIL  {failMessage}");
        }
    }
}
