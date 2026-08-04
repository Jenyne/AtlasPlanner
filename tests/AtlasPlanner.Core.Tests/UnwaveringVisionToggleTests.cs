using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

/// <summary>
/// The point-granting keystone is a big enough trade that the user decides, so these pin down that
/// the toggle is obeyed and that nothing automatic quietly overrides it.
/// </summary>
[Collection(TreeCollection.Name)]
public class UnwaveringVisionToggleTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private int UnwaveringVisionId => Tree.Nodes.Values.Single(n => n.Name == "Unwavering Vision").Id;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    private static SolveProfile Profile(PointGrantChoice choice, bool weightScarabs) => new()
    {
        Name = "toggle test",
        Budget = 60,
        TimeLimitMs = 30,
        UnwaveringVision = choice,
        Weights = weightScarabs
            ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Scarabs"] = 10, ["Map Sustain"] = 5 }
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Map Sustain"] = 5 },
    };

    private (RouteSolution Solution, PrizeModel Prize) Solve(SolveProfile profile)
    {
        var prize = PrizeModel.Build(Tree, Scores(), profile);
        return (RouteSolver.Solve(Tree, prize, profile), prize);
    }

    [Fact]
    public void Exclude_keeps_it_out_of_the_route()
    {
        var (solution, _) = Solve(Profile(PointGrantChoice.Exclude, weightScarabs: false));

        Assert.DoesNotContain(UnwaveringVisionId, solution.Nodes);
        Assert.False(solution.Stats.UsedPointGrantBranch);
        Assert.Equal(60, solution.Budget);
    }

    [Fact]
    public void Include_takes_it_and_raises_the_budget_by_the_points_it_grants()
    {
        var (solution, _) = Solve(Profile(PointGrantChoice.Include, weightScarabs: false));

        Assert.Contains(UnwaveringVisionId, solution.Nodes);
        Assert.True(solution.Stats.UsedPointGrantBranch);
        Assert.Equal(80, solution.Budget);
        Assert.Equal(20, solution.PointsGranted);
    }

    [Fact]
    public void Include_beats_the_rule_that_would_otherwise_rule_it_out()
    {
        // Weighting Scarabs normally rules the keystone out, since it bans them outright. Asking for
        // it explicitly has to win, or the toggle would be pointless.
        var (solution, prize) = Solve(Profile(PointGrantChoice.Include, weightScarabs: true));

        Assert.Contains(UnwaveringVisionId, solution.Nodes);
        Assert.DoesNotContain(prize.Nullified, n => n.NodeId == UnwaveringVisionId);
    }

    [Fact]
    public void Including_it_zeroes_the_category_it_switches_off()
    {
        var (_, prize) = Solve(Profile(PointGrantChoice.Include, weightScarabs: true));

        Assert.Contains(prize.ZeroedByRequirement, n => n.NodeId == UnwaveringVisionId && n.Category == "Scarabs");
        Assert.Equal(0d, prize.EffectiveWeights["Scarabs"]);
    }

    [Fact]
    public void Including_it_stops_the_route_paying_for_the_mechanic_it_disabled()
    {
        var withKeystone = Solve(Profile(PointGrantChoice.Include, weightScarabs: true)).Solution;
        var withoutKeystone = Solve(Profile(PointGrantChoice.Exclude, weightScarabs: true)).Solution;

        static int ScarabNodes(AtlasTree tree, RouteSolution solution) =>
            solution.Nodes.Count(id => tree[id].Stats.Any(s => s.Text.Contains("Scarab")));

        Assert.True(ScarabNodes(Tree, withKeystone) < ScarabNodes(Tree, withoutKeystone));
    }

    [Fact]
    public void Auto_declines_it_when_the_profile_wants_the_mechanic_it_bans()
    {
        var (solution, prize) = Solve(Profile(PointGrantChoice.Auto, weightScarabs: true));

        Assert.DoesNotContain(UnwaveringVisionId, solution.Nodes);
        Assert.Contains(prize.Nullified, n => n.NodeId == UnwaveringVisionId && n.Category == "Scarabs");
    }

    [Fact]
    public void Auto_is_free_to_consider_it_when_nothing_it_bans_is_wanted()
    {
        var (_, prize) = Solve(Profile(PointGrantChoice.Auto, weightScarabs: false));

        Assert.DoesNotContain(UnwaveringVisionId, prize.ForbiddenIds);
        Assert.DoesNotContain(UnwaveringVisionId, prize.RequiredIds);
    }

    [Fact]
    public void Banning_one_variety_is_not_treated_as_switching_the_category_off()
    {
        // Dimensional Barrier bans Breach Scarabs specifically. That is a downside, not a shutdown,
        // so it must not zero out Scarabs the way Unwavering Vision does.
        var profile = Profile(PointGrantChoice.Exclude, weightScarabs: true);
        profile.Require = ["Dimensional Barrier"];

        var (_, prize) = Solve(profile);

        Assert.Equal(10d, prize.EffectiveWeights["Scarabs"]);
        Assert.DoesNotContain(prize.ZeroedByRequirement, n => n.Category == "Scarabs");
    }
}
