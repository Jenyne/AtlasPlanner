using AtlasPlanner.Gui.Controls;
using AtlasPlanner.Gui.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace AtlasPlanner.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        TreeCanvas.NodeActivated += (_, nodeId) => ViewModel?.OnNodeActivated(nodeId);
        TreeCanvas.NodeRequired += (_, nodeId) => ViewModel?.OnNodeRequired(nodeId);
        TreeCanvas.NodeForbidden += (_, nodeId) => ViewModel?.OnNodeForbidden(nodeId);
        TreeCanvas.NodeRemoved += (_, nodeId) => ViewModel?.OnNodeRemoved(nodeId);
        TreeCanvas.HoveredNodeChanged += (_, node) => ViewModel?.OnHoverChanged(node);
        TreeCanvas.ScaleChanged += (_, scale) => ViewModel?.OnScaleChanged(scale);

        OrderList.SelectionChanged += OnOrderSelectionChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>The request and the tree are remembered here, so the next run opens where this one left off.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModel?.SaveSettings();
        base.OnClosing(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        // The clipboard and the canvas belong to the window, so the view model reaches them
        // through these hooks rather than holding on to view types.
        viewModel.WriteClipboard = text =>
            Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : Task.CompletedTask;

        viewModel.ReadClipboard = async () =>
            Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

        viewModel.RequestFit = () => TreeCanvas.FitToTree();
        viewModel.RequestCentreOn = nodeId => TreeCanvas.CentreOn(nodeId);
    }

    private void OnOrderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (OrderList.SelectedItem is OrderRow row)
            ViewModel?.FocusStepCommand.Execute(row);
    }
}
