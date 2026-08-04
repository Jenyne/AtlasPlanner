namespace AtlasPlanner.Core.Art;

public sealed record SpriteDownloadProgress(int Completed, int Total, string CurrentFile);

public sealed record SpriteCacheResult(
    IReadOnlyList<SpriteSheet> Downloaded,
    IReadOnlyList<(SpriteSheet Sheet, string Error)> Failed)
{
    public bool AllSucceeded => Failed.Count == 0;
}

/// <summary>
/// Keeps the tree export's sprite sheets on disk so the art is fetched once and then loaded
/// locally. Holds no decoded images, leaving that to whichever UI toolkit is in use.
/// </summary>
public sealed class SpriteCache
{
    private const int MaxConcurrentDownloads = 4;

    private static readonly HttpClient Http = CreateClient();

    public SpriteCache(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory();
    }

    public string Directory { get; }

    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasPlanner",
            "sprites");

    public string PathFor(SpriteSheet sheet) => Path.Combine(Directory, sheet.CacheFileName);

    public bool IsCached(SpriteSheet sheet)
    {
        var path = PathFor(sheet);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    /// <summary>Sheets that still need fetching, deduplicated by cache name.</summary>
    public IReadOnlyList<SpriteSheet> Missing(SpriteCatalog catalog)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return catalog.Sheets
            .Where(sheet => seen.Add(sheet.CacheFileName) && !IsCached(sheet))
            .ToArray();
    }

    /// <summary>
    /// Downloads whatever is missing. Individual failures are reported rather than thrown, so a
    /// partial art set still renders.
    /// </summary>
    public async Task<SpriteCacheResult> EnsureAsync(
        SpriteCatalog catalog,
        IProgress<SpriteDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var missing = Missing(catalog);
        if (missing.Count == 0)
            return new SpriteCacheResult([], []);

        System.IO.Directory.CreateDirectory(Directory);

        var downloaded = new List<SpriteSheet>();
        var failed = new List<(SpriteSheet, string)>();
        var completed = 0;
        var gate = new SemaphoreSlim(MaxConcurrentDownloads);
        var sync = new object();

        var tasks = missing.Select(async sheet =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await DownloadAsync(sheet, cancellationToken).ConfigureAwait(false);
                lock (sync)
                {
                    downloaded.Add(sheet);
                    progress?.Report(new SpriteDownloadProgress(++completed, missing.Count, sheet.CacheFileName));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (sync)
                {
                    failed.Add((sheet, ex.Message));
                    progress?.Report(new SpriteDownloadProgress(++completed, missing.Count, sheet.CacheFileName));
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return new SpriteCacheResult(downloaded, failed);
    }

    private async Task DownloadAsync(SpriteSheet sheet, CancellationToken cancellationToken)
    {
        var destination = PathFor(sheet);
        var temporary = destination + ".part";

        using var response = await Http
            .GetAsync(sheet.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var file = File.Create(temporary))
        {
            await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        // Move into place only once complete, so an interrupted run never leaves a truncated sheet
        // that would be mistaken for a cache hit.
        File.Move(temporary, destination, overwrite: true);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AtlasPlanner/1.0");
        return client;
    }
}
