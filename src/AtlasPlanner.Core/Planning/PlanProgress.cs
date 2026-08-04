namespace AtlasPlanner.Core.Planning;

/// <summary>
/// Where a live atlas tree sits against a plan: what has been placed, what to place next, and what was
/// spent outside the plan.
/// </summary>
/// <remarks>
/// The overlay plugin drives both its highlights and its clicking from this, so the decisions live here
/// in Core where they can be tested without a game attached. Nothing in here knows about ExileAPI.
/// </remarks>
public sealed class PlanProgress
{
    private readonly Dictionary<int, PlanStep> _stepsByNode;
    private readonly HashSet<int> _done;

    private PlanProgress(
        AtlasPlan plan,
        Dictionary<int, PlanStep> stepsByNode,
        HashSet<int> done,
        List<PlanStep> doneSteps,
        List<PlanStep> remaining,
        List<int> offPlan,
        List<int> missingGroundwork)
    {
        Plan = plan;
        _stepsByNode = stepsByNode;
        _done = done;
        Done = doneSteps;
        Remaining = remaining;
        OffPlan = offPlan;
        MissingGroundwork = missingGroundwork;
    }

    public AtlasPlan Plan { get; }

    /// <summary>Plan steps already allocated in game, in plan order.</summary>
    public IReadOnlyList<PlanStep> Done { get; }

    /// <summary>Plan steps still to allocate, in plan order. The head is what to take next.</summary>
    public IReadOnlyList<PlanStep> Remaining { get; }

    /// <summary>Allocated nodes the plan never asked for, lowest id first.</summary>
    public IReadOnlyList<int> OffPlan { get; }

    /// <summary>
    /// Nodes the plan assumed were already allocated but which are not. The plan's order only stays
    /// connected on top of these, so an ordered apply can stall until they are taken.
    /// </summary>
    public IReadOnlyList<int> MissingGroundwork { get; }

    public int TotalSteps => Plan.Order.Count;
    public int PointsPlaced => Done.Count;
    public int PointsRemaining => Remaining.Count;
    public bool IsComplete => Remaining.Count == 0;

    /// <summary>True when the live tree has nodes the plan does not, or lacks groundwork it assumed.</summary>
    public bool HasDrifted => OffPlan.Count > 0 || MissingGroundwork.Count > 0;

    /// <summary>
    /// Compares a plan against the nodes currently allocated in game. Callers hand over whatever
    /// collection they have; ids the tree does not know about are simply drift.
    /// </summary>
    public static PlanProgress For(AtlasPlan plan, IEnumerable<int> allocatedNodes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(allocatedNodes);

        var allocated = allocatedNodes as IReadOnlySet<int> ?? new HashSet<int>(allocatedNodes);

        var stepsByNode = new Dictionary<int, PlanStep>(plan.Order.Count);
        foreach (var step in plan.Order)
            stepsByNode.TryAdd(step.NodeId, step);

        var ordered = plan.Order.OrderBy(step => step.Step).ToList();
        var doneSteps = new List<PlanStep>();
        var remaining = new List<PlanStep>();
        var done = new HashSet<int>();

        foreach (var step in ordered)
        {
            if (allocated.Contains(step.NodeId))
            {
                doneSteps.Add(step);
                done.Add(step.NodeId);
            }
            else
            {
                remaining.Add(step);
            }
        }

        // The plan's node set covers the start node and any groundwork, none of which are steps.
        var planNodes = new HashSet<int>(plan.Nodes);
        planNodes.UnionWith(stepsByNode.Keys);

        var offPlan = allocated.Where(id => !planNodes.Contains(id)).Order().ToList();
        var missingGroundwork = plan.PreAllocated.Where(id => !allocated.Contains(id)).Order().ToList();

        return new PlanProgress(plan, stepsByNode, done, doneSteps, remaining, offPlan, missingGroundwork);
    }

    /// <summary>
    /// The next <paramref name="count"/> steps to allocate, in order. Shorter than asked for when the
    /// plan is nearly finished, and empty once it is.
    /// </summary>
    public IReadOnlyList<PlanStep> Next(int count) =>
        count <= 0 ? [] : Remaining.Take(count).ToList();

    /// <summary>The single next step, or null when the plan is finished.</summary>
    public PlanStep? NextStep => Remaining.Count > 0 ? Remaining[0] : null;

    public bool IsOnPlan(int nodeId) => _stepsByNode.ContainsKey(nodeId);

    public bool IsDone(int nodeId) => _done.Contains(nodeId);

    /// <summary>1-based step number for a node, or null when the node is not part of the plan.</summary>
    public int? StepNumberOf(int nodeId) =>
        _stepsByNode.TryGetValue(nodeId, out var step) ? step.Step : null;

    public PlanStep? StepOf(int nodeId) =>
        _stepsByNode.TryGetValue(nodeId, out var step) ? step : null;

    /// <summary>How far into the plan a node sits, 0 to 1, for shading a route from start to finish.</summary>
    public double FractionOf(int nodeId)
    {
        if (TotalSteps == 0)
            return 0d;

        return StepNumberOf(nodeId) is { } step ? (double)step / TotalSteps : 0d;
    }

    public string Summary =>
        TotalSteps == 0
            ? "Plan has no steps."
            : IsComplete
                ? $"Plan complete: all {TotalSteps} points placed."
                : $"{PointsPlaced} of {TotalSteps} points placed, {PointsRemaining} to go.";
}
