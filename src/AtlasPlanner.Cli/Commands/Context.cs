using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Cli.Commands;

internal sealed class Context
{
    public required AtlasTree Tree { get; init; }
    public required ScoreTable Scores { get; init; }
    public required string TreePath { get; init; }
    public required string ScoresPath { get; init; }

    public static Context Load(CommandLine cli)
    {
        var treePath = DataLocator.ResolveTree(cli.Value("tree"));
        var scoresPath = DataLocator.ResolveScores(cli.Value("scores"));

        var tree = AtlasTree.Load(treePath);
        var scores = ScoreTable.LoadOrDefault(scoresPath);
        scores.PrepareFor(tree);

        return new Context
        {
            Tree = tree,
            Scores = scores,
            TreePath = treePath,
            ScoresPath = scoresPath ?? "(built-in defaults)",
        };
    }

    /// <summary>
    /// Resolves the node set a command should operate on, from <c>--url</c>, <c>--nodes</c>, or
    /// <c>--all</c>. The free start node is always included so connectivity checks line up.
    /// </summary>
    public HashSet<int> ResolveNodeSet(CommandLine cli)
    {
        HashSet<int> ids;

        if (cli.Value("url") is { Length: > 0 } url)
        {
            ids = AtlasUrl.Decode(url);
        }
        else if (cli.Value("nodes") is { Length: > 0 } list)
        {
            ids = list
                .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .ToHashSet();
        }
        else if (cli.Has("all"))
        {
            ids = Tree.Nodes.Keys.ToHashSet();
        }
        else
        {
            throw new ArgumentException("Provide --url <tree url>, --nodes <id,id,...>, or --all.");
        }

        ids.Add(Tree.StartNodeId);

        var unknown = ids.Where(id => !Tree.Nodes.ContainsKey(id)).Order().ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine(
                $"warning: {unknown.Count} id(s) are not allocatable nodes in this tree and were dropped: " +
                string.Join(", ", unknown.Take(20)));
            ids.RemoveWhere(id => !Tree.Nodes.ContainsKey(id));
        }

        return ids;
    }
}
