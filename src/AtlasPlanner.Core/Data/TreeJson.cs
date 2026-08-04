using System.Text.Json.Serialization;

namespace AtlasPlanner.Core.Data;

/// <summary>
/// Mirrors the shape of the official tree export (<c>AtlasTreeData.json</c> / <c>SkillTreeData.json</c>).
/// Only the fields the planner needs are modelled.
/// </summary>
public sealed class TreeJson
{
    [JsonPropertyName("tree")] public string? Tree { get; set; }
    [JsonPropertyName("groups")] public Dictionary<string, GroupJson> Groups { get; set; } = new();
    [JsonPropertyName("nodes")] public Dictionary<string, NodeJson> Nodes { get; set; } = new();
    [JsonPropertyName("constants")] public ConstantsJson Constants { get; set; } = new();
    [JsonPropertyName("points")] public PointsJson? Points { get; set; }

    /// <summary>Sprite category ("notableActive", "frame", ...) to zoom level to sheet.</summary>
    [JsonPropertyName("sprites")]
    public Dictionary<string, Dictionary<string, SpriteSheetJson>> Sprites { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Zoom levels the export ships art for, ascending.</summary>
    [JsonPropertyName("imageZoomLevels")] public List<double> ImageZoomLevels { get; set; } = new();

    [JsonPropertyName("min_x")] public float MinX { get; set; }
    [JsonPropertyName("min_y")] public float MinY { get; set; }
    [JsonPropertyName("max_x")] public float MaxX { get; set; }
    [JsonPropertyName("max_y")] public float MaxY { get; set; }
}

public sealed class GroupJson
{
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("orbits")] public List<int> Orbits { get; set; } = new();
    [JsonPropertyName("nodes")] public List<string> Nodes { get; set; } = new();
}

public sealed class NodeJson
{
    [JsonPropertyName("skill")] public int? Skill { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }

    [JsonPropertyName("isNotable")] public bool IsNotable { get; set; }
    [JsonPropertyName("isKeystone")] public bool IsKeystone { get; set; }
    [JsonPropertyName("isMastery")] public bool IsMastery { get; set; }
    [JsonPropertyName("isWormhole")] public bool IsWormhole { get; set; }

    [JsonPropertyName("grantedPassivePoints")] public int GrantedPassivePoints { get; set; }

    [JsonPropertyName("stats")] public List<string> Stats { get; set; } = new();
    [JsonPropertyName("reminderText")] public List<string> ReminderText { get; set; } = new();
    [JsonPropertyName("flavourText")] public List<string> FlavourText { get; set; } = new();

    [JsonPropertyName("group")] public int Group { get; set; }
    [JsonPropertyName("orbit")] public int Orbit { get; set; }
    [JsonPropertyName("orbitIndex")] public int OrbitIndex { get; set; }

    [JsonPropertyName("out")] public List<string> Out { get; set; } = new();
    [JsonPropertyName("in")] public List<string> In { get; set; } = new();
}

public sealed class ConstantsJson
{
    [JsonPropertyName("skillsPerOrbit")] public List<int> SkillsPerOrbit { get; set; } = new();
    [JsonPropertyName("orbitRadii")] public List<int> OrbitRadii { get; set; } = new();
    [JsonPropertyName("PSSCentreInnerRadius")] public int CentreInnerRadius { get; set; }
}

public sealed class PointsJson
{
    [JsonPropertyName("totalPoints")] public int TotalPoints { get; set; }
    [JsonPropertyName("ascendancyPoints")] public int AscendancyPoints { get; set; }
}
