using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Gui.Tests;

/// <summary>
/// Personal export coverage. Only compiled when MainViewModel.Personal.cs is present locally.
/// </summary>
public sealed partial class MainViewModelTests
{
    [Fact]
    public void Sending_a_hand_built_tree_exports_it_in_an_order()
    {
        var session = _fixture.Session;
        _viewModel.OnNodeActivated(NodeAtDistance(session.Tree, 6));
        _viewModel.PlanName = "Hand Built";

        _viewModel.ExportPlanCommand.Execute(null);

        var path = _viewModel.LastExportPath;
        Assert.NotNull(path);
        Assert.Equal("Hand Built.json", Path.GetFileName(path));

        var plan = AtlasPlan.Load(path!);
        Assert.Equal("Hand Built", plan.Name);
        Assert.Equal(session.PointsSpent, plan.Order.Count);
        Assert.Equal(session.Allocated.Order(), plan.Nodes.Order());
        Assert.Equal(Enumerable.Range(1, plan.Order.Count), plan.Order.Select(step => step.Step));
    }

    [Fact]
    public void An_exported_plan_can_be_walked_step_by_step_from_scratch()
    {
        var session = _fixture.Session;
        _viewModel.OnNodeActivated(NodeAtDistance(session.Tree, 8));
        _viewModel.ExportPlanCommand.Execute(null);

        var plan = AtlasPlan.Load(_viewModel.LastExportPath!);

        var live = new HashSet<int> { session.Tree.StartNodeId };
        for (var progress = PlanProgress.For(plan, live);
             !progress.IsComplete;
             progress = PlanProgress.For(plan, live))
        {
            live.Add(progress.NextStep!.NodeId);
            Assert.True(session.Tree.IsConnected(live), $"step {progress.NextStep!.Step} broke connectivity");
        }

        Assert.Equal(plan.Nodes.Order(), live.Order());
    }

    [Fact]
    public void Exporting_an_empty_tree_says_so_rather_than_writing_a_pointless_file()
    {
        _viewModel.ExportPlanCommand.Execute(null);

        Assert.Contains("Nothing to export", _viewModel.Status);
        Assert.Empty(PlanFolder.List(_fixture.ExportFolder));
    }

    [Fact]
    public async Task Exporting_a_solved_route_keeps_the_solvers_own_order()
    {
        _viewModel.PlanName = "Solved";
        _viewModel.Budget = 25;
        _viewModel.TimeLimitMs = 300;
        _viewModel.Weights.First(row => row.Category.Contains("Scarab", StringComparison.OrdinalIgnoreCase)).Weight = 10m;

        await _viewModel.SolveCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.LastPlan);

        _viewModel.ExportPlanCommand.Execute(null);
        var exported = AtlasPlan.Load(_viewModel.LastExportPath!);

        Assert.Equal(_viewModel.LastPlan!.Order.Select(step => step.NodeId), exported.Order.Select(step => step.NodeId));
        Assert.NotNull(exported.Profile);
        Assert.Equal("Solved", exported.Name);
    }

    [Fact]
    public async Task Editing_a_solved_route_by_hand_exports_what_is_actually_on_the_canvas()
    {
        var session = _fixture.Session;
        _viewModel.Budget = 25;
        _viewModel.TimeLimitMs = 300;
        ChaseScarabs();
        await _viewModel.SolveCommand.ExecuteAsync(null);

        var solvedNodes = _viewModel.LastPlan!.Nodes.ToHashSet();
        var extra = session.Reachable.First(id => !solvedNodes.Contains(id));
        _viewModel.OnNodeActivated(extra);

        _viewModel.ExportPlanCommand.Execute(null);
        var exported = AtlasPlan.Load(_viewModel.LastExportPath!);

        Assert.Contains(extra, exported.Nodes);
        Assert.Equal(session.Allocated.Order(), exported.Nodes.Order());
    }
}
