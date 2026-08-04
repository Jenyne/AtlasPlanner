using System.Text.RegularExpressions;

namespace AtlasPlanner.Core.Io;

/// <summary>
/// Reads and writes <c>pathofexile.com/fullscreen-atlas-skill-tree/...</c> links.
/// Kept byte-compatible with PoBTreeOverlay's decoder so plans round-trip through the existing overlay.
/// </summary>
public static class AtlasUrl
{
    private const string AtlasPrefix = "https://www.pathofexile.com/fullscreen-atlas-skill-tree/";

    private static readonly Regex UrlRegex = new(
        @"^(http(|s):\/\/|)(\w*\.|)pathofexile\.com\/(fullscreen-|)(?<type>atlas|passive)-skill-tree\/(\d+(\.\d+)+\/)?(?<build>[\w\-=]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAtlasUrl(string url) =>
        UrlRegex.Match(url) is { Success: true } match && match.Groups["type"].Value.Equals("atlas", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts node ids from a tree URL, or from a bare build code. Throws when the input is neither.
    /// </summary>
    public static HashSet<int> Decode(string urlOrCode)
    {
        var match = UrlRegex.Match(urlOrCode);
        var code = match.Success ? match.Groups["build"].Value : urlOrCode.Trim();
        return DecodeCode(code);
    }

    public static HashSet<int> DecodeCode(string buildCode)
    {
        var normalised = buildCode.Replace('-', '+').Replace('_', '/');
        normalised = normalised.PadRight(normalised.Length + (4 - normalised.Length % 4) % 4, '=');

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(normalised);
        }
        catch (FormatException e)
        {
            throw new FormatException($"'{buildCode}' is not a valid tree code.", e);
        }

        if (bytes.Length < 4)
            throw new FormatException("Tree code is too short to contain a version header.");

        var version = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        var offset = version > 3 ? 7 : 6;

        var ids = new HashSet<int>();
        for (var i = offset; i + 1 < bytes.Length; i += 2)
        {
            var id = (bytes[i] << 8) | bytes[i + 1];
            if (id != 0)
                ids.Add(id);
        }

        return ids;
    }

    public static string Encode(IEnumerable<int> nodeIds)
    {
        var ids = nodeIds.Distinct().Order().ToArray();

        var bytes = new List<byte> { 0, 0, 0, 6, 0, 0, (byte)Math.Min(ids.Length, byte.MaxValue) };
        foreach (var id in ids)
        {
            bytes.Add((byte)(id >> 8));
            bytes.Add((byte)id);
        }

        bytes.AddRange([0, 0]);

        var code = Convert.ToBase64String(bytes.ToArray()).Replace('+', '-').Replace('/', '_');
        return AtlasPrefix + code;
    }
}
