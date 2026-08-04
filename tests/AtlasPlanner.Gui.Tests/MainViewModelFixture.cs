using AtlasPlanner.Core.Art;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Gui.Services;
using AtlasPlanner.Gui.ViewModels;

namespace AtlasPlanner.Gui.Tests;

/// <summary>
/// Loads the view model once for the whole suite. Art fetching is off so nothing touches the
/// network, and no window is created, so these tests cover the wiring rather than the rendering.
/// </summary>
public sealed class MainViewModelFixture : IAsyncLifetime
{
    private Dictionary<string, decimal> _seededWeights = [];

    public MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>A settings file of its own, so the suite neither reads nor overwrites the real one.</summary>
    public string SettingsPath { get; } = Path.Combine(
        Path.GetTempPath(),
        $"atlasplanner-tests-{Guid.NewGuid():N}",
        "settings.json");

    /// <summary>A drop folder of its own, so exporting never writes into a real install.</summary>
    public string ExportFolder => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "plans");

    /// <summary>Named save/load library folder for this suite only.</summary>
    public string LibraryFolder => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "library");

    public PlannerSession Session => ViewModel.Session
                                     ?? throw new InvalidOperationException("Initialisation produced no session.");

    public async Task InitializeAsync()
    {
        var cache = new SpriteCache(Path.Combine(Path.GetTempPath(), "atlasplanner-tests-no-art"));
        ViewModel = new MainViewModel(cache, fetchArt: false, settingsPath: SettingsPath)
        {
            ExportFolder = ExportFolder,
            LibraryFolder = LibraryFolder,
        };
        await ViewModel.InitialiseAsync();

        Assert.False(ViewModel.IsLoading, $"initialisation failed: {ViewModel.Status}");
        _seededWeights = ViewModel.Weights.ToDictionary(row => row.Category, row => row.Weight);
    }

    /// <summary>
    /// Puts the shared view model back the way initialisation left it, so tests cannot leak state
    /// into each other regardless of the order xUnit picks.
    /// </summary>
    public void Reset()
    {
        ViewModel.ClearRouteCommand.Execute(null);
        ViewModel.ClearSearchCommand.Execute(null);
        ViewModel.OnHoverChanged(null);

        foreach (var row in ViewModel.Weights)
        {
            row.ApplyLegacy((double)_seededWeights.GetValueOrDefault(row.Category), isOn: true);
        }

        ViewModel.RequireText = string.Empty;
        ViewModel.ForbidText = string.Empty;
        ViewModel.MechanicFilter = string.Empty;
        ViewModel.ShowUrlTools = false;
        ViewModel.ForbidText = string.Empty;
        ViewModel.UnwaveringVision = PointGrantChoice.Auto;
        ViewModel.KeepCurrentAllocation = false;
        ViewModel.TimeLimitMs = 2000;
        ViewModel.ExclusionWeight = (decimal)new SolveProfile().ExclusionWeight;
        ViewModel.ExportFolder = ExportFolder;
        ViewModel.LibraryFolder = LibraryFolder;

        if (Directory.Exists(ExportFolder))
            Directory.Delete(ExportFolder, recursive: true);

        if (Directory.Exists(LibraryFolder))
            Directory.Delete(LibraryFolder, recursive: true);

        ViewModel.SavedPlanNames.Clear();
        ViewModel.SelectedSavedPlan = null;
    }

    public Task DisposeAsync()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class ViewModelCollection : ICollectionFixture<MainViewModelFixture>
{
    public const string Name = "view model";
}
