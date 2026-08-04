using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public class PlanProgressTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private AtlasPlan BuildPlan(Action<SolveProfile>? tweak = null)
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);

        var profile = new SolveProfile
        {
            Name = "progress test",
            Budget = 30,
            TimeLimitMs = 30,
            UnwaveringVision = PointGrantChoice.Exclude,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Scarabs"] = 10 },
        };

        tweak?.Invoke(profile);

        var prize = PrizeModel.Build(Tree, scores, profile);
        var solution = RouteSolver.Solve(Tree, prize, profile);
        return AtlasPlan.Build(Tree, scores, profile, prize, solution);
    }

    /// <summary>The state of a fresh atlas: the free start node and nothing else.</summary>
    private HashSet<int> FreshTree() => [Tree.StartNodeId];

    [Fact]
    public void A_fresh_tree_has_the_whole_plan_ahead_of_it()
    {
        var plan = BuildPlan();

        var progress = PlanProgress.For(plan, FreshTree());

        Assert.Equal(0, progress.PointsPlaced);
        Assert.Equal(plan.Order.Count, progress.PointsRemaining);
        Assert.False(progress.IsComplete);
        Assert.Equal(1, progress.NextStep!.Step);
    }

    [Fact]
    public void Placing_points_in_order_advances_one_step_at_a_time()
    {
        var plan = BuildPlan();
        var allocated = FreshTree();

        foreach (var step in plan.Order)
        {
            var before = PlanProgress.For(plan, allocated);
            Assert.Equal(step.Step, before.NextStep!.Step);
            Assert.Equal(step.NodeId, before.NextStep!.NodeId);

            allocated.Add(step.NodeId);
        }

        var finished = PlanProgress.For(plan, allocated);
        Assert.True(finished.IsComplete);
        Assert.Null(finished.NextStep);
        Assert.Empty(finished.Next(5));
        Assert.False(finished.HasDrifted);
    }

    [Fact]
    public void Asking_for_more_points_than_are_left_gets_only_what_is_left()
    {
        var plan = BuildPlan();
        var allocated = FreshTree();
        foreach (var step in plan.Order.SkipLast(2))
            allocated.Add(step.NodeId);

        var progress = PlanProgress.For(plan, allocated);

        Assert.Equal(2, progress.Next(10).Count);
        Assert.Empty(progress.Next(0));
        Assert.Empty(progress.Next(-1));
    }

    [Fact]
    public void The_next_points_come_back_in_plan_order()
    {
        var plan = BuildPlan();

        var next = PlanProgress.For(plan, FreshTree()).Next(5);

        Assert.Equal([1, 2, 3, 4, 5], next.Select(s => s.Step));
        Assert.Equal(plan.Order.Take(5).Select(s => s.NodeId), next.Select(s => s.NodeId));
    }

    [Fact]
    public void A_point_taken_out_of_order_counts_as_placed_without_disturbing_the_queue()
    {
        var plan = BuildPlan();
        var jumpedAhead = plan.Order[7];

        var progress = PlanProgress.For(plan, [Tree.StartNodeId, jumpedAhead.NodeId]);

        Assert.Equal(1, progress.PointsPlaced);
        Assert.True(progress.IsDone(jumpedAhead.NodeId));
        Assert.DoesNotContain(jumpedAhead.NodeId, progress.Remaining.Select(s => s.NodeId));

        // The head of the queue is still the earliest step that has not been taken.
        Assert.Equal(1, progress.NextStep!.Step);
    }

    [Fact]
    public void Points_spent_outside_the_plan_are_reported_as_drift()
    {
        var plan = BuildPlan();
        var stranger = Tree.Nodes.Values.First(n => n.IsAllocatable && !plan.Nodes.Contains(n.Id));

        var progress = PlanProgress.For(plan, [Tree.StartNodeId, stranger.Id]);

        Assert.Equal([stranger.Id], progress.OffPlan);
        Assert.True(progress.HasDrifted);
        Assert.False(progress.IsOnPlan(stranger.Id));
        Assert.Null(progress.StepNumberOf(stranger.Id));
    }

    [Fact]
    public void The_free_start_node_is_never_mistaken_for_drift()
    {
        var plan = BuildPlan();

        var progress = PlanProgress.For(plan, FreshTree());

        Assert.Empty(progress.OffPlan);
        Assert.False(progress.HasDrifted);
    }

    [Fact]
    public void Groundwork_the_plan_assumed_but_the_tree_lacks_is_reported()
    {
        var notable = Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable);
        var existing = Tree.ShortestPath(Tree.StartNodeId, notable.Id)!;
        var plan = BuildPlan(profile => profile.PreAllocated = [..existing]);

        // Someone exported a plan continuing an existing tree, then respecced before importing it.
        var progress = PlanProgress.For(plan, FreshTree());

        Assert.NotEmpty(progress.MissingGroundwork);
        Assert.True(progress.HasDrifted);
        Assert.All(progress.MissingGroundwork, id => Assert.Contains(id, plan.PreAllocated));
    }

    [Fact]
    public void Groundwork_that_is_present_is_not_reported()
    {
        var notable = Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable);
        var existing = Tree.ShortestPath(Tree.StartNodeId, notable.Id)!;
        var plan = BuildPlan(profile => profile.PreAllocated = [..existing]);

        var progress = PlanProgress.For(plan, [Tree.StartNodeId, ..existing]);

        Assert.Empty(progress.MissingGroundwork);
        Assert.Empty(progress.OffPlan);
        Assert.False(progress.HasDrifted);
    }

    [Fact]
    public void Step_numbers_are_recoverable_from_a_node_id_for_labelling()
    {
        var plan = BuildPlan();
        var progress = PlanProgress.For(plan, FreshTree());

        foreach (var step in plan.Order)
        {
            Assert.True(progress.IsOnPlan(step.NodeId));
            Assert.Equal(step.Step, progress.StepNumberOf(step.NodeId));
            Assert.Equal(step.NodeId, progress.StepOf(step.NodeId)!.NodeId);
        }
    }

    [Fact]
    public void How_far_through_the_plan_a_node_sits_runs_from_just_above_zero_to_one()
    {
        var plan = BuildPlan();
        var progress = PlanProgress.For(plan, FreshTree());

        Assert.Equal(1d, progress.FractionOf(plan.Order[^1].NodeId), 6);
        Assert.InRange(progress.FractionOf(plan.Order[0].NodeId), 0d, 1d);
        Assert.Equal(0d, progress.FractionOf(Tree.StartNodeId));
    }

    [Fact]
    public void A_plan_with_no_steps_is_complete_rather_than_broken()
    {
        var progress = PlanProgress.For(new AtlasPlan { Name = "empty" }, FreshTree());

        Assert.True(progress.IsComplete);
        Assert.Equal(0, progress.TotalSteps);
        Assert.Null(progress.NextStep);
        Assert.Equal(0d, progress.FractionOf(Tree.StartNodeId));
        Assert.Contains("no steps", progress.Summary);
    }

    [Fact]
    public void The_summary_says_where_the_tree_is_up_to()
    {
        var plan = BuildPlan();
        var allocated = FreshTree();
        foreach (var step in plan.Order.Take(3))
            allocated.Add(step.NodeId);

        var summary = PlanProgress.For(plan, allocated).Summary;

        Assert.Contains("3 of", summary);
        Assert.Contains($"{plan.Order.Count - 3} to go", summary);
    }
}
