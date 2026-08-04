using AtlasPlanner.Gui.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AtlasPlanner.Gui.Controls;

public partial class QuickGoalsBoard : UserControl
{
    private static readonly DataFormat<string> CategoryFormat =
        DataFormat.CreateStringApplicationFormat("atlas-planner-quick-goal");

    private const double DragStartDistance = 6;

    private Point? _pressOrigin;
    private WeightRow? _pressRow;
    private PointerPressedEventArgs? _pressArgs;
    private bool _dragStarted;

    public QuickGoalsBoard()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, OnBoardPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnBoardPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnBoardPointerReleased, RoutingStrategies.Tunnel);

        foreach (var column in new[] { TrayDrop, BlockDrop, ChaseDrop })
        {
            DragDrop.AddDragOverHandler(column, OnDragOver);
            DragDrop.AddDropHandler(column, OnDrop);
        }
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnBoardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (FindPill(e.Source as Control) is not { Tag: WeightRow row })
            return;

        _pressOrigin = e.GetPosition(this);
        _pressRow = row;
        _pressArgs = e;
        _dragStarted = false;
    }

    private async void OnBoardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressOrigin is not { } origin || _pressRow is null || _pressArgs is null || _dragStarted)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var delta = e.GetPosition(this) - origin;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < DragStartDistance)
            return;

        _dragStarted = true;
        var row = _pressRow;
        var press = _pressArgs;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(CategoryFormat, row.Category));

        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);

        _pressOrigin = null;
        _pressRow = null;
        _pressArgs = null;
        _dragStarted = false;
    }

    private void OnBoardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressOrigin = null;
        _pressRow = null;
        _pressArgs = null;
        _dragStarted = false;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(CategoryFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is null || sender is not Control column)
            return;

        var category = e.DataTransfer.TryGetValue(CategoryFormat);
        if (category is null)
            return;

        var row = ViewModel.Weights.FirstOrDefault(r =>
            string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            return;

        var mode = column.Name switch
        {
            nameof(BlockDrop) => MechanicMode.Block,
            nameof(ChaseDrop) => MechanicMode.Chase,
            _ => MechanicMode.Ignore,
        };

        ViewModel.ApplyQuickMode(row, mode);
        e.Handled = true;
    }

    private static Border? FindPill(Control? source)
    {
        for (var current = source; current is not null; current = current.GetVisualParent() as Control)
        {
            if (current is Border { Tag: WeightRow } pill)
                return pill;
        }

        return null;
    }
}
