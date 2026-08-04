using System.Numerics;
using AtlasPlanner.Core.Art;

namespace AtlasPlanner.Core.Tree;

/// <summary>
/// A cluster of nodes arranged on concentric orbits around a shared centre. Groups carry no game
/// meaning; they exist so the tree can be laid out and drawn.
/// </summary>
public sealed class AtlasGroup
{
    public required int Id { get; init; }

    /// <summary>Centre in tree space.</summary>
    public required Vector2 Position { get; init; }

    public required IReadOnlyList<int> Orbits { get; init; }

    /// <summary>Outermost orbit in use, which sets how large the group reads on screen.</summary>
    public required int MaxOrbit { get; init; }

    public IReadOnlyList<int> NodeIds { get; internal set; } = [];

    /// <summary>Mastery name of this group, e.g. "Bestiary". Empty when the group has no mastery.</summary>
    public string Region { get; internal set; } = string.Empty;

    /// <summary>Sprite key of the decorative ring behind this group, if it has one.</summary>
    public string? BackgroundKey => NodeArt.GroupBackgroundKey(MaxOrbit, Region);

    public override string ToString() => $"group {Id} @ {Position} (orbits to {MaxOrbit})";
}

/// <summary>
/// An undirected connection between two allocatable nodes. Connections between nodes sharing a
/// group and orbit are drawn as arcs along that orbit; everything else is a straight line.
/// </summary>
public readonly record struct AtlasEdge(int FromId, int ToId, int ArcGroupId, int ArcOrbit)
{
    public bool IsArc => ArcOrbit > 0;

    public static AtlasEdge Straight(int fromId, int toId) => new(fromId, toId, -1, 0);
}

/// <summary>An axis-aligned rectangle in tree space.</summary>
public readonly record struct TreeBounds(float MinX, float MinY, float MaxX, float MaxY)
{
    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;
    public Vector2 Centre => new((MinX + MaxX) / 2f, (MinY + MaxY) / 2f);
    public Vector2 TopLeft => new(MinX, MinY);

    public TreeBounds Expand(float margin) =>
        new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

    public static TreeBounds Around(IEnumerable<Vector2> points, float margin = 0f)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var any = false;

        foreach (var point in points)
        {
            any = true;
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        return any ? new TreeBounds(minX, minY, maxX, maxY).Expand(margin) : default;
    }
}
