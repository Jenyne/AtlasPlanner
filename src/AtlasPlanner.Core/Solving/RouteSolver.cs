using System.Diagnostics;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Solving;

/// <summary>
/// Picks a connected set of nodes that maximises prize within a point budget, while honouring
/// required and forbidden nodes.
/// </summary>
/// <remarks>
/// This is a rooted budgeted prize-collecting Steiner tree, NP-hard in general but tiny here: 901
/// nodes, average degree 2.2, unit costs. The approach is seed, then greedily expand along the most
/// valuable path per point, then destroy-and-repair local search under a time limit. Everything is
/// driven off a fixed seed so the same profile always yields the same route.
/// </remarks>
public sealed class RouteSolver
{
    private readonly AtlasTree _tree;
    private readonly PrizeModel _prize;
    private readonly bool[] _forbidden;
    private readonly int[] _requiredIndices;
    private readonly bool[] _free;
    private readonly int _startIndex;

    private bool[] _chosen;
    private int _pointsSpent;
    private int _expansionPasses;

    private RouteSolver(AtlasTree tree, PrizeModel prize, IReadOnlySet<int> preAllocated)
    {
        _tree = tree;
        _prize = prize;
        _startIndex = tree.IndexOf(tree.StartNodeId);

        _forbidden = new bool[tree.NodeCount];
        foreach (var id in prize.ForbiddenIds)
            _forbidden[tree.IndexOf(id)] = true;

        _requiredIndices = prize.RequiredIds.Select(tree.IndexOf).Order().ToArray();

        _free = new bool[tree.NodeCount];
        _free[_startIndex] = true;
        foreach (var id in preAllocated)
        {
            if (tree.Nodes.ContainsKey(id))
                _free[tree.IndexOf(id)] = true;
        }

        _chosen = new bool[tree.NodeCount];
    }

    public static RouteSolution Solve(AtlasTree tree, PrizeModel prize, SolveProfile profile)
    {
        var stopwatch = Stopwatch.StartNew();
        var preAllocated = profile.PreAllocated.Where(tree.Nodes.ContainsKey).ToHashSet();
        var baseBudget = profile.Budget ?? tree.TotalPoints;

        // Only one node hands atlas points back, so the budget is a single binary choice. Whether it
        // is the user's call or the solver's has already been settled in the prize model.
        var granter = prize.PointGranter;

        if (granter is not null && prize.RequiredIds.Contains(granter.Id))
        {
            var forced = SolveBranch(tree, prize, profile, preAllocated,
                baseBudget + granter.GrantedPoints, extraRequired: null);

            return Finish(forced, preAllocated, stopwatch, usedGrantBranch: true);
        }

        var direct = SolveBranch(tree, prize, profile, preAllocated, baseBudget, extraRequired: null);

        var canConsiderGranter = granter is not null && !prize.ForbiddenIds.Contains(granter.Id);
        if (!canConsiderGranter)
            return Finish(direct, preAllocated, stopwatch, usedGrantBranch: false);

        RouteSolution? granted = null;
        try
        {
            granted = SolveBranch(tree, prize, profile, preAllocated,
                baseBudget + granter!.GrantedPoints, extraRequired: granter.Id);
        }
        catch (InfeasibleRouteException)
        {
            // Reaching it may not fit; the direct route still stands.
        }

        var best = granted is not null && granted.Prize > direct.Prize ? granted : direct;

        return Finish(best, preAllocated, stopwatch, ReferenceEquals(best, granted));
    }

    private static RouteSolution Finish(
        RouteSolution branch,
        IReadOnlySet<int> preAllocated,
        Stopwatch stopwatch,
        bool usedGrantBranch) => new()
    {
        Nodes = branch.Nodes,
        PointsSpent = branch.PointsSpent,
        Budget = branch.Budget,
        Prize = branch.Prize,
        PointsGranted = branch.PointsGranted,
        PreAllocated = preAllocated,
        Stats = new SolverStats
        {
            SeedPoints = branch.Stats.SeedPoints,
            ExpansionPasses = branch.Stats.ExpansionPasses,
            LocalSearchAttempts = branch.Stats.LocalSearchAttempts,
            LocalSearchImprovements = branch.Stats.LocalSearchImprovements,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            UsedPointGrantBranch = usedGrantBranch,
        },
    };

    private static RouteSolution SolveBranch(
        AtlasTree tree,
        PrizeModel prize,
        SolveProfile profile,
        IReadOnlySet<int> preAllocated,
        int budget,
        int? extraRequired)
    {
        var solver = new RouteSolver(tree, prize, preAllocated);

        var required = solver._requiredIndices.ToList();
        if (extraRequired is not null)
        {
            var index = tree.IndexOf(extraRequired.Value);
            if (!required.Contains(index))
                required.Add(index);
        }

        return solver.Run(profile, budget, required);
    }

