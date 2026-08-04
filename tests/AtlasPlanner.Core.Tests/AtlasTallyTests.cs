using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Tally;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public class AtlasTallyTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    [Fact]
    public void Identical_templates_add_up_across_nodes()
    {
        const string template = "#% increased Scarabs found in your Maps";

        var contributors = Tree.Nodes.Values
            .Where(n => n.Stats.Any(s => s.Template == template))
            .Take(3)
            .ToList();

        var expected = contributors
            .Sum(n => n.Stats.First(s => s.Template == template).Value);

        var tally = AtlasTally.Compute(Tree, contributors.Select(n => n.Id), Scores());

        var entry = Assert.Single(tally.Summed, e => e.Text == template);
        Assert.Equal(expected, entry.Total);
        Assert.Equal(contributors.Count, entry.NodeCount);
    }

    [Fact]
    public void Stats_without_numbers_are_listed_as_flags_rather_than_summed()
    {
        var unwaveringVision = Tree.Nodes.Values.Single(n => n.Name == "Unwavering Vision");

        var tally = AtlasTally.Compute(Tree, [unwaveringVision.Id], Scores());

        Assert.Contains(tally.Flags, e => e.Text == "Scarabs cannot be found in Your Maps");
        Assert.DoesNotContain(tally.Summed, e => e.Text.Contains("cannot be found"));
    }

    [Fact]
    public void Multi_number_stats_are_counted_and_kept_out_of_the_summed_bucket()
    {
        var node = Tree.Nodes.Values.First(n => n.Stats.Any(s => s.Numbers.Count > 1));

        var tally = AtlasTally.Compute(Tree, [node.Id], Scores());

        Assert.NotEmpty(tally.Repeated);
        Assert.All(tally.Repeated, entry => Assert.Equal(0d, entry.Total));
    }

    [Fact]
    public void Points_spent_counts_every_node_but_the_free_start()
    {
        var ids = Tree.ShortestPath(Tree.StartNodeId, Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable).Id)!;

        var tally = AtlasTally.Compute(Tree, ids, Scores());

        Assert.Equal(ids.Count - 1, tally.PointsSpent);
    }

    [Fact]
    public void Granted_points_are_reported_separately_from_spend()
    {
        var unwaveringVision = Tree.Nodes.Values.Single(n => n.Name == "Unwavering Vision");

        var tally = AtlasTally.Compute(Tree, [unwaveringVision.Id], Scores());

        Assert.Equal(1, tally.PointsSpent);
        Assert.Equal(20, tally.PointsGranted);
    }

    [Fact]
    public void Unknown_ids_are_ignored_rather_than_throwing()
    {
        var tally = AtlasTally.Compute(Tree, [Tree.StartNodeId, -7, 999999], Scores());

        Assert.Equal(0, tally.PointsSpent);
    }

    [Fact]
    public void Rendered_entries_put_the_total_back_into_the_template()
    {
        var contributors = Tree.Nodes.Values
            .Where(n => n.Stats.Any(s => s.Template == "#% increased Scarabs found in your Maps"))
            .Take(2)
            .Select(n => n.Id);

        var tally = AtlasTally.Compute(Tree, contributors, Scores());
        var entry = Assert.Single(tally.Summed, e => e.Text == "#% increased Scarabs found in your Maps");

        Assert.Equal($"{entry.Total:0}% increased Scarabs found in your Maps", entry.Rendered);
    }

    [Fact]
    public void Ignored_templates_are_left_out_entirely()
    {
        var scores = Scores();
        scores.IgnoredTemplates.Add("#% increased Scarabs found in your Maps");

        var contributors = Tree.Nodes.Values
            .Where(n => n.Stats.Any(s => s.Template == "#% increased Scarabs found in your Maps"))
            .Take(2)
            .Select(n => n.Id);

        var tally = AtlasTally.Compute(Tree, contributors, scores);

        Assert.DoesNotContain(tally.Summed, e => e.Text == "#% increased Scarabs found in your Maps");
    }
}
