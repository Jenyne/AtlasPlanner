using System.Globalization;
using System.Numerics;
using AtlasPlanner.Core.Art;
using AtlasPlanner.Core.Tree;
using AtlasPlanner.Gui.Rendering;
using AtlasPlanner.Gui.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AtlasPlanner.Gui.Controls;

/// <summary>
/// Draws the atlas tree from the game's own sprite art and turns pointer input into allocation
/// requests. Everything is drawn under a single tree-space transform, so geometry can be
/// precomputed once in <see cref="TreeScene"/>.
/// </summary>
public sealed class AtlasTreeCanvas : Control
{
    /// <summary>Pointer travel, in pixels, past which a press becomes a pan instead of a click.</summary>
    private const double DragThreshold = 4d;

    private const double WheelZoomStep = 1.15d;

    /// <summary>Extra tree units culled in, so half-visible art does not pop at the edges.</summary>
    private const float CullMargin = 600f;

    /// <summary>
    /// Connector width in tree units. The line sprites are a 13 pixel strip at zoom 0.3835, most of
    /// which is glow, so the drawn line is deliberately narrower than that.
    /// </summary>
    private const double ConnectorWidth = 13d;

    // Colours sampled from the line sprites: allocated connectors are cold blue-white, untaken ones
    // are dim bronze, and half-taken ones sit in between.
    private static readonly ImmutablePen ActivePen =
        new(new ImmutableSolidColorBrush(Color.FromRgb(0xD1, 0xE6, 0xFF)), ConnectorWidth);

    private static readonly ImmutablePen IntermediatePen =
        new(new ImmutableSolidColorBrush(Color.FromRgb(0x6E, 0x7A, 0x9E)), ConnectorWidth * 0.85);

    private static readonly ImmutablePen NormalPen =
        new(new ImmutableSolidColorBrush(Color.FromArgb(0xB4, 0x9A, 0x79, 0x39)), ConnectorWidth * 0.75);