    private RouteSolution Run(SolveProfile profile, int budget, List<int> required)
    {
        Reset();

        var seedPoints = Seed(required);
        if (_pointsSpent > budget)
            throw new InfeasibleRouteException(
                $"Connecting the required nodes costs {_pointsSpent} points, over the {budget}-point budget.");

        Expand(budget);
        Prune(required);
        Expand(budget);

        var bestChosen = (bool[])_chosen.Clone();
        var bestPoints = _pointsSpent;
        var bestPrize = CurrentPrize();

        var attempts = 0;
        var improvements = 0;
        var barrenAttempts = 0;
        var random = new Random(profile.Seed);
        var stopwatch = Stopwatch.StartNew();
        var requiredSet = required.ToHashSet();

        while (stopwatch.ElapsedMilliseconds < profile.TimeLimitMs)
        {
            attempts++;

            if (!DestroyBranch(random, requiredSet))
            {
                // Either the route is entirely required, or the dice keep landing on branches that
                // cannot be given up. Give it a fair number of tries before concluding it is stuck.
                if (++barrenAttempts > 512)
                    break;
                continue;
            }

            barrenAttempts = 0;

            Expand(budget);
            Prune(required);
            Expand(budget);

            var prize = CurrentPrize();
            if (prize > bestPrize)
            {
                improvements++;
                bestPrize = prize;
                bestPoints = _pointsSpent;
                bestChosen = (bool[])_chosen.Clone();
            }
            else
            {
                _chosen = (bool[])bestChosen.Clone();
                _pointsSpent = bestPoints;
            }
        }

        _chosen = bestChosen;
        _pointsSpent = bestPoints;

        var nodes = ChosenIds();

        return new RouteSolution
        {
            Nodes = nodes,
            PointsSpent = bestPoints,
            Budget = budget,
            Prize = bestPrize,
            PointsGranted = nodes.Sum(id => _tree[id].GrantedPoints),
            PreAllocated = new HashSet<int>(),
            Stats = new SolverStats
            {
                SeedPoints = seedPoints,
                ExpansionPasses = _expansionPasses,
                LocalSearchAttempts = attempts,
                LocalSearchImprovements = improvements,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                UsedPointGrantBranch = false,
            },
        };
    }

    private void Reset()
    {
        Array.Clear(_chosen);
        _pointsSpent = 0;
        _expansionPasses = 0;

        for (var i = 0; i < _free.Length; i++)
        {
            if (_free[i])
                _chosen[i] = true;
        }
    }

    /// <summary>
    /// Connects the required nodes with the shortest-path heuristic: repeatedly attach whichever
    /// unconnected requirement is cheapest to reach from what is already chosen.
    /// </summary>
    private int Seed(List<int> required)
    {
        var before = _pointsSpent;

        while (true)
        {
            var outstanding = required.Where(index => !_chosen[index]).ToList();
            if (outstanding.Count == 0)
                break;

            var search = SearchFromChosen();

            var target = outstanding
                .Where(index => search.Distance[index] > 0)
                .OrderBy(index => search.Distance[index])
                .ThenByDescending(index => search.Value[index])
                .ThenBy(index => index)
                .FirstOrDefault(-1);

            if (target < 0)
            {
                var unreachable = outstanding.Select(index => _tree[_tree.IdAt(index)].ToString());
                throw new InfeasibleRouteException(
                    "Required node(s) cannot be reached without crossing a forbidden node: " +
                    string.Join(", ", unreachable));
            }

            AddPath(search, target);
        }

        return _pointsSpent - before;
    }

    /// <summary>Repeatedly buys the path with the best prize per point until the budget runs out.</summary>
    private void Expand(int budget)
    {
        while (true)
        {
            var remaining = budget - _pointsSpent;
            if (remaining <= 0)
                return;

            _expansionPasses++;
            var search = SearchFromChosen();

            var best = -1;
            var bestRatio = 0d;

            for (var index = 0; index < _tree.NodeCount; index++)
            {
                var cost = search.Distance[index];
                if (cost <= 0 || cost > remaining)
                    continue;

                var value = search.Value[index];
                if (value <= 0d)
                    continue;

                var ratio = value / cost;
                if (ratio > bestRatio || (ratio == bestRatio && best >= 0 && cost < search.Distance[best]))
                {
                    bestRatio = ratio;
                    best = index;
                }
            }

            if (best < 0)
                return;

            AddPath(search, best);
        }
    }

    /// <summary>Drops worthless chosen leaves so their points can be spent somewhere useful.</summary>
    private void Prune(List<int> required)
    {
        var requiredSet = required.ToHashSet();
        var removedAny = true;

        while (removedAny)
        {
            removedAny = false;

            for (var index = 0; index < _tree.NodeCount; index++)
            {
                if (!_chosen[index] || _free[index] || requiredSet.Contains(index))
                    continue;
                if (_prize.PrizeOfIndex(index) > 0d)
                    continue;
                if (ChosenNeighbourCount(index) > 1)
                    continue;

                _chosen[index] = false;
                _pointsSpent--;
                removedAny = true;
            }
        }
    }

