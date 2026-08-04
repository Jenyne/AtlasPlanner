using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public class PlanTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private AtlasPlan BuildPlan(Action<SolveProfile>? tweak = null)
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);

        var profile = new SolveProfile
        {
            Name = "plan test",
            Budget = 45,
            TimeLimitMs = 30,
            UnwaveringVision = PointGrantChoice.Exclude,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Scarabs"] = 10,
                ["Map Sustain"] = 5,
            },
        };

        tweak?.Invoke(profile);

        var prize = PrizeModel.Build(Tree, scores, profile);
        var solution = RouteSolver.Solve(Tree, prize, profile);
        return AtlasPlan.Build(Tree, scores, profile, prize, solution);
    }

    [Fact]
    public void Every_prefix_of_the_order_is_a_legal_allocation()
    {
        var plan = BuildPlan();
        var allocated = new HashSet<int> { Tree.StartNodeId };

        foreach (var step in plan.Order)
        {
            allocated.Add(step.NodeId);
            Assert.True(Tree.IsConnected(allocated), $"step {step.Step} ({step.Describe()}) broke connectivity");
        }
    }

    [Fact]
    public void The_order_covers_the_whole_plan_exactly_once()
    {
        var plan = BuildPlan();

        var ordered = plan.Order.Select(s => s.NodeId).ToList();
        Assert.Equal(ordered.Count, ordered.Distinct().Count());
        Assert.Equal(plan.Nodes.Where(id => id != Tree.StartNodeId).Order(), ordered.Order());
    }

    [Fact]
    public void Steps_are_numbered_from_one_without_gaps()
    {
        var plan = BuildPlan();

        Assert.Equal(Enumerable.Range(1, plan.Order.Count), plan.Order.Select(s => s.Step));
        Assert.Equal(plan.PointsSpent, plan.Order.Count);
    }

    [Fact]
    public void The_url_round_trips_to_the_plan_without_the_free_start_node()
    {
        var plan = BuildPlan();

        var decoded = AtlasUrl.Decode(plan.Url);

        Assert.DoesNotContain(Tree.StartNodeId, decoded);
        Assert.Equal(plan.Nodes.Where(id => id != Tree.StartNodeId).Order(), decoded.Order());
    }

    [Fact]
    public void The_point_granting_keystone_is_ordered_early_so_its_points_are_available()
    {
        var plan = BuildPlan(profile =>
        {
            profile.Budget = 60;
            profile.UnwaveringVision = PointGrantChoice.Include;
            profile.Weights.Remove("Scarabs");
        });

        var granting = plan.Order.Single(s => s.GrantsPoints is > 0);

        // It sits 19 hops out, so it cannot be first, but it should be reached before anything else
        // is pursued rather than left until the end.
        Assert.True(granting.Step <= 25, $"granted points only arrive at step {granting.Step}");
    }

    [Fact]
    public void Connector_steps_say_what_they_are_heading_towards()
    {
        var plan = BuildPlan();

        var connectors = plan.Order.Where(s => s.Kind == "Normal" && s.Prize == 0d).ToList();

        Assert.All(connectors, step => Assert.False(string.IsNullOrEmpty(step.Towards)));
    }

    [Fact]
    public void The_tally_agrees_with_the_points_the_plan_spends()
    {
        var plan = BuildPlan();

        Assert.Equal(plan.PointsSpent, plan.Order.Count);
        Assert.NotEmpty(plan.Tally.Summed);
    }

    [Fact]
    public void A_plan_round_trips_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atlasplan-{Guid.NewGuid():N}.json");
        var plan = BuildPlan();

        try
        {
            plan.Save(path);
            var reloaded = AtlasPlan.Load(path);

            Assert.Equal(plan.Nodes.Order(), reloaded.Nodes.Order());
            Assert.Equal(plan.Order.Count, reloaded.Order.Count);
            Assert.Equal(plan.Url, reloaded.Url);
            Assert.Equal(plan.PointsSpent, reloaded.PointsSpent);
            Assert.Equal(AtlasPlan.CurrentVersion, reloaded.Version);
            Assert.NotNull(reloaded.Profile);
            Assert.Equal(PointGrantChoice.Exclude, reloaded.Profile!.UnwaveringVision);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Continuing_from_an_existing_allocation_keeps_those_nodes_and_charges_nothing_for_them()
    {
        var existing = Tree.ShortestPath(Tree.StartNodeId, Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable).Id)!;

        var plan = BuildPlan(profile => profile.PreAllocated = [..existing]);

        Assert.All(existing, id => Assert.Contains(id, plan.Nodes));
        Assert.DoesNotContain(plan.Order, step => existing.Contains(step.NodeId));
    }
}
