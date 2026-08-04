namespace AtlasPlanner.Core.Tree;

/// <summary>
/// Unweighted graph queries over the tree. Every node costs one point, so hop count is point cost
/// and breadth-first search is all that's needed.
/// </summary>
public static class TreeGraph
{
    public const int Unreachable = -1;

    /// <summary>
    /// Hop distances from <paramref name="sourceId"/> to every node, indexed by dense node index.
    /// <paramref name="blocked"/> nodes are treated as if they were not in the graph.
    /// </summary>
    public static int[] Distances(this AtlasTree tree, int sourceId, IReadOnlySet<int>? blocked = null)
    {
        var distance = new int[tree.NodeCount];
        Array.Fill(distance, Unreachable);

        var source = tree.IndexOf(sourceId);
        if (blocked is not null && blocked.Contains(sourceId))
            return distance;

        distance[source] = 0;

        var queue = new Queue<int>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in tree.NeighboursByIndex(current))
            {
                if (distance[next] != Unreachable)
                    continue;
                if (blocked is not null && blocked.Contains(tree.IdAt(next)))
                    continue;

                distance[next] = distance[current] + 1;
                queue.Enqueue(next);
            }
        }

        return distance;
    }

    /// <summary>
    /// Shortest node path from <paramref name="fromId"/> to <paramref name="toId"/> inclusive of both
    /// ends, or null when unreachable. Ties are broken deterministically by node id.
    /// </summary>
    public static List<int>? ShortestPath(this AtlasTree tree, int fromId, int toId, IReadOnlySet<int>? blocked = null)
    {
        if (fromId == toId)
            return [fromId];

        var previous = new int[tree.NodeCount];
        Array.Fill(previous, Unreachable);

        var source = tree.IndexOf(fromId);
        var target = tree.IndexOf(toId);

        var visited = new bool[tree.NodeCount];
        visited[source] = true;

        var queue = new Queue<int>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target)
                break;

            foreach (var next in tree.NeighboursByIndex(current))
            {
                if (visited[next])
                    continue;
                if (blocked is not null && blocked.Contains(tree.IdAt(next)))
                    continue;

                visited[next] = true;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!visited[target])
            return null;

        var path = new List<int>();
        for (var at = target; at != Unreachable; at = previous[at])
        {
            path.Add(tree.IdAt(at));
            if (at == source)
                break;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// True when every node in <paramref name="allocated"/> is reachable from the start node using
    /// only nodes that are themselves allocated.
    /// </summary>
    public static bool IsConnected(this AtlasTree tree, IReadOnlySet<int> allocated)
    {
        if (allocated.Count == 0)
            return true;
        if (!allocated.Contains(tree.StartNodeId))
            return false;

        var seen = new HashSet<int> { tree.StartNodeId };
        var queue = new Queue<int>();
        queue.Enqueue(tree.StartNodeId);

        while (queue.Count > 0)
        {
            foreach (var neighbour in tree[queue.Dequeue()].Neighbours)
            {
                if (allocated.Contains(neighbour) && seen.Add(neighbour))
                    queue.Enqueue(neighbour);
            }
        }

        return seen.Count == allocated.Count;
    }

    /// <summary>Nodes in <paramref name="allocated"/> that are cut off from the start node.</summary>
    public static List<int> DisconnectedNodes(this AtlasTree tree, IReadOnlySet<int> allocated)
    {
        if (allocated.Count == 0)
            return [];

        var seen = new HashSet<int>();
        if (allocated.Contains(tree.StartNodeId))
        {
            seen.Add(tree.StartNodeId);
            var queue = new Queue<int>();
            queue.Enqueue(tree.StartNodeId);

            while (queue.Count > 0)
            {
                foreach (var neighbour in tree[queue.Dequeue()].Neighbours)
                {
                    if (allocated.Contains(neighbour) && seen.Add(neighbour))
                        queue.Enqueue(neighbour);
                }
            }
        }

        return allocated.Where(id => !seen.Contains(id)).Order().ToList();
    }
}
