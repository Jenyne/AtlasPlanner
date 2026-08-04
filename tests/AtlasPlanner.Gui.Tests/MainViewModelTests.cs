using System.Text.RegularExpressions;
using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;
using AtlasPlanner.Gui.Rendering;
using AtlasPlanner.Gui.Services;
using AtlasPlanner.Gui.ViewModels;

namespace AtlasPlanner.Gui.Tests;

[Collection(ViewModelCollection.Name)]
public sealed partial class MainViewModelTests
{
    private readonly MainViewModelFixture _fixture;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests(MainViewModelFixture fixture)
    {
        _fixture = fixture;
        _viewModel = fixture.ViewModel;
        fixture.Reset();
    }

    [Fact]
    public void Chasing_and_blocking_from_the_Goals_panel_update_the_summary()
    {
        var scarabs = _viewModel.Weights.Single(row => row.Category == "Scarabs");
        var blight = _viewModel.Weights.Single(row => row.Category == "Blight");

        _viewModel.ChaseMechanicCommand.Execute(scarabs);
        scarabs.Priority = ChasePriority.High;
        _viewModel.BlockMechanicCommand.Execute(blight);

        Assert.Equal(MechanicMode.Chase, scarabs.Mode);
        Assert.Equal(MechanicMode.Block, blight.Mode);
        Assert.Contains(scarabs, _viewModel.ChaseList);
        Assert.Contains(blight, _viewModel.BlockList);
        Assert.Contains("Chasing Scarabs·High", _viewModel.ActiveWeightSummary);
        Assert.Contains("Blocking Blight", _viewModel.ActiveWeightSummary);
    }

    [Fact]
    public void Must_take_chips_mirror_Ctrl_click_marks()
    {
        var notable = _fixture.Session.Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable && n.Name.Length > 0);

        _viewModel.OnNodeRequired(notable.Id);

        Assert.Contains(_viewModel.RequiredChips, chip => chip.Label == notable.Name);

