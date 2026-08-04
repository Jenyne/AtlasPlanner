using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtlasPlanner.Core.Data;

namespace AtlasPlanner.Core.Art;

/// <summary>Source rectangle within a sprite sheet, in sheet pixels.</summary>
public readonly record struct SpriteRect(int X, int Y, int Width, int Height);

/// <summary>A width and height in tree units, kept free of any UI toolkit's geometry types.</summary>
public readonly record struct Size2(double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>One sheet of packed sprites, resolved from the tree export.</summary>
public sealed class SpriteSheet
{
    public required string Category { get; init; }

    /// <summary>
    /// The zoom level this sheet was authored for. Art is drawn 1:1 when the tree is rendered at
    /// <see cref="SpriteCatalog.SpriteScale"/> of this value.
    /// </summary>
    public required double Zoom { get; init; }

    public required string Url { get; init; }

    /// <summary>Stable, filesystem-safe name for the cached copy, versioned by the export's query string.</summary>
    public required string CacheFileName { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }

    public required IReadOnlyDictionary<string, SpriteRect> Coords { get; init; }

    public bool TryGet(string key, out SpriteRect rect) => Coords.TryGetValue(key, out rect);

    public override string ToString() => $"{Category}@{Zoom} ({CacheFileName})";
}

/// <summary>
/// The tree export's art manifest: every sprite sheet, at every zoom level, with the source
/// rectangle of each sprite. Holds no image data, so it is safe to use from any host.
/// </summary>
public sealed class SpriteCatalog
{
    /// <summary>Reference category used when a caller asks for zoom levels without naming one.</summary>
    private const string ReferenceCategory = "notableActive";

    private readonly Dictionary<string, SortedList<double, SpriteSheet>> _byCategory;

    private SpriteCatalog(
        Dictionary<string, SortedList<double, SpriteSheet>> byCategory,
        IReadOnlyList<SpriteSheet> sheets,
        IReadOnlyList<double> zoomLevels)
    {
        _byCategory = byCategory;
        Sheets = sheets;
        ZoomLevels = zoomLevels;
    }

    /// <summary>
    /// Art is authored 1:1 with tree units scaled by zoom: a sprite <c>w</c> pixels wide at zoom
    /// <c>z</c> spans <c>w / z</c> tree units.
    /// </summary>
    /// <remarks>
    /// Verified against orbit radii. Each <c>OrbitN</c> sprite is one quadrant of the ring, so its
    /// width matches <c>orbitRadii[n] * zoom</c> plus roughly eight pixels of glow padding, which is
    /// why the ring art looks half-sized next to the node art.
    /// </remarks>
    public const double SpriteScale = 1.0;

    /// <summary>
    /// Fraction of a frame sprite that is the frame itself rather than surrounding glow. Used to
    /// turn sprite dimensions into sensible click targets.
    /// </summary>
    public const double FrameInkFraction = 0.82;

    /// <summary>Every distinct sheet, for prefetching.</summary>
    public IReadOnlyList<SpriteSheet> Sheets { get; }

    /// <summary>Zoom levels available for ordinary node art, ascending.</summary>
    public IReadOnlyList<double> ZoomLevels { get; }

    public static SpriteCatalog Empty { get; } = new([], [], []);

    internal static SpriteCatalog From(TreeJson raw)
    {
        var byCategory = new Dictionary<string, SortedList<double, SpriteSheet>>(StringComparer.Ordinal);
        var sheets = new List<SpriteSheet>();
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (category, zooms) in raw.Sprites)
        {
            var levels = new SortedList<double, SpriteSheet>();

            foreach (var (zoomKey, sheetJson) in zooms)
            {
                if (!double.TryParse(zoomKey, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom))
                    continue;
                if (string.IsNullOrWhiteSpace(sheetJson.Filename))
                    continue;

                var coords = new Dictionary<string, SpriteRect>(sheetJson.Coords.Count, StringComparer.Ordinal);
                foreach (var (key, rect) in sheetJson.Coords)
                    coords[key] = new SpriteRect(rect.X, rect.Y, rect.Width, rect.Height);

                var sheet = new SpriteSheet
                {
                    Category = category,
                    Zoom = zoom,
                    Url = sheetJson.Filename,
                    CacheFileName = CacheNameFor(sheetJson.Filename),
                    Width = sheetJson.Width,
                    Height = sheetJson.Height,
                    Coords = coords,
                };

                levels[zoom] = sheet;
                if (seenUrls.Add(sheet.Url))
                    sheets.Add(sheet);
            }

            if (levels.Count > 0)
                byCategory[category] = levels;
        }

        var zoomLevels = byCategory.TryGetValue(ReferenceCategory, out var reference)
            ? reference.Keys.ToArray()
            : raw.ImageZoomLevels.Order().ToArray();

        return new SpriteCatalog(byCategory, sheets, zoomLevels);
    }

    public bool HasArt => Sheets.Count > 0;

    /// <summary>
    /// The zoom level whose art is closest to <paramref name="renderScale"/> without being smaller,
    /// so sprites are downscaled rather than blown up. Falls back to the largest available.
    /// </summary>
    public double ZoomFor(double renderScale)
    {
        if (ZoomLevels.Count == 0)
            return 1d;

        foreach (var zoom in ZoomLevels)
        {
            if (zoom >= renderScale / SpriteScale)
                return zoom;
        }

        return ZoomLevels[^1];
    }

    /// <summary>
    /// Size of a sprite in tree units, which is zoom-independent and therefore safe to precompute.
    /// Returns zero when the sprite is not in the manifest.
    /// </summary>
    public Size2 TreeSizeOf(string category, string key)
    {
        var zoom = ZoomLevels.Count > 0 ? ZoomLevels[^1] : 1d;
        return TryGetSprite(category, zoom, key, out var sheet, out var rect)
            ? new Size2(rect.Width / (sheet.Zoom * SpriteScale), rect.Height / (sheet.Zoom * SpriteScale))
            : default;
    }

    public SpriteSheet? Sheet(string category, double zoom)
    {
        if (!_byCategory.TryGetValue(category, out var levels))
            return null;
        if (levels.TryGetValue(zoom, out var exact))
            return exact;

        // Categories such as masteryOverlay ship a single level; snap to the nearest one.
        SpriteSheet? best = null;
        var bestDelta = double.MaxValue;
        foreach (var (level, sheet) in levels)
        {
            var delta = Math.Abs(level - zoom);
            if (delta >= bestDelta)
                continue;

            bestDelta = delta;
            best = sheet;
        }

        return best;
    }

    public bool TryGetSprite(string category, double zoom, string key, out SpriteSheet sheet, out SpriteRect rect)
    {
        var found = Sheet(category, zoom);
        if (found is not null && found.TryGet(key, out rect))
        {
            sheet = found;
            return true;
        }

        sheet = null!;
        rect = default;
        return false;
    }

    private static string CacheNameFor(string url)
    {
        var withoutQuery = url.Split('?', 2);
        var name = withoutQuery[0][(withoutQuery[0].LastIndexOf('/') + 1)..];
        if (name.Length == 0)
            name = "sheet.bin";

        if (withoutQuery.Length == 1 || withoutQuery[1].Length == 0)
            return name;

        // Fold the export's cache-busting token into the name so a new league's art re-downloads.
        var token = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(withoutQuery[1])))[..8].ToLowerInvariant();
        var extension = Path.GetExtension(name);
        return $"{Path.GetFileNameWithoutExtension(name)}.{token}{extension}";
    }
}