    /// <summary>
    /// Removes a random chosen node together with everything that hangs off it, so the repair phase
    /// can spend those points differently. Returns false when nothing could be removed.
    /// </summary>
    private bool DestroyBranch(Random random, IReadOnlySet<int> required)
    {
        var candidates = new List<int>();
        for (var index = 0; index < _tree.NodeCount; index++)
        {
            if (_chosen[index] && !_free[index] && !required.Contains(index))
                candidates.Add(index);
        }

        if (candidates.Count == 0)
            return false;

        var victim = candidates[random.Next(candidates.Count)];
        var dependents = DependentsOf(victim);

        if (dependents.Any(index => _free[index] || required.Contains(index)))
            return false;

        foreach (var index in dependents)
        {
            _chosen[index] = false;
            _pointsSpent--;
        }

        return dependents.Count > 0;
    }

    /// <summary>
    /// Chosen nodes that would be cut off from the start node if <paramref name="victim"/> were
    /// removed, including the victim itself.
    /// </summary>
    private List<int> DependentsOf(int victim)
    {
        var reached = new bool[_tree.NodeCount];
        var queue = new Queue<int>();

        reached[_startIndex] = true;
        queue.Enqueue(_startIndex);

        while (queue.Count > 0)
        {
            foreach (var neighbour in _tree.NeighboursByIndex(queue.Dequeue()))
            {
                if (neighbour == victim || !_chosen[neighbour] || reached[neighbour])
                    continue;

                reached[neighbour] = true;
                queue.Enqueue(neighbour);
            }
        }

        var dependents = new List<int>();
        for (var index = 0; index < _tree.NodeCount; index++)
        {
            if (_chosen[index] && !reached[index])
                dependents.Add(index);
        }

        return dependents;
    }

    /// <summary>
    /// Breadth-first search out of everything currently chosen, then a dynamic program over the
    /// resulting shortest-path layers so each node records the most valuable shortest route to it.
    /// Because every unchosen node costs exactly one point, hop distance is the point cost.
    /// </summary>
    private PathSearch SearchFromChosen()
    {
        var distance = new int[_tree.NodeCount];
        Array.Fill(distance, TreeGraph.Unreachable);

        var frontier = new Queue<int>();
        var maxDistance = 0;

        for (var index = 0; index < _tree.NodeCount; index++)
        {
            if (!_chosen[index])
                continue;

            distance[index] = 0;
            frontier.Enqueue(index);
        }

        var layered = new List<List<int>>();

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            foreach (var neighbour in _tree.NeighboursByIndex(current))
            {
                if (distance[neighbour] != TreeGraph.Unreachable || _forbidden[neighbour])
                    continue;

                distance[neighbour] = distance[current] + 1;
                maxDistance = Math.Max(maxDistance, distance[neighbour]);

                while (layered.Count < distance[neighbour])
                    layered.Add([]);
                layered[distance[neighbour] - 1].Add(neighbour);

                frontier.Enqueue(neighbour);
            }
        }

        var value = new double[_tree.NodeCount];
        var parent = new int[_tree.NodeCount];
        Array.Fill(parent, TreeGraph.Unreachable);

        for (var layer = 0; layer < maxDistance; layer++)
        {
            foreach (var index in layered[layer])
            {
                var bestParent = TreeGraph.Unreachable;
                var bestValue = double.NegativeInfinity;

                foreach (var neighbour in _tree.NeighboursByIndex(index))
                {
                    if (distance[neighbour] != distance[index] - 1)
                        continue;
                    if (value[neighbour] > bestValue)
                    {
                        bestValue = value[neighbour];
                        bestParent = neighbour;
                    }
                }

                parent[index] = bestParent;
                value[index] = (bestParent == TreeGraph.Unreachable ? 0d : bestValue) + _prize.PrizeOfIndex(index);
            }
        }

        return new PathSearch { Distance = distance, Value = value, Parent = parent };
    }

    private void AddPath(PathSearch search, int target)
    {
        for (var index = target; index != TreeGraph.Unreachable && !_chosen[index]; index = search.Parent[index])
        {
            _chosen[index] = true;
            if (!_free[index])
                _pointsSpent++;
        }
    }

    private int ChosenNeighbourCount(int index) =>
        _tree.NeighboursByIndex(index).Count(neighbour => _chosen[neighbour]);

    private double CurrentPrize()
    {
        var total = 0d;
        for (var index = 0; index < _tree.NodeCount; index++)
        {
            if (_chosen[index])
                total += _prize.PrizeOfIndex(index);
        }

        return total;
    }

    private HashSet<int> ChosenIds()
    {
        var ids = new HashSet<int>();
        for (var index = 0; index < _tree.NodeCount; index++)
        {
            if (_chosen[index])
                ids.Add(_tree.IdAt(index));
        }

        return ids;
    }

    private sealed class PathSearch
    {
        public required int[] Distance { get; init; }
        public required double[] Value { get; init; }
        public required int[] Parent { get; init; }
    }
}
