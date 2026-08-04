using AtlasPlanner.Core;
using AtlasPlanner.Core.Planning;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Enums;
using ImGuiNET;
using Color = SharpDX.Color;
using RectangleF = SharpDX.RectangleF;
using Vector2 = System.Numerics.Vector2;

namespace AtlasPlannerOverlay;

public partial class AtlasPlannerOverlay
{
    /// <summary>How many upcoming points count as "coming up" and get numbered and brightened.</summary>
    private int LookAhead => Settings.PointsPerApply.Value;

    /// <summary>
    /// Writes text that wraps, without treating it as a format string.
    /// </summary>
    /// <remarks>
    /// ImGui's Text and TextWrapped take a printf format, so a stat line like "+40% chance to contain
    /// Niko" gets read as a conversion specifier and comes out as "+40?hance". Atlas stats are full of
    /// percentages, so everything drawn from plan data has to go through TextUnformatted.
    /// </remarks>
    private static void WrappedText(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private static void DimText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Maps a node from the tree export onto the screen. The atlas canvas reports its own centre and
    /// zoom, so this stays correct while the tree is panned and zoomed.
    /// </summary>
    private static Vector2 ScreenOf(Vector2 treePosition, Vector2 canvasCentre, float scale) =>
        new(canvasCentre.X + treePosition.X * scale, canvasCentre.Y + treePosition.Y * scale);

    private void DrawPlan(TreePanel panel, PlanProgress progress)
    {
        if (_tree is null)
            return;

        var canvas = panel.CanvasElement;
        if (canvas is null || !canvas.IsValid)
            return;

        var centre = new Vector2(canvas.Center.X, canvas.Center.Y);
        var scale = canvas.Scale;
        if (scale <= 0f)
            return;

        var upcoming = progress.Next(LookAhead).Select(step => step.NodeId).ToHashSet();

        if (Settings.ShowRoute)
            DrawRoute(progress, centre, scale, upcoming);

        foreach (var step in progress.Plan.Order)
        {
            if (!_tree.TryGet(step.NodeId, out var node) || node is null)
                continue;

            var screen = ScreenOf(node.Position, centre, scale);
            var done = progress.IsDone(step.NodeId);
            var isNext = progress.NextStep?.NodeId == step.NodeId;

            var colour = done
                ? Settings.DoneColor.Value
                : isNext
                    ? Settings.NextColor.Value
                    : upcoming.Contains(step.NodeId)
                        ? Settings.UpcomingColor.Value
                        : Settings.PlannedColor.Value;

            var size = Settings.RingSize.Value * scale;
            if (isNext)
                size *= 1.35f;

            if (_ringImage is not null)
                Graphics.DrawImage(_ringImage, new RectangleF(screen.X - size / 2f, screen.Y - size / 2f, size, size), colour);

            DrawStepLabel(step, screen, colour, done, isNext, upcoming);
        }

        DrawOffPlan(progress, centre, scale);
    }

    private void DrawStepLabel(PlanStep step, Vector2 screen, Color colour, bool done, bool isNext, IReadOnlySet<int> upcoming)
    {
        if (done)
            return;

        var number = Settings.NumberEveryStep || isNext || upcoming.Contains(step.NodeId);
        if (!number)
            return;

        Graphics.DrawText(step.Step.ToString(), screen, colour, FontAlign.Center);

        // The point you are about to take is worth naming, since it is the one you are looking for.
        if (isNext && step.Name.Length > 0)
            Graphics.DrawText(step.Name, screen with { Y = screen.Y + 16 }, colour, FontAlign.Center);
    }

    /// <summary>Links planned nodes that are adjacent in the tree, so the route reads as a path.</summary>
    private void DrawRoute(PlanProgress progress, Vector2 centre, float scale, IReadOnlySet<int> upcoming)
    {
        if (_tree is null)
            return;

        var planned = progress.Plan.Nodes.ToHashSet();

        foreach (var id in planned)
        {
            if (!_tree.TryGet(id, out var node) || node is null)
                continue;

            var from = ScreenOf(node.Position, centre, scale);

            foreach (var neighbourId in node.Neighbours)
            {
                // Each link once, and never the paired gateways, whose line would cross the whole atlas.
                if (neighbourId <= id || !planned.Contains(neighbourId))
                    continue;
                if (!_tree.TryGet(neighbourId, out var neighbour) || neighbour is null)
                    continue;
                if (node.IsGateway && neighbour.IsGateway)
                    continue;

                var bothDone = progress.IsDone(id) && progress.IsDone(neighbourId);
                var eitherUpcoming = upcoming.Contains(id) || upcoming.Contains(neighbourId);

                var colour = bothDone
                    ? Settings.DoneColor.Value
                    : eitherUpcoming
                        ? Settings.UpcomingColor.Value
                        : Settings.PlannedColor.Value;

                Graphics.DrawLine(from, ScreenOf(neighbour.Position, centre, scale), 2f, colour);
            }
        }
    }

    private void DrawOffPlan(PlanProgress progress, Vector2 centre, float scale)
    {
        if (_tree is null || progress.OffPlan.Count == 0)
            return;

        var size = Settings.RingSize.Value * scale;

        foreach (var id in progress.OffPlan)
        {
            if (!_tree.TryGet(id, out var node) || node is null || !node.IsAllocatable)
                continue;

            var screen = ScreenOf(node.Position, centre, scale);
            if (_ringImage is not null)
                Graphics.DrawImage(
                    _ringImage,
                    new RectangleF(screen.X - size / 2f, screen.Y - size / 2f, size, size),
                    Settings.OffPlanColor.Value);
        }
    }

    private void DrawPanel(TreePanel panel, PlanProgress? progress)
    {
        ImGui.SetNextWindowSize(new Vector2(360, 460), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin($"{AppVersion.DisplayName}##atlasPlannerOverlay"))
        {
            ImGui.End();
            return;
        }

        DrawPlanPicker();
        ImGui.Separator();

        if (progress is null)
        {
            WrappedText(_loadStatus.Length > 0 ? _loadStatus : "No plan loaded.");
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted(progress.Plan.Name.Length > 0 ? progress.Plan.Name : "(unnamed plan)");
        ImGui.TextUnformatted(progress.Summary);
        if (progress.TotalSteps > 0)
            ImGui.ProgressBar((float)progress.PointsPlaced / progress.TotalSteps, new Vector2(-1, 0));

        DrawDriftWarnings(progress);
        DrawPlacingControls(panel, progress);
        DrawNextSteps(progress);

        if (Settings.ShowTally)
            DrawTally(progress.Plan);

        if (Settings.ShowDiagnostics)
            DrawDiagnostics(panel, progress);

        ImGui.End();
    }

    /// <summary>
    /// Reports what the plugin can see of the game for the point it is about to take. This is the fastest
    /// way to tell a plan that does not match the tree from a node the game will not let go of.
    /// </summary>
    private void DrawDiagnostics(TreePanel panel, PlanProgress progress)
    {
        if (!ImGui.CollapsingHeader("Diagnostics"))
            return;

        var passives = 0;
        foreach (var _ in panel.Passives)
            passives++;

        DimText($"Nodes the game is offering: {passives}");
        DimText($"Confirmed allocated: {AllocatedIds().Count}");
        DimText($"Taken (confirmed + pending Apply): {TakenIds(panel).Count}");
        DimText($"Tree data loaded: {(_tree is null ? "no" : "yes")}");

        var canvas = panel.CanvasElement;
        DimText(canvas is null || !canvas.IsValid
            ? "Canvas: not readable, so nothing can be drawn in place"
            : $"Canvas centre {canvas.Center.X:F0},{canvas.Center.Y:F0} at zoom {canvas.Scale:F2}");

        var step = progress.NextStep;
        if (step is null)
        {
            DimText("Nothing left to place.");
            return;
        }

        ImGui.Separator();
        DimText($"Next: step {step.Step}, node {step.NodeId}, {step.Describe()}");

        var passive = FindPassive(panel, step.NodeId);
        if (passive is null)
        {
            DimText("The game is not offering that node at all, so it cannot be clicked.");
            return;
        }

        var rect = ((Element)passive).GetClientRectCache;
        DimText($"Sits at {rect.Center.X:F0},{rect.Center.Y:F0} in the window");
        DimText($"On screen: {(IsOnScreen(passive) ? "yes" : "no, pan the tree to it")}");
        DimText($"Game will allow it: {(passive.CanAllocate ? "yes" : "no")}");
        DimText($"Pending Apply: {(passive.IsAllocatedForPlan ? "yes" : "no")}");
        DimText($"Cursor is on it right now: {(IsHovered(passive) ? "yes" : "no")}");
    }

    private void DrawPlanPicker()
    {
        var label = _planPath is null ? "(none)" : Path.GetFileNameWithoutExtension(_planPath);

        if (ImGui.BeginCombo("Plan", label))
        {
            if (_planFiles.Count == 0)
                DimText("Nothing in the Plans folder yet.");

            foreach (var file in _planFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (ImGui.Selectable(name, file == _planPath))
                    LoadPlan(file);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reload"))
        {
            RefreshPlanFiles(force: true);
            if (_planPath is not null && File.Exists(_planPath))
                LoadPlan(_planPath);
            else
                LoadNewestPlan();
        }

        if (_loadStatus.Length > 0)
            WrappedText(_loadStatus);
    }

    private void DrawDriftWarnings(PlanProgress progress)
    {
        if (progress.OffPlan.Count > 0)
            WrappedText($"{progress.OffPlan.Count} point(s) are spent outside this plan. " +
                              "They are ringed in the off-plan colour; refund them yourself if you want them back.");

        if (progress.MissingGroundwork.Count > 0)
            WrappedText($"This plan was built on top of {progress.MissingGroundwork.Count} node(s) " +
                              "you no longer have, so placing points may stall. Re-solve it from your current tree.");
    }

    private void DrawPlacingControls(TreePanel panel, PlanProgress progress)
    {
        ImGui.Separator();
        DimText("Selects points on the tree; you press Apply at the top to commit.");

        var remaining = progress.Remaining.Count;
        var perPress = Math.Clamp(Settings.PointsPerApply.Value, 1, 200);
        if (perPress != Settings.PointsPerApply.Value)
            Settings.PointsPerApply.Value = perPress;

        if (_isPlacing)
        {
            if (ImGui.Button("Stop"))
            {
                _stopRequested = true;
                _applyStatus = "Stopping.";
            }
        }
        else
        {
            ImGui.BeginDisabled(progress.IsComplete);
            if (ImGui.Button($"Place next {Math.Min(perPress, Math.Max(remaining, 1))}"))
                StartPlacing(panel, progress);
            ImGui.SameLine();
            if (ImGui.Button($"Place all ({remaining})"))
                StartPlacing(panel, progress, remaining);
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.SliderInt("##perPress", ref perPress, 1, 200))
            Settings.PointsPerApply.Value = perPress;

        if (_applyStatus.Length > 0)
            WrappedText(_applyStatus);
    }

    private static void DrawNextSteps(PlanProgress progress)
    {
        ImGui.Separator();
        if (progress.IsComplete)
        {
            ImGui.TextUnformatted("Nothing left to place.");
            return;
        }

        ImGui.TextUnformatted("Coming up");
        foreach (var step in progress.Next(12))
        {
            var name = step.Name.Length > 0 ? step.Name : $"#{step.NodeId}";
            ImGui.TextUnformatted($"{step.Step,4}  {name}");

            if (step.Towards is { Length: > 0 })
            {
                ImGui.SameLine();
                DimText($"towards {step.Towards}");
            }
        }
    }

    private static void DrawTally(AtlasPlan plan)
    {
        if (!ImGui.CollapsingHeader("What the finished plan gives"))
            return;

        foreach (var entry in plan.Tally.Summed)
            WrappedText(entry.Text);

        foreach (var entry in plan.Tally.Repeated)
            WrappedText(entry.Text);

        foreach (var entry in plan.Tally.Flags)
            WrappedText(entry.Text);
    }
}
