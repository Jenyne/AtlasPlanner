using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public class TreeGraphTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    [Fact]
    public void Shortest_path_starts_and_ends_where_asked_and_only_uses_real_edges()
    {
        var target = Tree.Nodes.Values.First(n => n.Kind == NodeKind.Keystone).Id;

        var path = Tree.ShortestPath(Tree.StartNodeId, target);

        Assert.NotNull(path);
        Assert.Equal(Tree.StartNodeId, path[0]);
        Assert.Equal(target, path[^1]);

        for (var i = 1; i < path.Count; i++)
            Assert.Contains(path[i], Tree[path[i - 1]].Neighbours);
    }

    [Fact]
    public void Shortest_path_length_agrees_with_the_distance_map()
    {
        var distances = Tree.Distances(Tree.StartNodeId);

        foreach (var node in Tree.Nodes.Values.Take(50))
        {
            var path = Tree.ShortestPath(Tree.StartNodeId, node.Id);

            Assert.NotNull(path);
            Assert.Equal(distances[Tree.IndexOf(node.Id)] + 1, path.Count);
        }
    }

    [Fact]
    public void Blocking_a_node_removes_it_from_every_route()
    {
        var start = Tree.StartNodeId;
        var blocked = Tree[start].Neighbours[0];
        var beyond = Tree[blocked].Neighbours.First(id => id != start);

        var path = Tree.ShortestPath(start, beyond, new HashSet<int> { blocked });

        Assert.True(path is null || !path.Contains(blocked));
    }

    [Fact]
    public void A_path_from_the_start_node_is_a_connected_allocation()
    {
        var target = Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable).Id;
        var path = Tree.ShortestPath(Tree.StartNodeId, target)!.ToHashSet();

        Assert.True(Tree.IsConnected(path));
        Assert.Empty(Tree.DisconnectedNodes(path));
    }

    [Fact]
    public void An_allocation_missing_the_start_node_is_not_connected()
    {
        var target = Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable).Id;
        var orphan = new HashSet<int> { target };

        Assert.False(Tree.IsConnected(orphan));
        Assert.Equal([target], Tree.DisconnectedNodes(orphan));
    }

    [Fact]
    public void Distance_to_self_is_zero() =>
        Assert.Equal(0, Tree.Distances(Tree.StartNodeId)[Tree.IndexOf(Tree.StartNodeId)]);
}
