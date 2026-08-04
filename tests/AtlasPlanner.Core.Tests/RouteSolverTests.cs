using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public class RouteSolverTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    private static SolveProfile Profile(int budget = 40) => new()
    {
        Name = "test",
        Budget = budget,
        // Keeps the local search phase short; the tests are about correctness, not route quality.
        TimeLimitMs = 30,
        Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Scarabs"] = 10,
            ["Map Sustain"] = 5,
        },
    };

    private RouteSolution Solve(SolveProfile profile, out PrizeModel prize)
    {
        prize = PrizeModel.Build(Tree, Scores(), profile);
        return RouteSolver.Solve(Tree, prize, profile);
    }

    private RouteSolution Solve(SolveProfile profile) => Solve(profile, out _);

    [Fact]
    public void The_route_never_exceeds_its_budget()
    {
        var solution = Solve(Profile(budget: 25));

        Assert.True(solution.PointsSpent <= solution.Budget,
            $"spent {solution.PointsSpent} of {solution.Budget}");
    }

    [Fact]
    public void The_route_is_connected_and_anchored_at_the_start_node()
    {
        var solution = Solve(Profile());

        Assert.Contains(Tree.StartNodeId, solution.Nodes);
        Assert.True(Tree.IsConnected(solution.Nodes));
    }

    [Fact]
    public void Points_spent_matches_the_number_of_paid_nodes()
    {
        var solution = Solve(Profile());

        Assert.Equal(solution.Nodes.Count - 1, solution.PointsSpent);
    }

    [Fact]
    public void Required_nodes_are_always_in_the_route()
    {
        var profile = Profile(budget: 60);
        profile.Require = ["Significant Troves", "Remarkable Relics"];

        var solution = Solve(profile, out var prize);

        Assert.NotEmpty(prize.RequiredIds);
        Assert.All(prize.RequiredIds, id => Assert.Contains(id, solution.Nodes));
    }

    [Fact]
    public void Forbidden_nodes_are_never_in_the_route()
    {
        var profile = Profile();
        var scarabNotable = Tree.Nodes.Values.First(n => n.Name == "Significant Troves");
        profile.Forbid = [scarabNotable.Id.ToString()];

        var solution = Solve(profile);

        Assert.DoesNotContain(scarabNotable.Id, solution.Nodes);
    }

    [Fact]
    public void A_forbidden_region_is_avoided_entirely()
    {
        var profile = Profile();
        profile.ForbidRegions = ["Bestiary"];

        var solution = Solve(profile);

        Assert.DoesNotContain(solution.Nodes, id => Tree[id].Region == "Bestiary");
    }

    [Fact]
    public void The_same_seed_produces_the_same_route()
    {
        var first = Solve(Profile());
        var second = Solve(Profile());

        Assert.Equal(first.Nodes.Order(), second.Nodes.Order());
        Assert.Equal(first.Prize, second.Prize);
    }

    [Fact]
    public void A_required_node_out_of_budget_reach_is_reported_as_infeasible()
    {
        var distances = Tree.Distances(Tree.StartNodeId);

        // Nodes that grant points are excluded, since requiring one raises the budget to pay for itself.
        var faraway = Tree.Nodes.Values
            .Where(node => node.GrantedPoints == 0)
            .OrderByDescending(node => distances[Tree.IndexOf(node.Id)])
            .First();

        var profile = Profile(budget: 3);
        profile.Require = [faraway.Id.ToString()];

        var exception = Assert.Throws<InfeasibleRouteException>(() => Solve(profile));

        Assert.Contains("budget", exception.Message);
    }

    [Fact]
    public void Requiring_the_point_granting_keystone_raises_the_budget_enough_to_reach_it()
    {
        var profile = Profile(budget: 5);
        profile.Weights.Remove("Scarabs");
        profile.Require = ["Unwavering Vision"];

        var solution = Solve(profile);

        Assert.Equal(25, solution.Budget);
        Assert.Contains(Tree.Nodes.Values.Single(n => n.Name == "Unwavering Vision").Id, solution.Nodes);
    }

    [Fact]
    public void Weighting_a_category_pulls_the_route_towards_it()
    {
        var scarabs = Profile();
        var bestiary = Profile();
        bestiary.Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Bestiary"] = 10 };

        var scarabRoute = Solve(scarabs);
        var bestiaryRoute = Solve(bestiary);

        Assert.True(
            bestiaryRoute.Nodes.Count(id => Tree[id].Region == "Bestiary") >
            scarabRoute.Nodes.Count(id => Tree[id].Region == "Bestiary"));
    }
}
