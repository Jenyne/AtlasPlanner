using AtlasPlanner.Core.Art;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Cli.Commands;

/// <summary>
/// Reports and prefetches the tree export's sprite art. The planner GUI downloads on demand, so
/// this exists mostly to warm the cache and to confirm the art manifest lines up with the tree.
/// </summary>
internal static class ArtCommand
{
    public static int Run(CommandLine cli)
    {
        var context = Context.Load(cli);
        var tree = context.Tree;
        var catalog = tree.Sprites;
        var cache = new SpriteCache(cli.Value("cache"));

        if (!catalog.HasArt)
        {
            Console.Error.WriteLine("error: this tree export ships no sprite manifest.");
            return 1;
        }

        Console.WriteLine($"tree        {tree.TreeName}  ({context.TreePath})");
        Console.WriteLine($"cache       {cache.Directory}");
        Console.WriteLine($"zoom levels {string.Join(", ", catalog.ZoomLevels)}");
        Console.WriteLine($"sheets      {catalog.Sheets.Count}");
        Console.WriteLine();

        ReportCoverage(tree, catalog);

        var missing = cache.Missing(catalog);
        Console.WriteLine();
        Console.WriteLine($"cached      {catalog.Sheets.Count - missing.Count}/{catalog.Sheets.Count} sheets");

        if (!cli.Has("fetch"))
        {
            if (missing.Count > 0)
                Console.WriteLine($"            run with --fetch to download the {missing.Count} missing sheet(s)");
            return 0;
        }

        if (missing.Count == 0)
        {
            Console.WriteLine("            nothing to fetch");
            return 0;
        }

        Console.WriteLine();
        var progress = new Progress<SpriteDownloadProgress>(p =>
            Console.WriteLine($"  [{p.Completed}/{p.Total}] {p.CurrentFile}"));

        var result = cache.EnsureAsync(catalog, progress).GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine($"downloaded  {result.Downloaded.Count} sheet(s)");
        foreach (var (sheet, error) in result.Failed)
            Console.Error.WriteLine($"failed      {sheet.CacheFileName}: {error}");

        return result.AllSucceeded ? 0 : 1;
    }

    /// <summary>
    /// Checks that every node and group actually resolves to a sprite, at the largest zoom level.
    /// A gap here means the renderer would silently drop art.
    /// </summary>
    private static void ReportCoverage(AtlasTree tree, SpriteCatalog catalog)
    {
        var zoom = catalog.ZoomLevels[^1];
        var missingIcons = new List<AtlasNode>();
        var missingFrames = new List<AtlasNode>();

        foreach (var node in tree.Nodes.Values.Concat(tree.Masteries))
        {
            var iconKey = NodeArt.IconKey(node);
            if (iconKey is not null
                && !catalog.TryGetSprite(NodeArt.IconCategory(node, allocated: true), zoom, iconKey, out _, out _))
            {
                missingIcons.Add(node);
            }

            var frameKey = NodeArt.FrameKey(node, NodeVisualState.Unallocated);
            if (frameKey is not null
                && !catalog.TryGetSprite(SpriteCategories.Frame, zoom, frameKey, out _, out _))
            {
                missingFrames.Add(node);
            }
        }

        var missingBackgrounds = tree.Groups.Values
            .Where(g => g.BackgroundKey is { } key
                        && !catalog.TryGetSprite(SpriteCategories.GroupBackground, zoom, key, out _, out _))
            .ToList();

        Console.WriteLine($"art coverage at zoom {zoom}:");
        Console.WriteLine($"  nodes            {tree.NodeCount + tree.Masteries.Count - missingIcons.Count}/{tree.NodeCount + tree.Masteries.Count} icons resolved");
        Console.WriteLine($"  frames           {missingFrames.Count} node(s) without a frame sprite");
        Console.WriteLine($"  group rings      {tree.Groups.Count - missingBackgrounds.Count}/{tree.Groups.Count} resolved");
        Console.WriteLine($"  edges            {tree.Edges.Count} ({tree.Edges.Count(e => e.IsArc)} arcs, {tree.Edges.Count(e => !e.IsArc)} straight)");

        foreach (var node in missingIcons.Take(10))
            Console.WriteLine($"    no icon: {node} icon='{node.Icon}'");
        if (missingIcons.Count > 10)
            Console.WriteLine($"    ... and {missingIcons.Count - 10} more");
    }
}
