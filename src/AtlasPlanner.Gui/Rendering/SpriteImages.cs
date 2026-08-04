using AtlasPlanner.Core.Art;
using Avalonia;
using Avalonia.Media.Imaging;

namespace AtlasPlanner.Gui.Rendering;

/// <summary>
/// Decoded sprite sheets, loaded from the on-disk cache the first time each one is asked for.
/// A sheet that is missing or unreadable resolves to null so the renderer can skip it instead of
/// failing.
/// </summary>
public sealed class SpriteImages : IDisposable
{
    private readonly SpriteCache _cache;
    private readonly Dictionary<string, Bitmap?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public SpriteImages(SpriteCache cache) => _cache = cache;

    public Bitmap? Get(SpriteSheet sheet)
    {
        if (_bitmaps.TryGetValue(sheet.CacheFileName, out var cached))
            return cached;

        Bitmap? bitmap = null;
        try
        {
            var path = _cache.PathFor(sheet);
            if (File.Exists(path))
                bitmap = new Bitmap(path);
        }
        catch (Exception)
        {
            // A corrupt sheet should cost us that sprite, not the whole render pass.
            bitmap = null;
        }

        _bitmaps[sheet.CacheFileName] = bitmap;
        return bitmap;
    }

    /// <summary>Drops decoded images, keeping the on-disk cache. Used when art is re-downloaded.</summary>
    public void Clear()
    {
        foreach (var bitmap in _bitmaps.Values)
            bitmap?.Dispose();
        _bitmaps.Clear();
    }

    public void Dispose() => Clear();

    /// <summary>Source rectangle of a sprite, as an Avalonia rect.</summary>
    public static Rect ToRect(SpriteRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
