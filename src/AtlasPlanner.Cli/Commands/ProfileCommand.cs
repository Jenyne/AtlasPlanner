using AtlasPlanner.Core.Solving;

namespace AtlasPlanner.Cli.Commands;

internal static class ProfileCommand
{
    public static int Run(CommandLine cli)
    {
        if (cli.Value("init") is { Length: > 0 } target)
        {
            if (File.Exists(target) && !cli.Has("force"))
                throw new InvalidOperationException($"'{target}' already exists. Pass --force to overwrite.");

            var directory = Path.GetDirectoryName(Path.GetFullPath(target));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            SolveProfile.Example().Save(target);
            Console.WriteLine($"wrote an example profile to {target}");
            Console.WriteLine("Edit the weights, then: atlasplanner solve --profile " + target);
            return 0;
        }

        var path = cli.Positional0 ?? cli.Value("in")
            ?? throw new ArgumentException("Provide a profile path, or --init <path> to write an example.");

        var context = Context.Load(cli);
        var profile = SolveProfile.Load(path);
        var prize = PrizeModel.Build(context.Tree, context.Scores, profile);

        Console.WriteLine($"Profile   {profile.Name}");
        Console.WriteLine($"Budget    {profile.Budget?.ToString() ?? $"{context.Tree.TotalPoints} (tree default)"}");
        Console.WriteLine($"Seed      {profile.Seed}, time limit {profile.TimeLimitMs} ms per branch");
        Console.WriteLine($"Unwav.    {profile.UnwaveringVision.ToString().ToLowerInvariant()}");
        Console.WriteLine();

        if (prize.Nullified.Count > 0)
        {
            Console.WriteLine($"Ruled out for switching off a weighted category ({prize.Nullified.Count}):");
            foreach (var node in prize.Nullified.OrderBy(n => n.Name, StringComparer.Ordinal))
                Console.WriteLine($"  {node.Name,-24} [{node.Category}] {node.Stat}");
            Console.WriteLine();
        }

        Console.WriteLine("Weights in force:");
        foreach (var (category, weight) in prize.EffectiveWeights.OrderByDescending(w => w.Value))
            Console.WriteLine($"  {weight,8:0.##}  {category}");

        Console.WriteLine();
        Console.WriteLine($"Required ({prize.RequiredIds.Count}):");
        foreach (var id in prize.RequiredIds.Order())
            Console.WriteLine($"  {context.Tree[id]}");

        if (prize.ForbiddenIds.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Forbidden ({prize.ForbiddenIds.Count}):");
            foreach (var id in prize.ForbiddenIds.Order().Take(20))
                Console.WriteLine($"  {context.Tree[id]}");
        }

        Console.WriteLine();
        Console.WriteLine("Highest-value nodes under these weights:");
        foreach (var node in context.Tree.Nodes.Values
                     .OrderByDescending(n => prize.PrizeOf(n.Id))
                     .ThenBy(n => n.Id)
                     .Take(cli.Value("top", 15)))
        {
            Console.WriteLine($"  {prize.PrizeOf(node.Id),9:0.##}  {node.Kind,-8} {node}");

            if (cli.Has("explain"))
            {
                foreach (var contribution in prize.Explain(node.Id, context.Scores, profile)
                             .Where(c => c.Value != 0d)
                             .OrderByDescending(c => Math.Abs(c.Value)))
                {
                    Console.WriteLine($"              {contribution.Value,8:0.##} = {contribution.Weight:0.##} x {contribution.Magnitude:0.##}  [{contribution.Category}] {contribution.Stat}");
                }
            }
        }

        return 0;
    }
}
