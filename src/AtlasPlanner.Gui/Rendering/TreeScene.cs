using System.Numerics;
using AtlasPlanner.Core.Art;
using AtlasPlanner.Core.Tree;
using Avalonia;
using Avalonia.Media;

namespace AtlasPlanner.Gui.Rendering;

/// <summary>A node as it appears on the canvas. Geometry is in tree units and never changes.</summary>
public sealed class NodeVisual
{
    public required AtlasNode Node { get; init; }
    public required Vector2 Position { get; init; }

    /// <summary>Half the frame sprite's width, in tree units.</summary>
    public required double FrameRadius { get; init; }

    /// <summary>Click target, slightly inside the frame so neighbouring nodes stay separable.</summary>
    public required double HitRadius { get; init; }

    public int Id => Node.Id;
}

/// <summary>
/// A connection as it appears on the canvas. Orbital connections carry a precomputed polyline;
/// everything else is a straight segment, both already trimmed back to the node frames.
/// </summary>
public sealed class EdgeVisual
{
    private Geometry? _arc;

    public required int FromId { get; init; }
    public required int ToId { get; init; }
    public required Point From { get; init; }
    public required Point To { get; init; }

    /// <summary>Sampled arc in tree space, for orbital connections only.</summary>
    public IReadOnlyList<Point>? ArcPoints { get; init; }

    /// <summary>Bounding box in tree space, for culling.</summary>
    public required TreeBounds Bounds { get; init; }

    public bool IsArc => ArcPoints is { Count: > 1 };

    /// <summary>
    /// The arc as a stroked geometry, built on first draw. Geometry construction needs an
    /// initialised render backend, so it deliberately does not happen while the scene is loading.
    /// </summary>
    public Geometry? Arc()
    {
        if (_arc is not null || ArcPoints is not { Count: > 1 } points)
            return _arc;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false);
            for (var i = 1; i < points.Count; i++)
                context.LineTo(points[i]);
            context.EndFigure(false);
        }

        return _arc = geometry;
    }
}

public sealed class GroupVisual
{
    public required Vector2 Position { get; init; }
    public required string BackgroundKey { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}

/// <summary>
/// Everything about drawing the tree that does not depend on allocation state or the camera, worked
/// out once at load so each frame is just transform, cull, and blit.
/// </summary>
public sealed class TreeScene
{
    /// <summary>Arcs are sampled at roughly this angular resolution.</summary>
    private const double ArcStepRadians = 0.06;

    private TreeScene(
        AtlasTree tree,
        IReadOnlyList<NodeVisual> nodes,
        IReadOnlyList<NodeVisual> masteries,
        IReadOnlyList<EdgeVisual> edges,
        IReadOnlyList<GroupVisual> groups,
        IReadOnlyDictionary<int, NodeVisual> byId)
    {
        Tree = tree;
        Nodes = nodes;
        Masteries = masteries;
        Edges = edges;
        Groups = groups;
        _byId = byId;
    }

    private readonly IReadOnlyDictionary<int, NodeVisual> _byId;

    public AtlasTree Tree { get; }
    public IReadOnlyList<NodeVisual> Nodes { get; }
    public IReadOnlyList<NodeVisual> Masteries { get; }
    public IReadOnlyList<EdgeVisual> Edges { get; }
    public IReadOnlyList<GroupVisual> Groups { get; }

    public NodeVisual? Visual(int nodeId) => _byId.GetValueOrDefault(nodeId);

    public static TreeScene Build(AtlasTree tree)
    {
        var catalog = tree.Sprites;

        var nodes = tree.Nodes.Values
            .Select(node => BuildNode(node, catalog))
            .ToArray();
        var byId = nodes.ToDictionary(visual => visual.Id);

        var masteries = tree.Masteries
            .Select(node => BuildNode(node, catalog))
            .ToArray();

        var edges = tree.Edges
            .Select(edge => BuildEdge(tree, edge, byId))
            .Where(edge => edge is not null)
            .Select(edge => edge!)
            .ToArray();

        var groups = tree.Groups.Values
            .Where(group => group.BackgroundKey is not null)
            .Select(group =>
            {
                var size = catalog.TreeSizeOf(SpriteCategories.GroupBackground, group.BackgroundKey!);
                return new GroupVisual
                {
                    Position = group.Position,
                    BackgroundKey = group.BackgroundKey!,
                    Width = size.Width,
                    Height = size.Height,
                };
            })
            .Where(group => group.Width > 0)
            // Largest first, so small groups are never buried under a bigger neighbour's ring.
            .OrderByDescending(group => group.Width)
            .ToArray();

        return new TreeScene(tree, nodes, masteries, edges, groups, byId);
    }