    private static readonly ImmutablePen PlannedPen =
        new(new ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xA9, 0x3C)), ConnectorWidth);

    private static readonly ImmutableSolidColorBrush PlannedRingBrush = new(Color.FromRgb(0xE0, 0xA9, 0x3C));
    private static readonly ImmutableSolidColorBrush HighlightRingBrush = new(Color.FromRgb(0x62, 0xD0, 0xFF));
    private static readonly ImmutableSolidColorBrush RequiredRingBrush = new(Color.FromRgb(0x4C, 0xE0, 0x70));
    private static readonly ImmutableSolidColorBrush ForbiddenRingBrush = new(Color.FromRgb(0xE0, 0x4C, 0x4C));
    private static readonly ImmutableSolidColorBrush StepTextBrush = new(Colors.White);
    private static readonly ImmutableSolidColorBrush StepBackBrush = new(Color.FromArgb(0xD0, 0x10, 0x20, 0x2E));

    private readonly Camera _camera = new();
    private readonly Dictionary<double, ImageBrush> _tileBrushes = [];
    private readonly Typeface _typeface = new("Inter, Segoe UI, sans-serif");

    private Point _pressOrigin;
    private Point _lastPointer;
    private bool _isPanning;
    private bool _pressIsPan;
    private bool _hasFitted;
    private int? _hoveredNodeId;

    /// <summary>
    /// Modifiers held when the press started. Read from the release event as well, but the press
    /// snapshot is the one that matters: by release Avalonia can have dropped Alt on Windows.
    /// </summary>
    private KeyModifiers _pressModifiers;

    public AtlasTreeCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public static readonly StyledProperty<PlannerSession?> SessionProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, PlannerSession?>(nameof(Session));

    public static readonly StyledProperty<SpriteImages?> ImagesProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, SpriteImages?>(nameof(Images));

    public static readonly StyledProperty<bool> ShowBackgroundProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, bool>(nameof(ShowBackground), true);

    public static readonly StyledProperty<bool> ShowStepNumbersProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, bool>(nameof(ShowStepNumbers), true);

    public static readonly StyledProperty<IReadOnlyDictionary<int, int>?> PlanStepsProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, IReadOnlyDictionary<int, int>?>(nameof(PlanSteps));

    public static readonly StyledProperty<IReadOnlySet<int>?> HighlightedProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, IReadOnlySet<int>?>(nameof(Highlighted));

    public static readonly StyledProperty<IReadOnlySet<int>?> RequiredNodesProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, IReadOnlySet<int>?>(nameof(RequiredNodes));

    public static readonly StyledProperty<IReadOnlySet<int>?> ForbiddenNodesProperty =
        AvaloniaProperty.Register<AtlasTreeCanvas, IReadOnlySet<int>?>(nameof(ForbiddenNodes));

    static AtlasTreeCanvas()
    {
        AffectsRender<AtlasTreeCanvas>(
            SessionProperty,
            ImagesProperty,
            ShowBackgroundProperty,
            ShowStepNumbersProperty,
            PlanStepsProperty,
            HighlightedProperty,
            RequiredNodesProperty,
            ForbiddenNodesProperty);
    }

    public PlannerSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public SpriteImages? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    public bool ShowBackground
    {
        get => GetValue(ShowBackgroundProperty);
        set => SetValue(ShowBackgroundProperty, value);
    }

    public bool ShowStepNumbers
    {
        get => GetValue(ShowStepNumbersProperty);
        set => SetValue(ShowStepNumbersProperty, value);
    }

    /// <summary>Node id to 1-based allocation step, drawn over the planned route.</summary>
    public IReadOnlyDictionary<int, int>? PlanSteps
    {
        get => GetValue(PlanStepsProperty);
        set => SetValue(PlanStepsProperty, value);
    }

    /// <summary>Nodes to ring, used for search results and for previewing a solved route.</summary>
    public IReadOnlySet<int>? Highlighted
    {
        get => GetValue(HighlightedProperty);
        set => SetValue(HighlightedProperty, value);
    }

    /// <summary>Nodes marked Must take — green ring so they stand out before solving.</summary>
    public IReadOnlySet<int>? RequiredNodes
    {
        get => GetValue(RequiredNodesProperty);
        set => SetValue(RequiredNodesProperty, value);
    }

    /// <summary>Nodes marked Never take — red ring so the solver's bans are visible on the tree.</summary>
    public IReadOnlySet<int>? ForbiddenNodes
    {
        get => GetValue(ForbiddenNodesProperty);
        set => SetValue(ForbiddenNodesProperty, value);
    }

    /// <summary>Left click on a node: the user wants it allocated.</summary>
    public event EventHandler<int>? NodeActivated;

    /// <summary>Ctrl+left click: mark the node as Must take for the next solve.</summary>
    public event EventHandler<int>? NodeRequired;

    /// <summary>Alt+left click: mark the node as Never take for the next solve.</summary>
    public event EventHandler<int>? NodeForbidden;

    /// <summary>Right click on a node: the user wants it removed.</summary>
    public event EventHandler<int>? NodeRemoved;

    public event EventHandler<AtlasNode?>? HoveredNodeChanged;

    /// <summary>Reports the camera scale in screen pixels per tree unit whenever it changes.</summary>
    public event EventHandler<double>? ScaleChanged;

    public double Scale => _camera.Scale;

    public void FitToTree()
    {
        ApplyFit();
        Redraw();
    }

    private void Redraw()
    {
        ScaleChanged?.Invoke(this, _camera.Scale);
        InvalidateVisual();
    }

    /// <summary>
    /// Frames the whole tree without asking for a redraw, so it is also safe to call from inside a
    /// render pass as a first-frame fallback.
    /// </summary>
    private void ApplyFit()
    {
        if (Session is null || Bounds.Width <= 0)
            return;

        _camera.FitTo(Session.Tree.ArtBounds, Bounds.Size);
        _hasFitted = true;
    }

    public void CentreOn(int nodeId)
    {
        if (Session?.Scene.Visual(nodeId) is not { } visual || Bounds.Width <= 0)
            return;

        _camera.CentreOn(visual.Position, Bounds.Size);
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SessionProperty)
        {
            _hasFitted = false;
            _hoveredNodeId = null;

            // The session usually arrives after layout, so this is the fit that normally wins.
            if (Bounds.Width > 0)
                FitToTree();
        }
        else if (change.Property == BoundsProperty && !_hasFitted && Session is not null && Bounds.Width > 0)
        {
            FitToTree();
        }
    }

    public override void Render(DrawingContext context)
    {
        // A background is always painted so the control never shows through as a hole.
        context.FillRectangle(new ImmutableSolidColorBrush(Color.FromRgb(0x0B, 0x0A, 0x08)), new Rect(Bounds.Size));

        if (Session is not { } session || Images is not { } images)
            return;

        if (!_hasFitted)
            ApplyFit();

        var scene = session.Scene;
        var catalog = session.Tree.Sprites;
        var zoom = catalog.ZoomFor(_camera.Scale);
        var visible = _camera.VisibleBounds(Bounds.Size, CullMargin);

        using var _ = context.PushTransform(
            Matrix.CreateScale(_camera.Scale, _camera.Scale) *
            Matrix.CreateTranslation(_camera.Offset.X, _camera.Offset.Y));

        if (ShowBackground)
            DrawBackdrop(context, session.Tree, catalog, images, zoom, visible);

        DrawGroupRings(context, scene, catalog, images, zoom, visible);
        DrawConnectors(context, scene, session, visible);
        DrawNodes(context, scene, session, catalog, images, zoom, visible);
    }

    private void DrawBackdrop(
        DrawingContext context,
        AtlasTree tree,
        SpriteCatalog catalog,
        SpriteImages images,
        double zoom,
        TreeBounds visible)
    {
        if (TileBrush(catalog, images, zoom) is { } tile)
        {
            var area = new Rect(visible.MinX, visible.MinY, visible.Width, visible.Height);
            context.FillRectangle(tile, area);
        }

        if (catalog.TryGetSprite(SpriteCategories.AtlasBackground, zoom, NodeArt.AtlasBackgroundKey, out var sheet, out var rect)
            && images.Get(sheet) is { } bitmap)
        {
            // The art was authored for the whole tree, so it is stretched to the layout extent
            // rather than placed at its native size.
            var bounds = tree.ArtBounds;
            context.DrawImage(
                bitmap,
                SpriteImages.ToRect(rect),
                new Rect(bounds.MinX, bounds.MinY, bounds.Width, bounds.Height));
        }
    }

    /// <summary>
    /// Tiling brushes are rebuilt only when the zoom level changes, since constructing one per frame
    /// is enough to show up while panning.
    /// </summary>
    private ImageBrush? TileBrush(SpriteCatalog catalog, SpriteImages images, double zoom)
    {
        if (_tileBrushes.TryGetValue(zoom, out var cached))
            return cached;

        if (!catalog.TryGetSprite(SpriteCategories.Background, zoom, NodeArt.BackgroundTileKey, out var sheet, out var rect)
            || images.Get(sheet) is not { } bitmap)
        {
            return null;
        }

        var size = rect.Width / zoom;
        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            SourceRect = new RelativeRect(SpriteImages.ToRect(rect), RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
            Opacity = 0.65,
        };

        _tileBrushes[zoom] = brush;
        return brush;
    }

    private static void DrawGroupRings(
        DrawingContext context,
        TreeScene scene,
        SpriteCatalog catalog,
        SpriteImages images,
        double zoom,
        TreeBounds visible)
    {
        foreach (var group in scene.Groups)
        {
            var half = new Vector2((float)group.Width / 2f, (float)group.Height / 2f);
            if (!Intersects(visible, group.Position - half, group.Position + half))
                continue;

            if (!catalog.TryGetSprite(SpriteCategories.GroupBackground, zoom, group.BackgroundKey, out var sheet, out var rect)
                || images.Get(sheet) is not { } bitmap)
            {
                continue;
            }

            context.DrawImage(
                bitmap,
                SpriteImages.ToRect(rect),
                new Rect(
                    group.Position.X - group.Width / 2d,
                    group.Position.Y - group.Height / 2d,
                    group.Width,
                    group.Height));
        }
    }

    private void DrawConnectors(DrawingContext context, TreeScene scene, PlannerSession session, TreeBounds visible)
    {
        var planned = PlanSteps;

        foreach (var edge in scene.Edges)
        {
            if (!Intersects(visible, edge.Bounds))
                continue;

            var fromAllocated = session.IsAllocated(edge.FromId);
            var toAllocated = session.IsAllocated(edge.ToId);

            var pen = NodeArt.StateFor(fromAllocated, toAllocated) switch
            {
                ConnectorState.Active => ActivePen,
                ConnectorState.Intermediate => IntermediatePen,
                _ => planned is not null && planned.ContainsKey(edge.FromId) && planned.ContainsKey(edge.ToId)
                    ? PlannedPen
                    : NormalPen,
            };

            if (edge.Arc() is { } arc)
                context.DrawGeometry(null, pen, arc);
            else
                context.DrawLine(pen, edge.From, edge.To);
        }
    }

    private void DrawNodes(
        DrawingContext context,
        TreeScene scene,
        PlannerSession session,
        SpriteCatalog catalog,
        SpriteImages images,
        double zoom,
        TreeBounds visible)
    {
        DrawSprite(context, catalog, images, zoom, SpriteCategories.StartNode, NodeArt.StartNodeKey, session.Tree.Start.Position);

        foreach (var mastery in scene.Masteries)
        {
            if (!InView(visible, mastery))
                continue;

            if (NodeArt.IconKey(mastery.Node) is { } key)
                DrawSprite(context, catalog, images, zoom, SpriteCategories.Mastery, key, mastery.Position);
        }

        var highlighted = Highlighted;
        var planned = PlanSteps;

        foreach (var visual in scene.Nodes)
        {
            if (visual.Node.Kind == NodeKind.Start || !InView(visible, visual))
                continue;

            var allocated = session.IsAllocated(visual.Id);
            var state = allocated
                ? NodeVisualState.Allocated
                : _hoveredNodeId == visual.Id
                    ? NodeVisualState.Highlighted
                    : session.Reachable.Contains(visual.Id)
                        ? NodeVisualState.CanAllocate
                        : NodeVisualState.Unallocated;

            if (NodeArt.IconKey(visual.Node) is { } iconKey)
            {
                DrawSprite(
                    context,
                    catalog,
                    images,
                    zoom,
                    NodeArt.IconCategory(visual.Node, allocated),
                    iconKey,
                    visual.Position);
            }

            if (NodeArt.FrameKey(visual.Node, state) is { } frameKey)
                DrawSprite(context, catalog, images, zoom, SpriteCategories.Frame, frameKey, visual.Position);

            // Rings call out planned steps, search hits, and Must-take / Never-take marks.
            var isPlanned = planned is not null && !allocated && planned.ContainsKey(visual.Id);
            var isHighlighted = highlighted is not null && highlighted.Contains(visual.Id);
            var isRequired = RequiredNodes is not null && RequiredNodes.Contains(visual.Id);
            var isForbidden = ForbiddenNodes is not null && ForbiddenNodes.Contains(visual.Id);

            if (isPlanned || isHighlighted || isRequired || isForbidden)
            {
                var radius = visual.FrameRadius * 0.95;
                var brush = isForbidden
                    ? ForbiddenRingBrush
                    : isRequired
                        ? RequiredRingBrush
                        : isHighlighted
                            ? HighlightRingBrush
                            : PlannedRingBrush;
                var width = (isRequired || isForbidden ? 0.2 : 0.14) * visual.FrameRadius;
                context.DrawEllipse(
                    null,
                    new ImmutablePen(brush, width),
                    new Point(visual.Position.X, visual.Position.Y),
                    radius,
                    radius);
            }
        }

        if (ShowStepNumbers && planned is { Count: > 0 })
            DrawStepNumbers(context, scene, session, planned);
    }

    /// <summary>
    /// Step labels are drawn in tree space so they stay pinned to their nodes, with the font size
    /// compensating for zoom so they stay readable.
    /// </summary>
    private void DrawStepNumbers(
        DrawingContext context,
        TreeScene scene,
        PlannerSession session,
        IReadOnlyDictionary<int, int> steps)
    {
        var fontSize = 13d / _camera.Scale;
        if (_camera.Scale < 0.06)
            return;

        foreach (var (nodeId, step) in steps)
        {
            if (scene.Visual(nodeId) is not { } visual)
                continue;

            var text = new FormattedText(
                step.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                fontSize,
                StepTextBrush);

            var origin = new Point(
                visual.Position.X - text.Width / 2d,
                visual.Position.Y + visual.FrameRadius * 0.55);

            context.FillRectangle(
                StepBackBrush,
                new Rect(origin.X - fontSize * 0.2, origin.Y, text.Width + fontSize * 0.4, text.Height),
                (float)(fontSize * 0.25));
            context.DrawText(text, origin);
        }
    }

    private static void DrawSprite(
        DrawingContext context,
        SpriteCatalog catalog,
        SpriteImages images,
        double zoom,
        string category,
        string key,
        Vector2 centre)
    {
        if (!catalog.TryGetSprite(category, zoom, key, out var sheet, out var rect)
            || images.Get(sheet) is not { } bitmap)
        {
            return;
        }

        var width = rect.Width / (sheet.Zoom * SpriteCatalog.SpriteScale);
        var height = rect.Height / (sheet.Zoom * SpriteCatalog.SpriteScale);

        context.DrawImage(
            bitmap,
            SpriteImages.ToRect(rect),
            new Rect(centre.X - width / 2d, centre.Y - height / 2d, width, height));
    }

    private static bool InView(TreeBounds visible, NodeVisual visual)
    {
        var radius = (float)visual.FrameRadius;
        return visual.Position.X + radius >= visible.MinX
               && visual.Position.X - radius <= visible.MaxX
               && visual.Position.Y + radius >= visible.MinY
               && visual.Position.Y - radius <= visible.MaxY;
    }

    private static bool Intersects(TreeBounds visible, TreeBounds other) =>
        other.MaxX >= visible.MinX && other.MinX <= visible.MaxX
        && other.MaxY >= visible.MinY && other.MinY <= visible.MaxY;

    private static bool Intersects(TreeBounds visible, Vector2 min, Vector2 max) =>
        Intersects(visible, new TreeBounds(min.X, min.Y, max.X, max.Y));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetCurrentPoint(this);
        _pressOrigin = point.Position;
        _lastPointer = point.Position;
        _isPanning = false;
        _pressModifiers = e.KeyModifiers;

        if (point.Properties.IsRightButtonPressed)
        {
            if (HitTest(point.Position) is { } visual)
            {
                NodeRemoved?.Invoke(this, visual.Id);
                e.Handled = true;
            }

            return;
        }

        // Middle drag always pans; left drag pans only once it clears the click threshold.
        _pressIsPan = point.Properties.IsMiddleButtonPressed;
        _isPanning = _pressIsPan;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetCurrentPoint(this);
        var position = point.Position;

        var buttonHeld = point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed;
        if (buttonHeld && Equals(e.Pointer.Captured, this))
        {
            if (!_isPanning
                && (Math.Abs(position.X - _pressOrigin.X) > DragThreshold
                    || Math.Abs(position.Y - _pressOrigin.Y) > DragThreshold))
            {
                _isPanning = true;
            }

            if (_isPanning)
            {
                _camera.Pan(position - _lastPointer);
                _lastPointer = position;
                InvalidateVisual();
                return;
            }
        }

        _lastPointer = position;
        UpdateHover(position);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var wasPanning = _isPanning;
        _isPanning = false;
        _pressIsPan = false;
        e.Pointer.Capture(null);

        if (wasPanning || e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (HitTest(e.GetPosition(this)) is not { } visual)
            return;

        // Prefer the modifiers from the press: release can lose Alt on Windows after the menu key fires.
        var modifiers = _pressModifiers | e.KeyModifiers;
        _pressModifiers = KeyModifiers.None;

        // Ctrl = Must take. Shift or Alt = Never take (Shift is the reliable one on Windows).
        if (modifiers.HasFlag(KeyModifiers.Control))
            NodeRequired?.Invoke(this, visual.Id);
        else if (modifiers.HasFlag(KeyModifiers.Shift) || modifiers.HasFlag(KeyModifiers.Alt))
            NodeForbidden?.Invoke(this, visual.Id);
        else
            NodeActivated?.Invoke(this, visual.Id);

        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredNodeId is null)
            return;

        _hoveredNodeId = null;
        HoveredNodeChanged?.Invoke(this, null);
        InvalidateVisual();
    }

    /// <summary>
    /// Shortcuts live on the canvas rather than the window so that keys the panels need for typing,
    /// such as minus in a weight box, are never swallowed by the view.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key != Key.F || e.KeyModifiers != KeyModifiers.None)
            return;

        FitToTree();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Math.Abs(e.Delta.Y) < 0.01)
            return;

        _camera.ZoomAt(e.GetPosition(this), Math.Pow(WheelZoomStep, e.Delta.Y));
        UpdateHover(e.GetPosition(this));
        Redraw();
        e.Handled = true;
    }

    private void UpdateHover(Point position)
    {
        var hit = HitTest(position);
        if (hit?.Id == _hoveredNodeId)
            return;

        _hoveredNodeId = hit?.Id;
        HoveredNodeChanged?.Invoke(this, hit?.Node);
        InvalidateVisual();
    }

    private NodeVisual? HitTest(Point position) =>
        Session?.Scene.HitTest(_camera.ToTree(position));
}
