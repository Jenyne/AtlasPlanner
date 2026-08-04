using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public class AtlasTreeTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    [Fact]
    public void Masteries_are_kept_out_of_the_allocatable_graph()
    {
        Assert.DoesNotContain(Tree.Nodes.Values, node => node.Kind == NodeKind.Mastery);
        Assert.NotEmpty(Tree.Masteries);
    }

    [Fact]
    public void The_structural_root_sentinel_is_dropped_in_favour_of_a_free_start_node()
    {
        Assert.Equal(0, Tree.Start.Cost);
        Assert.NotEmpty(Tree.Start.Neighbours);
        Assert.All(Tree.Nodes.Values.Where(n => n.Id != Tree.StartNodeId), node => Assert.Equal(1, node.Cost));
    }

    [Fact]
    public void Every_allocatable_node_is_reachable_from_the_start_node()
    {
        var distances = Tree.Distances(Tree.StartNodeId);

        Assert.DoesNotContain(TreeGraph.Unreachable, distances);
    }

    [Fact]
    public void Adjacency_is_symmetric()
    {
        foreach (var node in Tree.Nodes.Values)
        foreach (var neighbour in node.Neighbours)
            Assert.Contains(node.Id, Tree[neighbour].Neighbours);
    }

    [Fact]
    public void No_node_lists_itself_as_a_neighbour() =>
        Assert.All(Tree.Nodes.Values, node => Assert.DoesNotContain(node.Id, node.Neighbours));

    [Fact]
    public void Gateways_come_in_connected_pairs()
    {
        var gateways = Tree.Nodes.Values.Where(n => n.IsGateway).ToList();

        Assert.NotEmpty(gateways);
        Assert.All(gateways, gateway =>
            Assert.Contains(gateway.Neighbours, id => Tree[id].IsGateway));
    }

    [Fact]
    public void Nodes_inherit_their_group_mastery_as_a_region()
    {
        var bestiary = Tree.Nodes.Values.Where(n => n.Region == "Bestiary").ToList();

        Assert.NotEmpty(bestiary);
        Assert.All(bestiary, node => Assert.Contains("Bestiary", Tree.Masteries.Select(m => m.Name)));
    }

    [Fact]
    public void Only_one_node_hands_points_back_so_the_budget_stays_a_binary_choice()
    {
        var granters = Tree.Nodes.Values.Where(n => n.GrantedPoints > 0).ToList();

        Assert.Single(granters);
        Assert.Equal(20, granters[0].GrantedPoints);
    }

    [Fact]
    public void Exclusion_notables_are_detected_with_the_stat_that_makes_them_one()
    {
        var exclusions = Tree.Nodes.Values.Where(n => n.IsExclusionNotable).ToList();

        Assert.NotEmpty(exclusions);
        Assert.All(exclusions, node =>
        {
            Assert.Equal(NodeKind.Notable, node.Kind);
            Assert.StartsWith("Your Maps have no chance to contain", node.ExclusionStat!.Text);
        });
    }

    [Fact]
    public void Stat_markup_is_unwrapped_at_load_time() =>
        Assert.DoesNotContain(
            Tree.Nodes.Values.SelectMany(n => n.Stats),
            stat => stat.Text.Contains('[') || stat.Text.Contains(']'));
}
