using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;

namespace AtlasPlanner.Cli.Commands;

internal static class SolveCommand
{
    public static int Run(CommandLine cli)
    {
        var context = Context.Load(cli);

        var profile = cli.Value("profile") is { Length: > 0 } profilePath
            ? SolveProfile.Load(profilePath)
            : throw new ArgumentException("Provide --profile <path>. Create one with 'atlasplanner profile --init my.json'.");

        if (cli.Value("budget") is { Length: > 0 })
            profile.Budget = cli.Value("budget", profile.Budget ?? context.Tree.TotalPoints);

        if (cli.Value("time", 0) > 0)
            profile.TimeLimitMs = cli.Value("time", profile.TimeLimitMs);

        // Continue from the tree you actually have, when given one.
        if (cli.Value("from") is { Length: > 0 } from)
            profile.PreAllocated = AtlasUrl.Decode(from).Where(context.Tree.Nodes.ContainsKey).ToList();

        if (profile.Weights.Count == 0 && profile.Require.Count == 0)
            Console.Error.WriteLine("warning: the profile has no weights and no required nodes, so there is nothing to optimise.");

        var unknownCategories = profile.Weights.Keys
            .Except(context.Scores.KnownCategories(context.Tree), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknownCategories.Count > 0)
            Console.Error.WriteLine(
                $"warning: these weighted categories do not exist for this tree and will do nothing: {string.Join(", ", unknownCategories)}. " +
                "Run 'atlasplanner scores' to list valid categories.");

        if (cli.Value("unwavering") is { Length: > 0 } toggle)
        {
            profile.UnwaveringVision = Enum.TryParse<PointGrantChoice>(toggle, ignoreCase: true, out var parsed)
                ? parsed
                : throw new ArgumentException("--unwavering must be auto, include, or exclude.");
        }

        var prize = PrizeModel.Build(context.Tree, context.Scores, profile);

        if (prize.ZeroedByRequirement.Count > 0)
        {
            Console.WriteLine("Categories zeroed because a node you asked for switches them off:");
            foreach (var node in prize.ZeroedByRequirement.OrderBy(n => n.Category, StringComparer.Ordinal))
                Console.WriteLine($"  {node.Category,-20} {node.Name}: {node.Stat}");
            Console.WriteLine();
        }

        if (prize.Nullified.Count > 0 && !cli.Has("quiet"))
        {
            Console.WriteLine($"Ruled out {prize.Nullified.Count} node(s) that would switch off a category you asked for:");
            foreach (var node in prize.Nullified.OrderBy(n => n.Name, StringComparer.Ordinal))
                Console.WriteLine($"  {node.Name,-24} [{node.Category}] {node.Stat}");
            Console.WriteLine();
        }

        var solution = RouteSolver.Solve(context.Tree, prize, profile);

        Console.WriteLine($"Unwavering Vision: {profile.UnwaveringVision.ToString().ToLowerInvariant()}" +
                          $" -> {(solution.Stats.UsedPointGrantBranch ? "taken, budget raised" : "not taken")}");
        Console.WriteLine($"Solved in {solution.Stats.ElapsedMs} ms " +
                          $"(seed {solution.Stats.SeedPoints} pts, {solution.Stats.LocalSearchImprovements} improvement(s) " +
                          $"over {solution.Stats.LocalSearchAttempts} local search attempt(s))");
        Console.WriteLine();

        var plan = AtlasPlan.Build(context.Tree, context.Scores, profile, prize, solution);

        if (cli.Value("out") is { Length: > 0 } outPath)
        {
            plan.Save(outPath);
            Console.WriteLine($"wrote plan to {outPath}");
            Console.WriteLine();
        }

        PlanCommand.Print(plan, context, cli);
        return 0;
    }
}
