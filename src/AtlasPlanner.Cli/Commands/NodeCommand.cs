using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Cli.Commands;

internal static class NodeCommand
{
    public static int Run(CommandLine cli)
    {
        var context = Context.Load(cli);

        if (!int.TryParse(cli.Positional0 ?? cli.Value("id"), out var id))
            throw new ArgumentException("Provide a node id, e.g. 'atlasplanner node 65225'.");

        if (!context.Tree.TryGet(id, out var node) || node is null)
            throw new ArgumentException($"No allocatable node with id {id}.");

        var distances = context.Tree.Distances(context.Tree.StartNodeId);

        Console.WriteLine($"{node.Id}  {node.Name}");
        Console.WriteLine($"  kind        {node.Kind}{(node.IsGateway ? " (gateway)" : string.Empty)}");
        Console.WriteLine($"  region      {(node.Region.Length > 0 ? node.Region : "(none)")}");
        Console.WriteLine($"  group       {node.GroupId}, orbit {node.Orbit}, index {node.OrbitIndex}");
        Console.WriteLine($"  position    {node.Position.X:0.#}, {node.Position.Y:0.#}");
        Console.WriteLine($"  cost        {node.Cost}" +
                          (node.GrantedPoints > 0 ? $", grants {node.GrantedPoints} points" : string.Empty));
        Console.WriteLine($"  from start  {distances[context.Tree.IndexOf(node.Id)]} hop(s)");
        Console.WriteLine($"  neighbours  {string.Join(", ", node.Neighbours.Select(n => $"{n} ({Describe(context.Tree[n])})"))}");

        if (node.Stats.Count > 0)
        {
            Console.WriteLine("  stats");
            foreach (var stat in node.Stats)
            {
                var category = context.Scores.Classify(stat, node);
                Console.WriteLine($"    [{category}] {stat.Text}");
                Console.WriteLine($"      template: {stat.Template}" +
                                  (stat.Numbers.Count > 0 ? $"  numbers: {string.Join(", ", stat.Numbers)}" : string.Empty));
            }
        }

        foreach (var reminder in node.ReminderText)
            Console.WriteLine($"  reminder    {reminder}");
        foreach (var flavour in node.FlavourText)
            Console.WriteLine($"  flavour     {flavour}");

        return 0;
    }

    private static string Describe(AtlasNode node) =>
        node.Name.Length > 0 ? node.Name : node.Kind.ToString();
}
