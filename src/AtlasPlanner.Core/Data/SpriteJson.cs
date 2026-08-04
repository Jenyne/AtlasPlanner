using System.Text.Json.Serialization;

namespace AtlasPlanner.Core.Data;

/// <summary>
/// One sprite sheet as described by the tree export: a remote image plus the source rectangle of
/// every sprite packed into it.
/// </summary>
public sealed class SpriteSheetJson
{
    /// <summary>Absolute URL, including the cache-busting query the export ships with.</summary>
    [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("w")] public int Width { get; set; }
    [JsonPropertyName("h")] public int Height { get; set; }

    /// <summary>
    /// Sprite key to source rectangle. Keys are art paths for node icons
    /// (<c>Art/2DArt/SkillIcons/passives/AtlasTrees/AbyssNode1.png</c>) and short names for
    /// everything else (<c>PSSkillFrameActive</c>).
    /// </summary>
    [JsonPropertyName("coords")] public Dictionary<string, SpriteRectJson> Coords { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SpriteRectJson
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("w")] public int Width { get; set; }
    [JsonPropertyName("h")] public int Height { get; set; }
}
