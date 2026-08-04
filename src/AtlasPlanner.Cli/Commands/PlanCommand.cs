using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Cli.Commands;

internal static class PlanCommand
{
    public static int Run(CommandLine cli)
    {
        var path = cli.Positional0 ?? cli.Value("in")
            ?? throw new ArgumentException("Provide a plan path, e.g. 'atlasplanner plan myplan.json'.");

        Print(AtlasPlan.Load(path), Context.Load(cli), cli);
        return 0;
    }

    public static void Print(AtlasPlan plan, Context context, CommandLine cli)
    {
        Console.WriteLine($"Plan      {plan.Name}");
        Console.WriteLine($"Points    {plan.PointsSpent} of {plan.Budget}" +
                          (plan.PointsGranted > 0 ? $" (includes +{plan.PointsGranted} granted)" : string.Empty) +
                          (plan.Budget > plan.PointsSpent ? $", {plan.Budget - plan.PointsSpent} unspent" : string.Empty));
        Console.WriteLine($"Score     {plan.Prize:0.##}");
        Console.WriteLine($"Nodes     {plan.Nodes.Count} ({plan.Order.Count} to allocate)");

        var connected = context.Tree.IsConnected(plan.Nodes.ToHashSet());
        Console.WriteLine($"Connected {(connected ? "yes" : "NO")}");

        var notables = plan.Order.Where(s => s.Kind is "Notable" or "Keystone").ToList();
        var filler = plan.Order.Count - notables.Count;
        Console.WriteLine($"Content   {notables.Count(s => s.Kind == "Keystone")} keystone(s), " +
                          $"{notables.Count(s => s.Kind == "Notable")} notable(s), {filler} connector(s)");

        Console.WriteLine();
        Console.WriteLine($"URL       {plan.Url}");

        if (!cli.Has("no-order"))
        {
            var limit = cli.Value("steps", plan.Order.Count);

            Console.WriteLine();
            Console.WriteLine($"== Allocation order (first {Math.Min(limit, plan.Order.Count)} of {plan.Order.Count}) ==");

            foreach (var step in plan.Order.Take(limit))
            {
                var marker = step.Kind switch
                {
                    "Keystone" => "K",
                    "Notable" => "N",
                    _ => " ",
                };

                Console.WriteLine($"  {step.Step,3} {marker} {step.Describe()}" +
                                  (step.GrantsPoints is { } granted ? $"  [+{granted} points]" : string.Empty));
            }
        }

        if (!cli.Has("no-tally"))
        {
            Console.WriteLine();
            Console.WriteLine("== Tally ==");

            foreach (var group in plan.Tally.Summed.Concat(plan.Tally.Repeated).Concat(plan.Tally.Flags)
                         .GroupBy(e => e.Category)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Console.WriteLine();
                Console.WriteLine($"-- {group.Key} --");
                foreach (var entry in group)
                    Console.WriteLine($"  {entry.NodeCount,3}x  {entry.Text}");
            }
        }
    }
}
