using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using Color = SharpDX.Color;

namespace AtlasPlannerOverlay;

public class AtlasPlannerOverlaySettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(true);

    [Menu("Draw the plan on the atlas tree")]
    public ToggleNode ShowHighlights { get; set; } = new ToggleNode(true);

    [Menu("Draw the route between planned nodes")]
    public ToggleNode ShowRoute { get; set; } = new ToggleNode(true);

    [Menu("Number every step", "Off numbers only the points you are about to take, which reads better on a long plan.")]
    public ToggleNode NumberEveryStep { get; set; } = new ToggleNode(false);

    [Menu("Show the plan panel")]
    public ToggleNode ShowPanel { get; set; } = new ToggleNode(true);

    [Menu("Show the tally in the panel")]
    public ToggleNode ShowTally { get; set; } = new ToggleNode(true);

    [Menu("Ring size")]
    public RangeNode<int> RingSize { get; set; } = new RangeNode<int>(90, 30, 200);

    [Menu("Placing points", 100)]
    public EmptyNode PlacingHeader { get; set; } = new EmptyNode();

    [Menu("Points to place per press", "How many nodes to select each run. Raise this to walk a whole plan before you press Apply yourself.", parentIndex = 100)]
    public RangeNode<int> PointsPerApply { get; set; } = new RangeNode<int>(20, 1, 200);

    [Menu("Place the next points", "Selects the next points in plan order on the open tree. You still press Apply at the top of the tree to commit them.", parentIndex = 100)]
    public HotkeyNodeV2 ApplyHotkey { get; set; } = new HotkeyNodeV2(Keys.None);

    [Menu("Stop placing", "Cancels a run that is in progress.", parentIndex = 100)]
    public HotkeyNodeV2 StopHotkey { get; set; } = new HotkeyNodeV2(Keys.Escape);

    [Menu("Settle time after moving the cursor (ms)", "Raise this if points get skipped.", parentIndex = 100)]
    public RangeNode<int> HoverDelayMs { get; set; } = new RangeNode<int>(60, 10, 500);

    [Menu("Pause between points (ms)", "Lower is faster but more likely to outrun the game's own updates.", parentIndex = 100)]
    public RangeNode<int> ClickDelayMs { get; set; } = new RangeNode<int>(120, 20, 1000);

    [Menu("Show why placing stopped", "Adds a diagnostics section to the panel.", parentIndex = 100)]
    public ToggleNode ShowDiagnostics { get; set; } = new ToggleNode(false);

    [Menu("Colours", 200)]
    public EmptyNode ColoursHeader { get; set; } = new EmptyNode();

    [Menu("Next point to take", parentIndex = 200)]
    public ColorNode NextColor { get; set; } = new ColorNode(Color.Lime);

    [Menu("Coming up", parentIndex = 200)]
    public ColorNode UpcomingColor { get; set; } = new ColorNode(Color.Gold);

    [Menu("Planned but further off", parentIndex = 200)]
    public ColorNode PlannedColor { get; set; } = new ColorNode(new Color(90, 150, 255, 190));

    [Menu("Already placed", parentIndex = 200)]
    public ColorNode DoneColor { get; set; } = new ColorNode(new Color(120, 120, 120, 110));

    [Menu("Spent outside the plan", parentIndex = 200)]
    public ColorNode OffPlanColor { get; set; } = new ColorNode(Color.OrangeRed);
}
