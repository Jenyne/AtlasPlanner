using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tally;
using AtlasPlanner.Core.Tree;
using AtlasPlanner.Gui.Rendering;

namespace AtlasPlanner.Gui.Services;

public sealed record SolveOutcome(AtlasPlan Plan, RouteSolution Solution, PrizeModel Prize);

/// <summary>
/// The planner's live state: which nodes are allocated, what that adds up to, and the entry point
/// for solving. The canvas and the panels both read from here, so they can never disagree.
/// </summary>
public sealed class PlannerSession
{
    private readonly HashSet<int> _allocated = [];
    private readonly HashSet<int> _reachable = [];

    public PlannerSession(AtlasTree tree, ScoreTable scores, string treePath, string scoresPath)
    {
        Tree = tree;
        Scores = scores;
        TreePath = treePath;
        ScoresPath = scoresPath;
        Scene = TreeScene.Build(tree);
        Budget = tree.TotalPoints;

        _allocated.Add(tree.StartNodeId);
        RecomputeReachable();
    }

    public AtlasTree Tree { get; }
    public ScoreTable Scores { get; }
    public TreeScene Scene { get; }
    public string TreePath { get; }
    public string ScoresPath { get; }

    /// <summary>Raised whenever the allocated set changes, so views can refresh.</summary>
    public event Action? AllocationChanged;

    public IReadOnlySet<int> Allocated => _allocated;

    /// <summary>Unallocated nodes one step from the allocated set, which the art frames differently.</summary>
    public IReadOnlySet<int> Reachable => _reachable;

    /// <summary>Base points to spend, before anything the tree hands back.</summary>
    public int Budget { get; set; }

    public int PointsGranted => _allocated.Sum(id => Tree[id].GrantedPoints);

    /// <summary>The start node is free, so it never counts against the budget.</summary>
    public int PointsSpent => _allocated.Count(id => id != Tree.StartNodeId);

    public int PointsRemaining => Budget + PointsGranted - PointsSpent;

    public bool IsAllocated(int nodeId) => _allocated.Contains(nodeId);

    /// <summary>
    /// Allocates the cheapest chain of nodes that connects <paramref name="nodeId"/> to what is
    /// already taken, the way clicking a distant node in a tree planner is expected to behave.
    /// Returns the nodes that were added.
    /// </summary>
    public IReadOnlyList<int> AllocateTo(int nodeId)
    {
        if (!Tree.Nodes.ContainsKey(nodeId) || _allocated.Contains(nodeId))
            return [];

        var path = PathToAllocated(nodeId);
        if (path.Count == 0)
            return [];

        foreach (var id in path)
            _allocated.Add(id);

        RecomputeReachable();
        AllocationChanged?.Invoke();
        return path;
    }

    /// <summary>
    /// Removes a node along with anything that only stayed connected through it, so the allocation
    /// is always a single tree rooted at the start node.
    /// </summary>
    public IReadOnlyList<int> Deallocate(int nodeId)
    {
        if (nodeId == Tree.StartNodeId || !_allocated.Remove(nodeId))
            return [];

        var orphans = Tree.DisconnectedNodes(_allocated);
        foreach (var orphan in orphans)
            _allocated.Remove(orphan);

        RecomputeReachable();
        AllocationChanged?.Invoke();
        return [nodeId, .. orphans];
    }

    public void Clear()
    {
        _allocated.Clear();
        _allocated.Add(Tree.StartNodeId);
        RecomputeReachable();
        AllocationChanged?.Invoke();
    }

    public void SetAllocation(IEnumerable<int> nodeIds)
    {
        _allocated.Clear();
        _allocated.Add(Tree.StartNodeId);
        foreach (var id in nodeIds)
        {
            if (Tree.Nodes.ContainsKey(id))
                _allocated.Add(id);
        }

        RecomputeReachable();
        AllocationChanged?.Invoke();
    }

    /// <summary>Importable URL for the current allocation, with the free start node left out.</summary>
    public string ToUrl() => AtlasUrl.Encode(_allocated.Where(id => id != Tree.StartNodeId));

    /// <summary>
    /// Replaces the allocation from a tree URL or raw build code. Nodes that are not in this tree
    /// are reported rather than silently dropped.
    /// </summary>
    public IReadOnlyList<int> LoadUrl(string urlOrCode)
    {
        var ids = AtlasUrl.Decode(urlOrCode);
        var unknown = ids.Where(id => !Tree.Nodes.ContainsKey(id)).Order().ToArray();
        SetAllocation(ids);
        return unknown;
    }

    public AtlasTally Tally() => AtlasTally.Compute(Tree, _allocated, Scores);

    /// <summary>
    /// Runs the solver off the UI thread. The allocated set is untouched; the caller decides whether
    /// to apply the result.
    /// </summary>
    public Task<SolveOutcome> SolveAsync(SolveProfile profile, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                var prize = PrizeModel.Build(Tree, Scores, profile);
                var solution = RouteSolver.Solve(Tree, prize, profile);
                var plan = AtlasPlan.Build(Tree, Scores, profile, prize, solution);
                return new SolveOutcome(plan, solution, prize);
            },
            cancellationToken);

    /// <summary>
    /// Breadth-first walk out from <paramref name="target"/> until it meets the allocated set, then
    /// the chain back. Empty when the node cannot be reached at all.
    /// </summary>
    private List<int> PathToAllocated(int target)
    {
        var startIndex = Tree.IndexOf(target);
        var cameFrom = new int[Tree.NodeCount];
        Array.Fill(cameFrom, -1);

        var visited = new bool[Tree.NodeCount];
        visited[startIndex] = true;

        var queue = new Queue<int>();
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            if (_allocated.Contains(Tree.IdAt(index)))
                return Unwind(index, cameFrom);

            foreach (var neighbour in Tree.NeighboursByIndex(index))
            {
                if (visited[neighbour])
                    continue;

                visited[neighbour] = true;
                cameFrom[neighbour] = index;
                queue.Enqueue(neighbour);
            }
        }

        return [];
    }

    /// <summary>
    /// Walks the search tree back from the node that met the allocation, yielding the new nodes in
    /// the order they would be taken.
    /// </summary>
    private List<int> Unwind(int meetingIndex, int[] cameFrom)
    {
        var path = new List<int>();
        for (var index = meetingIndex; index >= 0; index = cameFrom[index])
        {
            var id = Tree.IdAt(index);
            if (!_allocated.Contains(id))
                path.Add(id);
        }

        // The walk started at the target, so it comes out furthest-first.
        path.Reverse();
        return path;
    }

    private void RecomputeReachable()
    {
        _reachable.Clear();
        foreach (var id in _allocated)
        {
            foreach (var neighbour in Tree[id].Neighbours)
            {
                if (!_allocated.Contains(neighbour))
                    _reachable.Add(neighbour);
            }
        }
    }
}
