using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Planning;

/// <summary>
/// Turns a finished route into the order to allocate it in, so the tree is useful while it is being
/// built rather than only once it is finished.
/// </summary>
/// <remarks>
/// Any traversal of the route outward from the start node is legal, since every prefix has to stay
/// connected. This picks a useful one: it repeatedly commits the whole path to whichever remaining
/// node offers the most prize per point, which front-loads notables and defers filler corridors as
/// long as they can be deferred.
/// </remarks>
public static class OrderPlanner
{
    /// <summary>
    /// Weight applied to points a node hands back when ordering. Unwavering Vision is worth reaching
    /// early whatever its own prize, because until it is allocated those 20 points do not exist.
    /// </summary>
    private const double GrantedPointBonus = 1000d;

    public static List<PlanStep> Order(AtlasTree tree, RouteSolution solution, PrizeModel prize)
    {
        var route = solution.Nodes;
        var allocated = new HashSet<int>(solution.PreAllocated.Where(route.Contains)) { tree.StartNodeId };
        var remaining = route.Where(id => !allocated.Contains(id)).ToHashSet();

        var steps = new List<PlanStep>();

        while (remaining.Count > 0)
        {
            var search = Search(tree, route, allocated, prize);
            var target = ChooseTarget(tree, remaining, search);

            if (target is null)
            {
                // A connected route always has a reachable target; never spin if one somehow does not.
                foreach (var id in remaining.Order())
                    steps.Add(Describe(tree, prize, id, steps.Count + 1, id));

                break;
            }

            foreach (var id in PathTo(tree, search, target.Value, allocated))
            {
                steps.Add(Describe(tree, prize, id, steps.Count + 1, target.Value));
                allocated.Add(id);
                remaining.Remove(id);
            }
        }

        return steps;
    }

    private static int? ChooseTarget(AtlasTree tree, IReadOnlySet<int> remaining, LayeredSearch search)
    {
        int? best = null;
        var bestRatio = double.NegativeInfinity;

        // Iterating in id order keeps the result stable when ratios tie.
        foreach (var id in remaining.Order())
        {
            var index = tree.IndexOf(id);
            var cost = search.Distance[index];
            if (cost <= 0)
                continue;

            var ratio = (search.Value[index] + tree[id].GrantedPoints * GrantedPointBonus) / cost;
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                best = id;
            }
        }

        return best;
    }

    /// <summary>The unallocated nodes on the way to <paramref name="targetId"/>, nearest first.</summary>
    private static List<int> PathTo(AtlasTree tree, LayeredSearch search, int targetId, IReadOnlySet<int> allocated)
    {
        var path = new List<int>();

        for (var index = tree.IndexOf(targetId); index != TreeGraph.Unreachable; index = search.Parent[index])
        {
            var id = tree.IdAt(index);
            if (allocated.Contains(id))
                break;

            path.Add(id);
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Breadth-first search from the allocated nodes, confined to the chosen route, plus a dynamic
    /// program over the shortest-path layers so each node records the most valuable route to it.
    /// </summary>
    private static LayeredSearch Search(
        AtlasTree tree,
        IReadOnlySet<int> route,
        IReadOnlySet<int> allocated,
        PrizeModel prize)
    {
        var distance = new int[tree.NodeCount];
        Array.Fill(distance, TreeGraph.Unreachable);

        var parent = new int[tree.NodeCount];
        Array.Fill(parent, TreeGraph.Unreachable);

        var value = new double[tree.NodeCount];
        var layers = new List<List<int>>();
        var queue = new Queue<int>();

        foreach (var id in allocated)
        {
            var index = tree.IndexOf(id);
            distance[index] = 0;
            queue.Enqueue(index);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var neighbour in tree.NeighboursByIndex(current))
            {
                if (distance[neighbour] != TreeGraph.Unreachable || !route.Contains(tree.IdAt(neighbour)))
                    continue;

                distance[neighbour] = distance[current] + 1;

                while (layers.Count < distance[neighbour])
                    layers.Add([]);
                layers[distance[neighbour] - 1].Add(neighbour);

                queue.Enqueue(neighbour);
            }
        }

        foreach (var layer in layers)
        {
            foreach (var index in layer)
            {
                var bestParent = TreeGraph.Unreachable;
                var bestValue = double.NegativeInfinity;

                foreach (var neighbour in tree.NeighboursByIndex(index))
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
                value[index] = (bestParent == TreeGraph.Unreachable ? 0d : bestValue) + prize.PrizeOfIndex(index);
            }
        }

        return new LayeredSearch { Distance = distance, Parent = parent, Value = value };
    }

    private static PlanStep Describe(AtlasTree tree, PrizeModel prize, int id, int step, int targetId)
    {
        var node = tree[id];

        return new PlanStep
        {
            Step = step,
            NodeId = id,
            Name = node.Name,
            Kind = node.Kind.ToString(),
            Region = node.Region,
            Prize = Math.Round(prize.PrizeOf(id), 2),
            GrantsPoints = node.GrantedPoints == 0 ? null : node.GrantedPoints,
            TowardsNodeId = targetId == id ? null : targetId,
            Towards = targetId == id ? null : tree[targetId].Name,
        };
    }

    private sealed class LayeredSearch
    {
        public required int[] Distance { get; init; }
        public required int[] Parent { get; init; }
        public required double[] Value { get; init; }
    }
}
