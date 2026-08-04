using AtlasPlanner.Core.Planning;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared;
using Extensions = ExileCore.Shared.Helpers.Extensions;
using Vector2 = System.Numerics.Vector2;

namespace AtlasPlannerOverlay;

public partial class AtlasPlannerOverlay
{
    /// <summary>How long to hold the button down. Too short and the game treats it as no click at all.</summary>
    private const int MouseDownMs = 30;

    /// <summary>
    /// How long to wait for the game to report the node as hovered before clicking anyway. Waiting is
    /// worth a moment because it means the click lands on a node the game has registered, but insisting
    /// on it is not: elements that never report as hovered would block forever.
    /// </summary>
    private const int HoverConfirmTimeoutMs = 200;

    private const int SelectionTimeoutMs = 800;
    private const int AttemptsPerPoint = 3;

    private void StartPlacing(TreePanel panel, PlanProgress? progress, int? countOverride = null)
    {
        if (_isPlacing)
            return;

        if (progress is null)
        {
            _applyStatus = "Load a plan first.";
            return;
        }

        if (progress.IsComplete)
        {
            _applyStatus = "The plan is already fully placed.";
            return;
        }

        var count = countOverride ?? Settings.PointsPerApply.Value;
        if (count < 1)
            count = 1;

        _stopRequested = false;
        _isPlacing = true;
        _applyStatus = "Starting.";
        _applyTask = PlacePoints(panel, count);
    }

    /// <summary>
    /// Selects the next points in plan order on the open tree. The game only commits them when you
    /// press Apply at the top — that step is left to you.
    /// </summary>
    /// <remarks>
    /// Progress uses <see cref="TreePassiveElement.IsAllocatedForPlan"/> (pending selection) plus
    /// server-confirmed ids. Waiting for Apply before the next click is what used to stick on step 1.
    /// </remarks>
    private async SyncTask<bool> PlacePoints(TreePanel panel, int count)
    {
        try
        {
            var plan = _plan;
            if (plan is null)
                return true;

            var placed = 0;

            while (placed < count)
            {
                if (_stopRequested)
                {
                    _applyStatus = $"Stopped after {placed} point(s). Press Apply on the tree when you want them committed.";
                    return true;
                }

                if (!((Element)panel).IsVisible)
                {
                    _applyStatus = $"The atlas tree closed after {placed} point(s).";
                    return true;
                }

                var step = PlanProgress.For(plan, TakenIds(panel)).NextStep;
                if (step is null)
                {
                    _applyStatus = placed == 0
                        ? "Plan finished — press Apply at the top of the tree if you still have pending points."
                        : $"Selected {placed} point(s). Press Apply at the top of the tree to commit them.";
                    return true;
                }

                var problem = await PlaceOne(panel, step);
                if (problem is not null)
                {
                    _applyStatus = $"Stopped after {placed} point(s). {problem}";
                    return true;
                }

                placed++;
                _applyStatus = $"Selected {placed} of {count}. Apply is yours when ready.";
                await Task.Delay(Settings.ClickDelayMs.Value);
            }

            _applyStatus = $"Selected {placed} point(s). Press Apply at the top of the tree to commit them.";
            return true;
        }
        finally
        {
            _isPlacing = false;
        }
    }

    /// <summary>Takes one step, returning null on success or a description of what stopped it.</summary>
    private async SyncTask<string?> PlaceOne(TreePanel panel, PlanStep step)
    {
        var passive = FindPassive(panel, step.NodeId);
        if (passive is null)
            return $"The game is not showing node {step.NodeId} ({step.Describe()}).";

        // Already selected pending Apply — treat as done so we do not re-click it.
        if (passive.IsAllocatedForPlan)
            return null;

        if (!passive.CanAllocate)
            return $"The game will not let step {step.Step}, {step.Describe()}, be allocated. " +
                   "You are most likely out of atlas points.";

        if (!IsOnScreen(passive))
            return $"Step {step.Step}, {step.Describe()}, is off screen. " +
                   "Pan the tree so it is visible, then start again.";

        for (var attempt = 1; attempt <= AttemptsPerPoint; attempt++)
        {
            if (_stopRequested)
                return "Stopped.";

            await ClickNode(passive);

            if (await WaitForSelection(panel, step.NodeId))
                return null;
        }

        return $"Clicked step {step.Step}, {step.Describe()}, {AttemptsPerPoint} times and the game did not " +
               "select it. Something may be covering that spot on screen, such as this panel.";
    }

    /// <summary>
    /// Moves the cursor onto a node and clicks it.
    /// </summary>
    /// <remarks>
    /// <c>SetCursorPos</c> on its own repositions the pointer without the game noticing, so the click
    /// lands on whatever the game still thinks is under the cursor. <c>MouseMove</c> is what makes the
    /// game register the new position, and pressing and releasing separately is what the panels here
    /// respond to reliably.
    /// </remarks>
    private async SyncTask<bool> ClickNode(TreePassiveElement passive)
    {
        var windowTopLeft = Extensions.ToVector2Num(GameController.Window.GetWindowRectangleTimeCache.TopLeft);
        var centre = Extensions.ToVector2Num(((Element)passive).GetClientRectCache.Center);

        Input.SetCursorPos(windowTopLeft + centre);
        Input.MouseMove();
        await Task.Delay(Settings.HoverDelayMs.Value);

        for (var waited = 0; waited < HoverConfirmTimeoutMs && !IsHovered(passive); waited += 20)
            await Task.Delay(20);

        Input.LeftDown();
        await Task.Delay(MouseDownMs);
        Input.LeftUp();

        return true;
    }

    /// <summary>
    /// Waits until the node is selected for the pending plan (before Apply) or already committed.
    /// </summary>
    private async SyncTask<bool> WaitForSelection(TreePanel panel, int nodeId)
    {
        for (var waited = 0; waited < SelectionTimeoutMs; waited += 30)
        {
            if (IsTaken(panel, nodeId))
                return true;

            await Task.Delay(30);
        }

        return IsTaken(panel, nodeId);
    }

    private bool IsTaken(TreePanel panel, int nodeId)
    {
        if (AllocatedIds().Contains(nodeId))
            return true;

        var passive = FindPassive(panel, nodeId);
        return passive?.IsAllocatedForPlan == true;
    }

    private bool IsHovered(TreePassiveElement passive)
    {
        var hovered = GameController?.IngameState?.UIHover;
        return hovered is not null &&
               ((RemoteMemoryObject)hovered).Address == ((RemoteMemoryObject)passive).Address;
    }

    /// <summary>
    /// Whether a node is somewhere a click can actually reach. Element rects are relative to the game
    /// window, and a node scrolled off the tree reports a position outside it.
    /// </summary>
    private bool IsOnScreen(TreePassiveElement passive)
    {
        var window = GameController.Window.GetWindowRectangleTimeCache;
        var centre = ((Element)passive).GetClientRectCache.Center;

        return centre.X > 4 && centre.Y > 4 &&
               centre.X < window.Width - 4 && centre.Y < window.Height - 4;
    }

    private static TreePassiveElement? FindPassive(TreePanel panel, int nodeId)
    {
        foreach (var passive in panel.Passives)
        {
            if (passive?.PassiveSkill is { } skill && skill.PassiveId == nodeId)
                return passive;
        }

        return null;
    }
}
