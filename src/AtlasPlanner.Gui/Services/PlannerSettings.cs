using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasPlanner.Core.Solving;

namespace AtlasPlanner.Gui.Services;

/// <summary>
/// Everything the planner remembers between runs: the request as it was last edited, plus the tree
/// that was on screen. Written to the per-user app data folder, so it never touches the repository.
/// </summary>
public sealed class PlannerSettings
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("planName")] public string PlanName { get; set; } = "Unnamed plan";

    /// <summary>Null means "whatever the tree's own total is", which is what a first run wants.</summary>
    [JsonPropertyName("budget")] public int? Budget { get; set; }

    [JsonPropertyName("timeLimitMs")] public int TimeLimitMs { get; set; } = 2000;

    [JsonPropertyName("exclusionWeight")] public double ExclusionWeight { get; set; } = new SolveProfile().ExclusionWeight;

    [JsonPropertyName("unwaveringVision")]
    [JsonConverter(typeof(JsonStringEnumConverter<PointGrantChoice>))]
    public PointGrantChoice UnwaveringVision { get; set; } = PointGrantChoice.Auto;

    [JsonPropertyName("require")] public string Require { get; set; } = string.Empty;

    [JsonPropertyName("forbid")] public string Forbid { get; set; } = string.Empty;

    [JsonPropertyName("keepCurrentAllocation")] public bool KeepCurrentAllocation { get; set; }

    [JsonPropertyName("showBackground")] public bool ShowBackground { get; set; } = true;

    [JsonPropertyName("showStepNumbers")] public bool ShowStepNumbers { get; set; } = true;

    [JsonPropertyName("weights")]
    public Dictionary<string, double> Weights { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Categories switched off, so their events are actively removed from the atlas.</summary>
    [JsonPropertyName("switchedOff")] public List<string> SwitchedOff { get; set; } = [];

    /// <summary>The allocated tree, in the same encoding the game's URLs use.</summary>
    [JsonPropertyName("tree")] public string Tree { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AtlasPlanner",
        "settings.json");

    /// <summary>
    /// Reads the saved settings, or returns defaults. A corrupt or half-written file is never allowed
    /// to stop the app starting; losing the last session is a far smaller problem.
    /// </summary>
    public static PlannerSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<PlannerSettings>(File.ReadAllText(path), JsonOptions) ?? new PlannerSettings()
                : new PlannerSettings();
        }
        catch (Exception)
        {
            return new PlannerSettings();
        }
    }

    /// <summary>Writes the settings, reporting failure rather than throwing on the way out.</summary>
    public bool TrySave(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