        _viewModel.RemoveRequiredChipCommand.Execute(_viewModel.RequiredChips.First());
        Assert.Empty(_viewModel.RequiredChips);
        Assert.DoesNotContain(notable.Name, _viewModel.RequireText);
    }

    [Fact]
    public void Initialisation_loads_the_tree_with_empty_goals()
    {
        var tree = _fixture.Session.Tree;

        Assert.NotNull(_viewModel.Images);
        Assert.Contains($"{tree.NodeCount} nodes", _viewModel.TreeLabel);
        Assert.Contains($"{tree.Edges.Count} connections", _viewModel.TreeLabel);
        Assert.Equal($"0 / {tree.TotalPoints} points", _viewModel.PointsLabel);

        Assert.NotEmpty(_viewModel.Weights);
        Assert.Equal(_viewModel.Weights.Count, _viewModel.QuickTray.Count);
        Assert.Empty(_viewModel.QuickBlock);
        Assert.Empty(_viewModel.QuickChase);
        Assert.DoesNotContain(_viewModel.Weights, row => row.IsActive);
        Assert.Contains("Nothing to chase", _viewModel.ActiveWeightSummary);
    }

    [Fact]
    public void The_scene_covers_every_node_with_both_straight_and_orbital_connectors()
    {
        var scene = _fixture.Session.Scene;

        Assert.Equal(_fixture.Session.Tree.NodeCount, scene.Nodes.Count);
        Assert.All(scene.Nodes, visual => Assert.True(visual.HitRadius > 0, $"{visual.Node.Name} has no click target"));

        Assert.NotEmpty(scene.Edges);
        Assert.Contains(scene.Edges, edge => edge.IsArc);
        Assert.Contains(scene.Edges, edge => !edge.IsArc);
        Assert.NotEmpty(scene.Groups);
    }

    [Fact]
    public void Paired_gateways_are_linked_in_the_graph_but_not_drawn_as_a_line()
    {
        var tree = _fixture.Session.Tree;
        var scene = _fixture.Session.Scene;

        var pairs = tree.Edges
            .Where(edge => tree[edge.FromId].IsGateway && tree[edge.ToId].IsGateway)
            .ToArray();

        Assert.NotEmpty(pairs);
        Assert.All(pairs, pair =>
        {
            Assert.Contains(pair.ToId, tree[pair.FromId].Neighbours);
            Assert.DoesNotContain(scene.Edges, edge => edge.FromId == pair.FromId && edge.ToId == pair.ToId);
        });
    }

    [Fact]
    public void A_gateway_still_paths_through_to_its_twin()
    {
        var tree = _fixture.Session.Tree;
        var gateway = tree.Nodes.Values.First(node => node.IsGateway);
        var twin = tree.Nodes.Values.Single(node =>
            node.IsGateway && node.Id != gateway.Id && node.Neighbours.Contains(gateway.Id));

        _viewModel.OnNodeActivated(twin.Id);

        Assert.True(_fixture.Session.IsAllocated(twin.Id));
        Assert.True(tree.IsConnected(_fixture.Session.Allocated));
    }

    [Fact]
    public void Clicking_a_distant_node_paths_all_the_way_to_it()
    {
        var session = _fixture.Session;
        var target = NodeAtDistance(session.Tree, 6);

        _viewModel.OnNodeActivated(target);

        Assert.True(session.IsAllocated(target));
        Assert.Equal(6, session.PointsSpent);
        Assert.True(session.Tree.IsConnected(session.Allocated));
        Assert.Contains("6 nodes", _viewModel.Status);
        Assert.Contains("6 /", _viewModel.PointsLabel);
    }

    [Fact]
    public void Removing_a_node_takes_everything_that_hung_off_it()
    {
        var session = _fixture.Session;
        _viewModel.OnNodeActivated(NodeAtDistance(session.Tree, 6));

        // The path is a single chain, so the one allocated neighbour of the start carries all of it.
        var start = session.Tree.StartNodeId;
        var firstStep = session.Allocated.Single(id => id != start && session.Tree[start].Neighbours.Contains(id));

        _viewModel.OnNodeRemoved(firstStep);

        Assert.Equal(0, session.PointsSpent);
        Assert.Contains("hung off it", _viewModel.Status);
    }

    [Fact]
    public void The_start_node_cannot_be_removed()
    {
        var session = _fixture.Session;

        _viewModel.OnNodeRemoved(session.Tree.StartNodeId);

        Assert.True(session.IsAllocated(session.Tree.StartNodeId));
        Assert.Equal("Nothing to remove there.", _viewModel.Status);
    }

    [Fact]
    public void Ctrl_marking_a_node_puts_it_in_Must_take_and_solve_will_see_it()
    {
        var session = _fixture.Session;
        var notable = session.Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable && n.Name.Length > 0);

        _viewModel.OnNodeRequired(notable.Id);

        Assert.Contains(notable.Id, _viewModel.RequiredNodes!);
        Assert.Contains(notable.Name, _viewModel.RequireText);
        Assert.Contains("Must take", _viewModel.Status);

        // Toggle off again.
        _viewModel.OnNodeRequired(notable.Id);
        Assert.Null(_viewModel.RequiredNodes);
        Assert.DoesNotContain(notable.Name, _viewModel.RequireText);
    }

    [Fact]
    public void Alt_marking_a_node_puts_it_in_Never_take_and_clears_Must_take()
    {
        var session = _fixture.Session;
        var notable = session.Tree.Nodes.Values.First(n => n.Kind == NodeKind.Notable && n.Name.Length > 0);

        _viewModel.OnNodeRequired(notable.Id);
        _viewModel.OnNodeForbidden(notable.Id);

        Assert.Null(_viewModel.RequiredNodes);
        Assert.Contains(notable.Id, _viewModel.ForbiddenNodes!);
        Assert.Contains(notable.Name, _viewModel.ForbidText);
        Assert.DoesNotContain(notable.Name, _viewModel.RequireText);
    }

    [Fact]
    public void Banning_an_allocated_node_removes_it_from_the_tree()
    {
        var session = _fixture.Session;
        var target = NodeAtDistance(session.Tree, 4);
        _viewModel.OnNodeActivated(target);
        Assert.True(session.IsAllocated(target));

        _viewModel.OnNodeForbidden(target);

        Assert.False(session.IsAllocated(target));
        Assert.Contains(target, _viewModel.ForbiddenNodes!);
        Assert.Contains("Removed", _viewModel.Status);
    }

    [Fact]
    public async Task Removing_a_node_from_a_solved_route_rebuilds_the_order()
    {
        var session = _fixture.Session;
        _viewModel.Budget = 25;
        _viewModel.TimeLimitMs = 300;
        _viewModel.Weights.First(row => row.Category.Contains("Scarab", StringComparison.OrdinalIgnoreCase)).Weight = 10m;

        await _viewModel.SolveCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.LastPlan);
        Assert.True(_viewModel.Order.Count > 5);

        var tip = _viewModel.Order[^1].NodeId;
        var before = _viewModel.Order.Count;

        _viewModel.OnNodeRemoved(tip);

        Assert.True(_viewModel.Order.Count < before);
        Assert.Equal(session.PointsSpent, _viewModel.Order.Count);
        Assert.DoesNotContain(_viewModel.Order, row => row.NodeId == tip);
        Assert.Equal(Enumerable.Range(1, _viewModel.Order.Count), _viewModel.Order.Select(row => row.Step));
        Assert.True(session.Tree.IsConnected(session.Allocated));
    }

    [Fact]
    public async Task A_Must_take_mark_is_honoured_by_solve()
    {
        var session = _fixture.Session;
        var distances = session.Tree.Distances(session.Tree.StartNodeId);
        var notable = session.Tree.Nodes.Values
            .Where(n => n.Kind == NodeKind.Notable && n.Name.Length > 0 && !n.IsExclusionNotable)
            .Select(n => (Node: n, Distance: distances[session.Tree.IndexOf(n.Id)]))
            .Where(pair => pair.Distance is > 0 and < 20)
            .OrderBy(pair => pair.Distance)
            .Select(pair => pair.Node)
            .First();

        _viewModel.OnNodeRequired(notable.Id);
        _viewModel.Budget = 40;
        _viewModel.TimeLimitMs = 500;
        _viewModel.UnwaveringVision = PointGrantChoice.Exclude;

        await _viewModel.SolveCommand.ExecuteAsync(null);

        Assert.NotNull(_viewModel.LastPlan);
        Assert.Contains(notable.Id, _viewModel.LastPlan!.Nodes);
    }

    [Fact]
    public async Task Solving_produces_a_connected_plan_with_an_order_and_a_tally()
    {
        var session = _fixture.Session;
        _viewModel.Budget = 30;
        _viewModel.TimeLimitMs = 400;
        _viewModel.UnwaveringVision = PointGrantChoice.Exclude;
        ChaseScarabs();

        await _viewModel.SolveCommand.ExecuteAsync(null);

        var plan = _viewModel.LastPlan;
        Assert.NotNull(plan);
        Assert.Equal(30, plan.Budget);
        Assert.InRange(plan.PointsSpent, 1, 30);
        Assert.True(session.Tree.IsConnected(session.Allocated));

        // The panels have to agree with the plan the solver handed back.
        Assert.Equal(plan.PointsSpent, session.PointsSpent);
        Assert.Equal(plan.Order.Count, _viewModel.Order.Count);
        Assert.Equal(plan.Order.Count, _viewModel.PlanSteps?.Count);
        Assert.NotEmpty(_viewModel.TallySummed);
        Assert.Contains("Score", _viewModel.SolveSummary);
        Assert.Contains($"{plan.PointsSpent}/30 points", _viewModel.SolveSummary);
    }

    [Fact]
    public async Task The_allocation_order_stays_connected_at_every_step()
    {
        _viewModel.Budget = 25;
        _viewModel.TimeLimitMs = 400;
        ChaseScarabs();

        await _viewModel.SolveCommand.ExecuteAsync(null);

        var tree = _fixture.Session.Tree;
        var taken = new HashSet<int> { tree.StartNodeId };
        foreach (var step in _viewModel.LastPlan!.Order.OrderBy(step => step.Step))
        {
            taken.Add(step.NodeId);
            Assert.True(tree.IsConnected(taken), $"step {step.Step} ({step.Name}) breaks connectivity");
        }
    }

    [Fact]
    public async Task Solving_with_nothing_to_aim_at_says_so_instead_of_running()
    {
        foreach (var row in _viewModel.Weights)
            row.Clear();

        await _viewModel.SolveCommand.ExecuteAsync(null);

        Assert.Null(_viewModel.LastPlan);
        Assert.Contains("aim at", _viewModel.Status);
        Assert.Contains("Nothing to chase", _viewModel.ActiveWeightSummary);
    }

    [Fact]
    public async Task A_required_node_always_ends_up_in_the_plan()
    {
        const string required = "Significant Troves";
        _viewModel.Budget = 60;
        _viewModel.TimeLimitMs = 400;
        _viewModel.RequireText = required;

        await _viewModel.SolveCommand.ExecuteAsync(null);

        var node = _fixture.Session.Tree.Nodes.Values.Single(candidate => candidate.Name == required);
        Assert.True(_fixture.Session.IsAllocated(node.Id), _viewModel.Status);
        Assert.Contains(_viewModel.Order, row => row.Name == required);
    }

    [Fact]
    public async Task Switching_a_category_off_buys_its_off_switch_and_avoids_its_nodes()
    {
        var session = _fixture.Session;
        var delirium = _viewModel.Weights.Single(row => row.Category == "Delirium");
        delirium.Weight = 0m;
        delirium.IsOn = false;

        _viewModel.Budget = 60;
        _viewModel.TimeLimitMs = 500;

        await _viewModel.SolveCommand.ExecuteAsync(null);

        Assert.NotNull(_viewModel.LastPlan);

        // Ominous Silence is the notable that stops Mirrors of Delirium appearing.
        var offSwitch = session.Tree.Nodes.Values.Single(node => node.Name == "Ominous Silence");
        Assert.True(session.IsAllocated(offSwitch.Id), _viewModel.Status);
        Assert.Contains("Ominous Silence", _viewModel.SolveNotes);

        // Nothing else from that mechanic should have been picked up for its own sake.
        var deliriumNodes = session.Allocated
            .Select(id => session.Tree[id])
            .Where(node => node.Id != offSwitch.Id && node.Stats.Any(stat =>
                stat.Text.Contains("Delirium", StringComparison.OrdinalIgnoreCase)
                || stat.Text.Contains("Simulacrum", StringComparison.OrdinalIgnoreCase)))
            .Select(node => node.Name)
            .ToArray();

        Assert.Empty(deliriumNodes);
    }

    [Fact]
    public void A_category_switched_off_stops_counting_as_something_to_chase()
    {
        var row = _viewModel.Weights.Single(row => row.Category == "Scarabs");
        row.ChaseWith(ChasePriority.Normal);
        Assert.True(row.IsActive);

        row.IsOn = false;

        Assert.False(row.IsActive);
        Assert.False(row.IsWeightEditable);
        Assert.Contains("Blocking Scarabs", _viewModel.ActiveWeightSummary);
    }

    [Fact]
    public async Task Switching_everything_off_is_still_a_goal_the_solver_will_run_with()
    {
        foreach (var row in _viewModel.Weights)
            row.Clear();

        _viewModel.Weights.Single(row => row.Category == "Breach").IsOn = false;
        _viewModel.Budget = 40;
        _viewModel.TimeLimitMs = 500;

        await _viewModel.SolveCommand.ExecuteAsync(null);

        Assert.NotNull(_viewModel.LastPlan);
        var barrier = _fixture.Session.Tree.Nodes.Values.Single(node => node.Name == "Dimensional Barrier");
        Assert.True(_fixture.Session.IsAllocated(barrier.Id), _viewModel.Status);
    }

    [Fact]
    public async Task Requiring_a_repeated_node_name_takes_every_copy_of_it()
    {
        const string repeated = "Mercenary Chance";
        var tree = _fixture.Session.Tree;
        var copies = tree.Nodes.Values.Where(node => node.Name == repeated).ToArray();

        // The premise of the test: this name really is on more than one node.
        Assert.Equal(7, copies.Length);

        _viewModel.Budget = 100;
        _viewModel.TimeLimitMs = 500;
        _viewModel.RequireText = repeated;

        await _viewModel.SolveCommand.ExecuteAsync(null);

        Assert.NotNull(_viewModel.LastPlan);
        Assert.All(copies, copy => Assert.True(
            _fixture.Session.IsAllocated(copy.Id),
            $"#{copy.Id} was left out: {_viewModel.Status}"));
    }

    [Fact]
    public async Task An_unknown_node_name_still_reports_the_near_misses()
    {
        _viewModel.Budget = 40;
        _viewModel.TimeLimitMs = 300;
        _viewModel.RequireText = "Mercenary Chnace";

        await _viewModel.SolveCommand.ExecuteAsync(null);

        Assert.Null(_viewModel.LastPlan);
        Assert.Contains("no node named", _viewModel.Status);
    }

    [Fact]
    public void Searching_rings_the_matches_and_names_the_first()
    {
        var centred = new List<int>();
        _viewModel.RequestCentreOn = centred.Add;
        _viewModel.SearchText = "Scarab";

        _viewModel.SearchCommand.Execute(null);

        Assert.NotNull(_viewModel.Highlighted);
        Assert.NotEmpty(_viewModel.Highlighted!);
        Assert.Contains("match", _viewModel.SearchSummary);
        Assert.Single(centred);

        _viewModel.ClearSearchCommand.Execute(null);
        Assert.Null(_viewModel.Highlighted);
        Assert.Empty(_viewModel.SearchSummary);
    }

    [Fact]
    public void Searching_for_something_absent_says_so_without_highlighting()
    {
        _viewModel.SearchText = "zzz not a real node zzz";

        _viewModel.SearchCommand.Execute(null);

        Assert.Empty(_viewModel.Highlighted!);
        Assert.Equal("No matches.", _viewModel.SearchSummary);
    }

    [Fact]
    public void Hovering_reports_the_node_under_the_cursor()
    {
        var keystone = _fixture.Session.Tree.Nodes.Values.First(node => node.Kind == NodeKind.Keystone);

        _viewModel.OnHoverChanged(keystone);

        var detail = _viewModel.HoveredNode;
        Assert.NotNull(detail);
        Assert.Equal(keystone.Name, detail.Name);
        Assert.Contains("Keystone", detail.Subtitle);
        Assert.Contains("from start", detail.Subtitle);
        Assert.False(detail.IsAllocated);
        Assert.NotEmpty(detail.Stats);

        _viewModel.OnHoverChanged(null);
        Assert.Null(_viewModel.HoveredNode);
    }

    [Fact]
    public void A_tree_url_round_trips_through_the_session()
    {
        var session = _fixture.Session;
        _viewModel.OnNodeActivated(NodeAtDistance(session.Tree, 5));
        var expected = session.Allocated.Order().ToArray();

        var url = session.ToUrl();
        session.Clear();
        Assert.Equal(0, session.PointsSpent);

        var unknown = session.LoadUrl(url);

        Assert.Empty(unknown);
        Assert.Equal(expected, session.Allocated.Order().ToArray());
    }

    [Fact]
    public async Task The_copied_url_is_one_link_the_official_atlas_page_accepts()
    {
        var session = _fixture.Session;
        _viewModel.OnNodeActivated(NodeAtDistance(session.Tree, 5));
        var expected = session.Allocated.Order().ToArray();

        var copied = string.Empty;
        _viewModel.WriteClipboard = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await _viewModel.CopyUrlCommand.ExecuteAsync(null);

        Assert.StartsWith("https://www.pathofexile.com/fullscreen-atlas-skill-tree/", copied);
        Assert.True(AtlasUrl.IsAtlasUrl(copied), copied);

        // One prefix, not two: URL parsers that take the first pathofexile.com match stay correct.
        Assert.Single(Regex.Matches(copied, "pathofexile\\.com"));
        Assert.Equal(expected, AtlasUrl.Decode(copied).Append(session.Tree.StartNodeId).Order().ToArray());
    }

    [Fact]
    public void Clearing_returns_the_tree_to_just_the_start_node()
    {
        var session = _fixture.Session;
        _viewModel.OnNodeActivated(NodeAtDistance(session.Tree, 4));

        _viewModel.ClearRouteCommand.Execute(null);

        Assert.Equal([session.Tree.StartNodeId], session.Allocated.ToArray());
        Assert.Equal(session.Tree.TotalPoints, _viewModel.Budget);
        Assert.Empty(_viewModel.Order);
        Assert.Null(_viewModel.PlanSteps);
        Assert.Null(_viewModel.LastPlan);
    }

    [Fact]
    public void The_budget_text_box_ignores_nonsense_and_clamps_the_rest()
    {
        _viewModel.BudgetText = "42";
        Assert.Equal(42, _viewModel.Budget);

        _viewModel.BudgetText = "not a number";
        Assert.Equal(42, _viewModel.Budget);

        _viewModel.BudgetText = "9999";
        Assert.Equal(400, _viewModel.Budget);
    }

    [Fact]
    public void The_zoom_readout_tracks_the_camera()
    {
        _viewModel.OnScaleChanged(Camera.MaxScale / 2);

        Assert.Equal("zoom 50%", _viewModel.ZoomLabel);
    }

    [Fact]
    public void The_request_and_the_tree_survive_a_restart()
    {
        var session = _fixture.Session;
        _viewModel.PlanName = "Remembered plan";
        _viewModel.Budget = 77;
        _viewModel.TimeLimitMs = 1234;
        _viewModel.ExclusionWeight = 40m;
        _viewModel.UnwaveringVision = PointGrantChoice.Include;
        _viewModel.RequireText = "Significant Troves";
        _viewModel.ShowStepNumbers = false;
        _viewModel.Weights.Single(row => row.Category == "Scarabs").Weight = 17m;
        _viewModel.Weights.Single(row => row.Category == "Breach").IsOn = false;
        _viewModel.OnNodeActivated(NodeAtDistance(session.Tree, 5));

        var expectedTree = session.Allocated.Order().ToArray();
        Assert.True(_viewModel.SaveSettings());

        var reopened = PlannerSettings.Load(_fixture.SettingsPath);

        Assert.Equal("Remembered plan", reopened.PlanName);
        Assert.Equal(77, reopened.Budget);
        Assert.Equal(1234, reopened.TimeLimitMs);
        Assert.Equal(40d, reopened.ExclusionWeight);
        Assert.Equal(PointGrantChoice.Include, reopened.UnwaveringVision);
        Assert.Equal("Significant Troves", reopened.Require);
        Assert.False(reopened.ShowStepNumbers);
        // 17 snaps to High priority (15) in the Goals model.
        Assert.Equal(15d, reopened.Weights["Scarabs"]);
        Assert.Equal(["Breach"], reopened.SwitchedOff);

        // The saved tree has to come back as the same allocation, not just the same length.
        session.Clear();
        session.LoadUrl(reopened.Tree);
        Assert.Equal(expectedTree, session.Allocated.Order().ToArray());
    }

    [Fact]
    public void A_missing_or_unreadable_settings_file_falls_back_to_defaults()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"atlasplanner-absent-{Guid.NewGuid():N}.json");
        Assert.Equal("Unnamed plan", PlannerSettings.Load(missing).PlanName);

        var corrupt = Path.Combine(Path.GetTempPath(), $"atlasplanner-corrupt-{Guid.NewGuid():N}.json");
        File.WriteAllText(corrupt, "{ this is not json");
        try
        {
            var settings = PlannerSettings.Load(corrupt);
            Assert.Equal("Unnamed plan", settings.PlanName);
            Assert.Empty(settings.SwitchedOff);
        }
        finally
        {
            File.Delete(corrupt);
        }
    }

    [Fact]
    public void Clear_markers_drops_must_and_never_without_touching_chase()
    {
        var scarabs = _viewModel.Weights.Single(row => row.Category == "Scarabs");
        _viewModel.ChaseMechanicCommand.Execute(scarabs);
        _viewModel.RequireText = "Significant Troves";
        _viewModel.ForbidText = "Blighted Maps";

        _viewModel.ClearMarkersCommand.Execute(null);

        Assert.Empty(_viewModel.RequireText);
        Assert.Empty(_viewModel.ForbidText);
        Assert.Equal(MechanicMode.Chase, scarabs.Mode);
    }

    [Fact]
    public void Reset_to_default_clears_goals_marks_and_tree()
    {
        var scarabs = _viewModel.Weights.Single(row => row.Category == "Scarabs");
        var blight = _viewModel.Weights.Single(row => row.Category == "Blight");
        _viewModel.ChaseMechanicCommand.Execute(scarabs);
        _viewModel.BlockMechanicCommand.Execute(blight);
        _viewModel.RequireText = "Significant Troves";

        var near = NodeAtDistance(_fixture.Session.Tree, 1);
        Assert.NotEmpty(_fixture.Session.AllocateTo(near));
        Assert.True(_fixture.Session.PointsSpent > 0);

        _viewModel.ResetGoalsCommand.Execute(null);

        Assert.All(_viewModel.Weights, row => Assert.Equal(MechanicMode.Ignore, row.Mode));
        Assert.Empty(_viewModel.RequireText);
        Assert.Empty(_viewModel.ForbidText);
        Assert.Equal(0, _fixture.Session.PointsSpent);
        Assert.Contains("Nothing to chase", _viewModel.ActiveWeightSummary);
    }

    [Fact]
    public void Quick_mode_moves_mechanics_between_tray_block_and_chase()
    {
        var breach = _viewModel.Weights.Single(row => row.Category == "Breach");
        Assert.Contains(breach, _viewModel.QuickTray);

        _viewModel.ApplyQuickMode(breach, MechanicMode.Chase);
        Assert.Equal(MechanicMode.Chase, breach.Mode);
        Assert.Equal(ChasePriority.High, breach.Priority);
        Assert.Contains(breach, _viewModel.ChaseList);
        Assert.Contains(breach, _viewModel.QuickChase);
        Assert.DoesNotContain(breach, _viewModel.QuickTray);

        _viewModel.ApplyQuickMode(breach, MechanicMode.Block);
        Assert.Equal(MechanicMode.Block, breach.Mode);
        Assert.Contains(breach, _viewModel.BlockList);
        Assert.Contains(breach, _viewModel.QuickBlock);

        _viewModel.ApplyQuickMode(breach, MechanicMode.Ignore);
        Assert.Equal(MechanicMode.Ignore, breach.Mode);
        Assert.Contains(breach, _viewModel.QuickTray);
        Assert.DoesNotContain(breach, _viewModel.ChaseList);
        Assert.DoesNotContain(breach, _viewModel.BlockList);
    }

    [Fact]
    public void Block_remaining_moves_the_tray_into_Block_and_leaves_Chase_alone()
    {
        var breach = _viewModel.Weights.Single(row => row.Category == "Breach");
        var abyss = _viewModel.Weights.Single(row => row.Category == "Abyss");
        _viewModel.ApplyQuickMode(breach, MechanicMode.Chase);

        var trayCount = _viewModel.QuickTray.Count;
        Assert.True(trayCount > 0);
        Assert.Contains(abyss, _viewModel.QuickTray);

        _viewModel.BlockRemainingQuickGoalsCommand.Execute(null);

        Assert.Empty(_viewModel.QuickTray);
        Assert.Equal(MechanicMode.Chase, breach.Mode);
        Assert.Equal(MechanicMode.Block, abyss.Mode);
        Assert.Contains(abyss, _viewModel.QuickBlock);
        Assert.Contains(breach, _viewModel.QuickChase);
        Assert.Contains($"Blocked {trayCount}", _viewModel.Status);
    }

    [Fact]
    public void Preset_toggle_cycles_ignore_chase_block()
    {
        var breach = _viewModel.Weights.Single(row => row.Category == "Breach");

        _viewModel.TogglePresetCommand.Execute(breach);
        Assert.Equal(MechanicMode.Chase, breach.Mode);

        _viewModel.TogglePresetCommand.Execute(breach);
        Assert.Equal(MechanicMode.Block, breach.Mode);

        _viewModel.TogglePresetCommand.Execute(breach);
        Assert.Equal(MechanicMode.Ignore, breach.Mode);
    }

    [Fact]
    public void Named_save_and_load_round_trips_goals_by_plan_name()
    {
        _viewModel.PlanName = "Breach farm";
        var breach = _viewModel.Weights.Single(row => row.Category == "Breach");
        _viewModel.ApplyQuickMode(breach, MechanicMode.Chase);
        _viewModel.Budget = 100;

        _viewModel.SaveNamedPlanCommand.Execute(null);
        Assert.Contains("Breach farm", _viewModel.SavedPlanNames);

        _viewModel.ResetGoalsCommand.Execute(null);
        Assert.Equal(MechanicMode.Ignore, breach.Mode);

        _viewModel.SelectedSavedPlan = "Breach farm";
        _viewModel.LoadNamedPlanCommand.Execute(null);

        Assert.Equal("Breach farm", _viewModel.PlanName);
        Assert.Equal(100, _viewModel.Budget);
        Assert.Equal(MechanicMode.Chase, breach.Mode);
        Assert.Equal(ChasePriority.High, breach.Priority);
    }

    /// <summary>Any node exactly <paramref name="distance"/> points away from the start.</summary>
    private static int NodeAtDistance(AtlasTree tree, int distance)
    {
        var distances = tree.Distances(tree.StartNodeId);
        for (var index = 0; index < distances.Length; index++)
        {
            if (distances[index] == distance)
                return tree.IdAt(index);
        }

        throw new InvalidOperationException($"No node sits exactly {distance} points from the start.");
    }

    private void ChaseScarabs(ChasePriority priority = ChasePriority.High) =>
        _viewModel.Weights.Single(row => row.Category == "Scarabs").ChaseWith(priority);
}
