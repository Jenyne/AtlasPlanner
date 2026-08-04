namespace AtlasPlanner.Core.Solving;

public sealed class RouteSolution
{
    /// <summary>Every node in the route, including the free start node.</summary>
    public required IReadOnlySet<int> Nodes { get; init; }

    public required int PointsSpent { get; init; }
    public required int Budget { get; init; }
    public required double Prize { get; init; }

    /// <summary>Extra points the route itself hands back, from Unwavering Vision.</summary>
    public required int PointsGranted { get; init; }

    /// <summary>Nodes treated as already allocated and therefore free.</summary>
    public required IReadOnlySet<int> PreAllocated { get; init; }

    public required SolverStats Stats { get; init; }

    public int PointsUnspent => Budget - PointsSpent;
}

public sealed class SolverStats
{
    public required int SeedPoints { get; init; }
    public required int ExpansionPasses { get; init; }
    public required int LocalSearchAttempts { get; init; }
    public required int LocalSearchImprovements { get; init; }
    public required long ElapsedMs { get; init; }

    /// <summary>True when the point-granting keystone branch produced the better route.</summary>
    public required bool UsedPointGrantBranch { get; init; }
}

public sealed class InfeasibleRouteException(string message) : Exception(message);
