using System.Numerics;
using AtlasPlanner.Core.Tree;
using Avalonia;

namespace AtlasPlanner.Gui.Rendering;

/// <summary>
/// Maps tree space to screen space. <see cref="Scale"/> is screen pixels per tree unit, which is
/// also what selects the sprite zoom level to draw at.
/// </summary>
public sealed class Camera
{
    public const double MinScale = 0.02;

    /// <summary>
    /// Past the largest authored zoom level (0.5) sprites are upscaled, which stays acceptable for
    /// a while and is worth it for close inspection.
    /// </summary>
    public const double MaxScale = 1.2;

    /// <summary>Screen pixels per tree unit.</summary>
    public double Scale { get; private set; } = 0.05;

    /// <summary>Screen position of the tree-space origin.</summary>
    public Point Offset { get; private set; }

    public Point ToScreen(Vector2 tree) =>
        new(tree.X * Scale + Offset.X, tree.Y * Scale + Offset.Y);

    public Vector2 ToTree(Point screen) =>
        new((float)((screen.X - Offset.X) / Scale), (float)((screen.Y - Offset.Y) / Scale));

    /// <summary>The tree-space rectangle currently visible, for culling.</summary>
    public TreeBounds VisibleBounds(Size viewport, float margin = 0f)
    {
        var topLeft = ToTree(new Point(0, 0));
        var bottomRight = ToTree(new Point(viewport.Width, viewport.Height));
        return new TreeBounds(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y).Expand(margin);
    }

    public void Pan(Avalonia.Vector delta) => Offset += delta;

    /// <summary>Zooms about a fixed screen point, so the tree stays put under the cursor.</summary>
    public void ZoomAt(Point anchor, double factor)
    {
        var clamped = Math.Clamp(Scale * factor, MinScale, MaxScale);
        if (Math.Abs(clamped - Scale) < double.Epsilon)
            return;

        var treeAnchor = ToTree(anchor);
        Scale = clamped;
        Offset = new Point(anchor.X - treeAnchor.X * Scale, anchor.Y - treeAnchor.Y * Scale);
    }

    /// <summary>Frames <paramref name="bounds"/> in the viewport, leaving a fractional margin.</summary>
    public void FitTo(TreeBounds bounds, Size viewport, double margin = 0.02)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var usable = 1d - margin * 2d;
        Scale = Math.Clamp(
            Math.Min(viewport.Width * usable / bounds.Width, viewport.Height * usable / bounds.Height),
            MinScale,
            MaxScale);

        var centre = bounds.Centre;
        Offset = new Point(
            viewport.Width / 2d - centre.X * Scale,
            viewport.Height / 2d - centre.Y * Scale);
    }

    /// <summary>Recentres on a tree position without changing zoom.</summary>
    public void CentreOn(Vector2 target, Size viewport) =>
        Offset = new Point(
            viewport.Width / 2d - target.X * Scale,
            viewport.Height / 2d - target.Y * Scale);
}