    private static NodeVisual BuildNode(AtlasNode node, SpriteCatalog catalog)
    {
        var frameKey = NodeArt.FrameKey(node, NodeVisualState.Unallocated);
        var size = frameKey is not null
            ? catalog.TreeSizeOf(SpriteCategories.Frame, frameKey)
            : default;

        if (size.IsEmpty)
        {
            // Masteries and the start node have no frame, so fall back to their own art.
            var iconKey = NodeArt.IconKey(node);
            size = iconKey is not null
                ? catalog.TreeSizeOf(NodeArt.IconCategory(node, allocated: false), iconKey)
                : catalog.TreeSizeOf(SpriteCategories.StartNode, NodeArt.StartNodeKey);
        }

        var frameRadius = size.IsEmpty ? 100d : size.Width / 2d;

        return new NodeVisual
        {
            Node = node,
            Position = node.Position,
            FrameRadius = frameRadius,
            HitRadius = frameRadius * SpriteCatalog.FrameInkFraction,
        };
    }

    private static EdgeVisual? BuildEdge(AtlasTree tree, AtlasEdge edge, Dictionary<int, NodeVisual> byId)
    {
        if (!byId.TryGetValue(edge.FromId, out var from) || !byId.TryGetValue(edge.ToId, out var to))
            return null;

        // Paired gateways link opposite ends of the atlas, and a straight line across the whole tree
        // says less about the shortcut than the wormhole art already does. The graph still has them.
        if (from.Node.IsGateway && to.Node.IsGateway)
            return null;

        return edge.IsArc && tree.Groups.TryGetValue(edge.ArcGroupId, out var group)
            ? BuildArc(tree, group, from, to)
            : BuildStraight(from, to);
    }

    private static EdgeVisual BuildStraight(NodeVisual from, NodeVisual to)
    {
        var direction = to.Position - from.Position;
        var length = direction.Length();
        if (length > 0.0001f)
            direction /= length;

        // Pull the ends back to the frames so connectors never cross a node's art.
        var start = from.Position + direction * (float)Math.Min(from.HitRadius, length / 2d);
        var end = to.Position - direction * (float)Math.Min(to.HitRadius, length / 2d);

        return new EdgeVisual
        {
            FromId = from.Id,
            ToId = to.Id,
            From = new Point(start.X, start.Y),
            To = new Point(end.X, end.Y),
            Bounds = TreeBounds.Around([start, end]),
        };
    }

    private static EdgeVisual BuildArc(AtlasTree tree, AtlasGroup group, NodeVisual from, NodeVisual to)
    {
        var radius = tree.RadiusOf(from.Node);
        if (radius <= 0)
            return BuildStraight(from, to);

        var startAngle = tree.AngleOf(from.Node);
        var sweep = NormaliseAngle(tree.AngleOf(to.Node) - startAngle);

        // Trim by the angle each node's frame subtends, so the arc meets the art rather than the centre.
        var trimFrom = Math.Min(from.HitRadius / radius, Math.Abs(sweep) / 3d);
        var trimTo = Math.Min(to.HitRadius / radius, Math.Abs(sweep) / 3d);
        var direction = Math.Sign(sweep);
        var a0 = startAngle + direction * trimFrom;
        var a1 = startAngle + sweep - direction * trimTo;

        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(a1 - a0) / ArcStepRadians));
        var points = new Point[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var angle = a0 + (a1 - a0) * i / steps;
            var point = group.Position + radius * new Vector2(MathF.Sin((float)angle), -MathF.Cos((float)angle));
            points[i] = new Point(point.X, point.Y);
        }

        return new EdgeVisual
        {
            FromId = from.Id,
            ToId = to.Id,
            From = points[0],
            To = points[^1],
            ArcPoints = points,
            Bounds = TreeBounds.Around(points.Select(p => new Vector2((float)p.X, (float)p.Y))),
        };
    }

    /// <summary>Folds an angle difference into (-pi, pi] so arcs always take the short way round.</summary>
    private static double NormaliseAngle(double radians)
    {
        while (radians <= -Math.PI) radians += Math.Tau;
        while (radians > Math.PI) radians -= Math.Tau;
        return radians;
    }

    /// <summary>
    /// The node under a tree-space point, preferring the closest when frames overlap. Masteries are
    /// ignored because they cannot be allocated.
    /// </summary>
    public NodeVisual? HitTest(Vector2 point)
    {
        NodeVisual? best = null;
        var bestDistance = double.MaxValue;

        foreach (var visual in Nodes)
        {
            var distance = Vector2.Distance(visual.Position, point);
            if (distance > visual.HitRadius || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = visual;
        }

        return best;
    }
}
