using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tally;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Planning;

/// <summary>
/// The handoff between the planner and anything that consumes a plan, the ExileAPI plugin in
/// particular: the final node set, the order to allocate it in, and what it adds up to.
/// </summary>
/// <remarks>
/// Treat this as a versioned contract. <see cref="Nodes"/> and <see cref="Url"/> are enough to drive
/// a plain highlight overlay; <see cref="Order"/> is what lets an overlay show "your next 5 points"
/// and drive step-by-step allocation.
/// </remarks>
public sealed class AtlasPlan
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("generatedUtc")] public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Which tree this was planned against, so a plugin can refuse a mismatched one.</summary>
    [JsonPropertyName("treeName")] public string TreeName { get; set; } = string.Empty;

    [JsonPropertyName("budget")] public int Budget { get; set; }
    [JsonPropertyName("pointsSpent")] public int PointsSpent { get; set; }
    [JsonPropertyName("pointsGranted")] public int PointsGranted { get; set; }

    /// <summary>Solver score. Only comparable between plans built from the same profile.</summary>
    [JsonPropertyName("prize")] public double Prize { get; set; }

    /// <summary>Importable into any atlas tree viewer, including the existing PoBTreeOverlay.</summary>
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;

    /// <summary>Every node in the finished plan, including the free start node.</summary>
    [JsonPropertyName("nodes")] public List<int> Nodes { get; set; } = [];

    /// <summary>Nodes that were already allocated when this was planned.</summary>
    [JsonPropertyName("preAllocated")] public List<int> PreAllocated { get; set; } = [];

    /// <summary>Allocation order. Step N is the Nth point to spend.</summary>
    [JsonPropertyName("order")] public List<PlanStep> Order { get; set; } = [];

    [JsonPropertyName("tally")] public PlanTally Tally { get; set; } = new();

    /// <summary>The request this came from, so a plan can be regenerated or explained later.</summary>
    [JsonPropertyName("profile")] public SolveProfile? Profile { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AtlasPlan Load(string path) =>
        JsonSerializer.Deserialize<AtlasPlan>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"'{path}' deserialised to null.");

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static AtlasPlan Build(
        AtlasTree tree,
        ScoreTable scores,
        SolveProfile profile,
        PrizeModel prize,
        RouteSolution solution)
    {
        var tally = AtlasTally.Compute(tree, solution.Nodes, scores);

        return new AtlasPlan
        {
            Name = profile.Name,
            TreeName = tree.TreeName,
            Budget = solution.Budget,
            PointsSpent = solution.PointsSpent,
            PointsGranted = solution.PointsGranted,
            Prize = Math.Round(solution.Prize, 2),
            // The start node is free and always allocated, so it has no place in an importable URL.
            Url = AtlasUrl.Encode(solution.Nodes.Where(id => id != tree.StartNodeId)),
            Nodes = solution.Nodes.Order().ToList(),
            PreAllocated = solution.PreAllocated.Order().ToList(),
            Order = OrderPlanner.Order(tree, solution, prize),
            Tally = PlanTally.From(tally),
            Profile = profile,
        };
    }

    /// <summary>
    /// Builds a plan from an allocation someone assembled by hand, so a tree clicked together in the
    /// planner exports the same shape as a solved one. Order and tally are derived the same way; there
    /// are simply no solver statistics behind it.
    /// </summary>
    public static AtlasPlan FromAllocation(
        AtlasTree tree,
        ScoreTable scores,
        SolveProfile profile,
        PrizeModel prize,
        IEnumerable<int> allocated)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(allocated);

        var nodes = new HashSet<int>(
            allocated.Where(id => tree.TryGet(id, out var node) && node!.IsAllocatable))
        {
            tree.StartNodeId,
        };

        var solution = new RouteSolution
        {
            Nodes = nodes,
            PointsSpent = nodes.Sum(id => tree[id].Cost),
            Budget = profile.Budget ?? tree.TotalPoints,
            PointsGranted = nodes.Sum(id => tree[id].GrantedPoints),
            Prize = Math.Round(nodes.Sum(prize.PrizeOf), 2),
            PreAllocated = new HashSet<int>(),
            Stats = new SolverStats
            {
                SeedPoints = 0,
                ExpansionPasses = 0,
                LocalSearchAttempts = 0,
                LocalSearchImprovements = 0,
                ElapsedMs = 0,
                UsedPointGrantBranch = false,
            },
        };

        return Build(tree, scores, profile, prize, solution);
    }
}

public sealed class PlanStep
{
    /// <summary>1-based; step N is the Nth point spent.</summary>
    [JsonPropertyName("step")] public int Step { get; set; }

    [JsonPropertyName("nodeId")] public int NodeId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("region")] public string Region { get; set; } = string.Empty;

    [JsonPropertyName("prize")] public double Prize { get; set; }

    [JsonPropertyName("grantsPoints")] public int? GrantsPoints { get; set; }

    /// <summary>Set when this node is a step along the way to something rather than the goal itself.</summary>
    [JsonPropertyName("towardsNodeId")] public int? TowardsNodeId { get; set; }

    [JsonPropertyName("towards")] public string? Towards { get; set; }

    public string Describe() =>
        (Name.Length > 0 ? Name : $"#{NodeId}") +
        (Towards is { Length: > 0 } ? $"  (towards {Towards})" : string.Empty);
}

public sealed class PlanTally
{
    [JsonPropertyName("summed")] public List<PlanTallyEntry> Summed { get; set; } = [];
    [JsonPropertyName("repeated")] public List<PlanTallyEntry> Repeated { get; set; } = [];
    [JsonPropertyName("flags")] public List<PlanTallyEntry> Flags { get; set; } = [];

    public static PlanTally From(AtlasTally tally) => new()
    {
        Summed = tally.Summed.Select(PlanTallyEntry.From).ToList(),
        Repeated = tally.Repeated.Select(PlanTallyEntry.From).ToList(),
        Flags = tally.Flags.Select(PlanTallyEntry.From).ToList(),
    };
}

public sealed class PlanTallyEntry
{
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;

    /// <summary>Ready to display, with the total already substituted into the template.</summary>
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;

    [JsonPropertyName("total")] public double Total { get; set; }
    [JsonPropertyName("nodeCount")] public int NodeCount { get; set; }

    public static PlanTallyEntry From(TallyEntry entry) => new()
    {
        Category = entry.Category,
        Text = entry.Rendered,
        Total = Math.Round(entry.Total, 2),
        NodeCount = entry.NodeCount,
    };
}
