using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasPlanner.Core.Solving;

/// <summary>
/// What the user wants out of the tree: soft goals as category weights, hard goals as node sets.
/// </summary>
public sealed class SolveProfile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("name")] public string Name { get; set; } = "Unnamed plan";

    /// <summary>Points to spend. Null uses the tree's own total (138 as of 3.26).</summary>
    [JsonPropertyName("budget")] public int? Budget { get; set; }

    /// <summary>
    /// Category to weight, e.g. <c>{"Scarabs": 10, "Map Sustain": 4}</c>. Categories come from the
    /// score table; anything not listed is worth nothing. Negative weights push the route away.
    /// </summary>
    [JsonPropertyName("weights")]
    public Dictionary<string, double> Weights { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-node bias added on top of the weighted stats, keyed by node id or exact node name. This is
    /// where keystone drawbacks belong, since no amount of text parsing can price them.
    /// </summary>
    [JsonPropertyName("nodeWeights")]
    public Dictionary<string, double> NodeWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nodes that must end up in the route, by id or exact name.</summary>
    [JsonPropertyName("require")] public List<string> Require { get; set; } = [];

    /// <summary>Nodes that must never be taken, by id or exact name.</summary>
    [JsonPropertyName("forbid")] public List<string> Forbid { get; set; } = [];

    /// <summary>Whole regions that must never be taken, e.g. <c>["Breach"]</c>. A hard ban.</summary>
    [JsonPropertyName("forbidRegions")] public List<string> ForbidRegions { get; set; } = [];

    /// <summary>
    /// Mechanics to opt out of, e.g. <c>["Breach"]</c>. Softer and usually more useful than a ban:
    /// the category is weighted against by <see cref="ExclusionWeight"/> so none of its nodes are
    /// worth taking, and the matching exclusion notable (Dimensional Barrier and friends) is added to
    /// <see cref="Require"/> so the mechanic is actively switched off. Pathing through the region
    /// stays legal, so this can never make the request infeasible.
    /// </summary>
    [JsonPropertyName("excludeMechanics")] public List<string> ExcludeMechanics { get; set; } = [];

    /// <summary>
    /// How hard to push away from a mechanic listed in <see cref="ExcludeMechanics"/>. Applied as a
    /// negative category weight, which makes the mechanic's own nodes cost score to take and, because
    /// "no chance to contain" reads as a downside, makes the notable that removes it worth buying.
    /// </summary>
    /// <remarks>
    /// Deliberately well above a typical category weight. An event you have not specced into is worse
    /// than useless, so the few points spent reaching its off-switch should almost always beat
    /// spending them on more of what you are farming.
    /// </remarks>
    [JsonPropertyName("exclusionWeight")] public double ExclusionWeight { get; set; } = 25d;

    /// <summary>
    /// Whether to take the keystone that grants extra atlas points, currently Unwavering Vision (+20).
    /// It is a big enough trade to be your decision rather than the solver's: it hands back 20 points
    /// but bans scarabs and fragment-modified maps.
    /// </summary>
    [JsonPropertyName("unwaveringVision")]
    [JsonConverter(typeof(JsonStringEnumConverter<PointGrantChoice>))]
    public PointGrantChoice UnwaveringVision { get; set; } = PointGrantChoice.Auto;

    /// <summary>
    /// Nodes already allocated in game. They are treated as free, so the route continues from where
    /// you actually are instead of assuming a fresh tree.
    /// </summary>
    [JsonPropertyName("preAllocated")] public List<int> PreAllocated { get; set; } = [];

    /// <summary>
    /// Value given to a stat line that carries no number, since "Beasts are more likely to be rarer
    /// varieties" is worth something but cannot be scored numerically.
    /// </summary>
    [JsonPropertyName("flatStatValue")] public double FlatStatValue { get; set; } = 10d;

    /// <summary>Fixed seed so the same profile always produces the same route.</summary>
    [JsonPropertyName("seed")] public int Seed { get; set; } = 1;

    /// <summary>Wall-clock budget for the local search phase.</summary>
    [JsonPropertyName("timeLimitMs")] public int TimeLimitMs { get; set; } = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static SolveProfile Load(string path) =>
        JsonSerializer.Deserialize<SolveProfile>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"'{path}' deserialised to null.");

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>A worked example, used by <c>profile --init</c> and as documentation of the format.</summary>
    public static SolveProfile Example() => new()
    {
        Name = "Scarab farming",
        Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Scarabs"] = 10,
            ["Map Sustain"] = 5,
            ["Quantity & Rarity"] = 4,
            ["Pack Size"] = 3,
            ["Extra Content"] = 2,
            ["Monster Difficulty"] = -2,
        },
        Require = ["Significant Troves", "Remarkable Relics"],
        ExcludeMechanics = ["Breach"],
        UnwaveringVision = PointGrantChoice.Exclude,
        NodeWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Overloaded Circuits"] = 250,
        },
    };
}

/// <summary>What to do about the keystone that grants extra atlas points.</summary>
public enum PointGrantChoice
{
    /// <summary>Solve both ways and keep whichever route scores higher.</summary>
    Auto,

    /// <summary>Always take it, and plan against the larger budget.</summary>
    Include,

    /// <summary>Never take it.</summary>
    Exclude,
}
