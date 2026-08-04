using System.Collections.ObjectModel;
using System.Globalization;
using AtlasPlanner.Core;
using AtlasPlanner.Core.Art;
using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Io;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;
using AtlasPlanner.Gui.Rendering;
using AtlasPlanner.Gui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtlasPlanner.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly char[] ListSeparators = [',', '\n', '\r', ';'];

    private readonly SpriteCache _spriteCache;
    private readonly bool _fetchArt;
    private readonly string? _settingsPath;
    private int[] _distancesFromStart = [];

    /// <param name="spriteCache">Where downloaded art lives. Defaults to the per-user cache.</param>
    /// <param name="fetchArt">
    /// When false, missing art is left missing instead of being downloaded. The tree still renders,
    /// just without sprites, which is what you want offline.
    /// </param>
    /// <param name="settingsPath">Where the last session is remembered. Defaults to the per-user file.</param>
    public MainViewModel(SpriteCache? spriteCache = null, bool fetchArt = true, string? settingsPath = null)
    {
        _spriteCache = spriteCache ?? new SpriteCache();
        _fetchArt = fetchArt;
        _settingsPath = settingsPath;
    }

    [ObservableProperty] private PlannerSession? _session;
    [ObservableProperty] private SpriteImages? _images;

    [ObservableProperty] private string _status = "Starting up.";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isSolving;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isDownloading;

    [ObservableProperty] private string _pointsLabel = string.Empty;
    [ObservableProperty] private string _treeLabel = string.Empty;
    [ObservableProperty] private string _zoomLabel = string.Empty;

    [ObservableProperty] private NodeDetail? _hoveredNode;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlySet<int>? _highlighted;
    [ObservableProperty] private string _searchSummary = string.Empty;

    [ObservableProperty] private IReadOnlyDictionary<int, int>? _planSteps;
    [ObservableProperty] private IReadOnlySet<int>? _requiredNodes;
    [ObservableProperty] private IReadOnlySet<int>? _forbiddenNodes;
    [ObservableProperty] private string _solveSummary = string.Empty;
    [ObservableProperty] private string _solveNotes = string.Empty;

    [ObservableProperty] private bool _showBackground = true;
    [ObservableProperty] private bool _showStepNumbers = true;
    [ObservableProperty] private bool _showUrlTools;

    // Solve profile, edited directly in the panel.
    [ObservableProperty] private string _planName = "Unnamed plan";
    [ObservableProperty] private int _budget = 138;
    [ObservableProperty] private PointGrantChoice _unwaveringVision = PointGrantChoice.Auto;
    [ObservableProperty] private string _requireText = string.Empty;
    [ObservableProperty] private string _forbidText = string.Empty;
    [ObservableProperty] private int _timeLimitMs = 2000;
    [ObservableProperty] private bool _keepCurrentAllocation;
    [ObservableProperty] private decimal _exclusionWeight = (decimal)new SolveProfile().ExclusionWeight;

    /// <summary>Filter text for finding a mechanic to chase or block.</summary>
    [ObservableProperty] private string _mechanicFilter = string.Empty;

    public ObservableCollection<WeightRow> Weights { get; } = [];
    public ObservableCollection<WeightRow> ChaseList { get; } = [];
    public ObservableCollection<WeightRow> BlockList { get; } = [];
    public ObservableCollection<WeightRow> MechanicSuggestions { get; } = [];
    public ObservableCollection<MarkChip> RequiredChips { get; } = [];
    public ObservableCollection<MarkChip> ForbiddenChips { get; } = [];
    public ObservableCollection<TallyRow> TallySummed { get; } = [];
    public ObservableCollection<TallyRow> TallyRepeated { get; } = [];
    public ObservableCollection<TallyRow> TallyFlags { get; } = [];
    public ObservableCollection<OrderRow> Order { get; } = [];

    public IReadOnlyList<PointGrantChoice> UnwaveringOptions { get; } = Enum.GetValues<PointGrantChoice>();
    public IReadOnlyList<ChasePriority> PriorityOptions { get; } = Enum.GetValues<ChasePriority>();
    public IReadOnlyList<MechanicMode> MechanicModes { get; } = Enum.GetValues<MechanicMode>();

    public string WindowTitle => AppVersion.WindowTitle;

    /// <summary>Set by the window, which is the only thing with access to a clipboard.</summary>
    public Func<string, Task>? WriteClipboard { get; set; }

    public Func<Task<string?>>? ReadClipboard { get; set; }

    /// <summary>Set by the window so commands can reframe the canvas.</summary>
    public Action? RequestFit { get; set; }

    public Action<int>? RequestCentreOn { get; set; }

    public AtlasPlan? LastPlan { get; private set; }

    /// <summary>
    /// Where Export plan writes. Left null in normal use so the ExileAPI install is found automatically;
    /// set it to send plans somewhere else.
    /// </summary>
    public string? ExportFolder { get; set; }

    /// <summary>Where named save/load plans live. Defaults to the per-user Library folder.</summary>
    public string? LibraryFolder { get; set; }

    public string? LastExportPath { get; private set; }

    public ObservableCollection<string> SavedPlanNames { get; } = [];

    [ObservableProperty] private string? _selectedSavedPlan;

    /// <summary>False in a public build so Send to game is not offered.</summary>
    public bool ShowSendToGame =>
#if ATLASPLANNER_PUBLIC
        false;
#else
        true;
#endif

    public ObservableCollection<WeightRow> QuickTray { get; } = [];

    public ObservableCollection<WeightRow> QuickBlock { get; } = [];

    public ObservableCollection<WeightRow> QuickChase { get; } = [];

    public async Task InitialiseAsync()
    {
        try
        {
            Status = "Loading tree data.";
            var treePath = await Task.Run(() => DataLocator.ResolveTree());
            var scoresPath = await Task.Run(() => DataLocator.ResolveScores());

            var session = await Task.Run(() =>
            {
                var tree = AtlasTree.Load(treePath);
                var scores = ScoreTable.LoadOrDefault(scoresPath);
                scores.PrepareFor(tree);
                return new PlannerSession(tree, scores, treePath, scoresPath ?? "(built-in defaults)");
            });

            await EnsureArtAsync(session.Tree.Sprites);

            Images = new SpriteImages(_spriteCache);
            Budget = session.Tree.TotalPoints;
            _distancesFromStart = session.Tree.Distances(session.Tree.StartNodeId);

            session.AllocationChanged += OnAllocationChanged;
            Session = session;

            BuildWeightRows(session);
            RefreshSavedPlanNames();
            TreeLabel = $"{session.Tree.TreeName} · {session.Tree.NodeCount} nodes · {session.Tree.Edges.Count} connections";

            var restored = ApplySettings(session, PlannerSettings.Load(_settingsPath));
            OnAllocationChanged();

            IsLoading = false;
            Status = restored
                ? "Picked up where you left off. Ctrl+click Must take, Shift+click Never take."
                : "Ctrl+click Must take · Shift+click Never take · Left click to path · Right click to remove.";
        }
        catch (Exception ex)
        {
            IsLoading = false;
            Status = $"Could not start: {ex.Message}";
        }
    }

    /// <summary>Downloads any sprite sheets that are not cached yet, reporting progress as it goes.</summary>
    private async Task EnsureArtAsync(SpriteCatalog catalog)
    {
        var missing = _spriteCache.Missing(catalog);
        if (missing.Count == 0 || !_fetchArt)
            return;

        IsDownloading = true;
        Status = $"Downloading {missing.Count} sprite sheets from the official CDN (one time only).";

        var progress = new Progress<SpriteDownloadProgress>(report =>
        {
            DownloadProgress = report.Total == 0 ? 100 : report.Completed * 100d / report.Total;
            Status = $"Downloading tree art: {report.Completed}/{report.Total}";
        });

        var result = await _spriteCache.EnsureAsync(catalog, progress);
        IsDownloading = false;

        if (!result.AllSucceeded)
            Status = $"{result.Failed.Count} sprite sheet(s) could not be downloaded; some art will be missing.";
    }

    private void BuildWeightRows(PlannerSession session)
    {
        Weights.Clear();

        foreach (var category in session.Scores.KnownCategories(session.Tree))
        {
            var row = new WeightRow(category);
            row.PropertyChanged += OnMechanicRowChanged;
            Weights.Add(row);
        }

        RefreshGoalLists();
        RefreshMechanicSuggestions();
        RefreshQuickColumns();
        OnPropertyChanged(nameof(ActiveWeightSummary));
    }

    private void OnMechanicRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WeightRow.Mode)
            or nameof(WeightRow.Priority)
            or nameof(WeightRow.Weight)
            or nameof(WeightRow.IsOn)
            or null)
        {
            RefreshGoalLists();
            RefreshMechanicSuggestions();
            RefreshQuickColumns();
            OnPropertyChanged(nameof(ActiveWeightSummary));
        }
    }

    /// <summary>
    /// Restores the last session. Anything the saved file does not know about keeps its default, so
    /// an older settings file is always usable. Returns whether anything was actually restored.
    /// </summary>
    private bool ApplySettings(PlannerSession session, PlannerSettings settings, bool applyEmptyGoals = false)
    {
        PlanName = settings.PlanName;
        Budget = settings.Budget is { } budget ? Math.Clamp(budget, 0, 400) : session.Tree.TotalPoints;
        session.Budget = Budget;
        TimeLimitMs = settings.TimeLimitMs;
        ExclusionWeight = (decimal)settings.ExclusionWeight;
        UnwaveringVision = settings.UnwaveringVision;
        RequireText = settings.Require;
        ForbidText = settings.Forbid;
        KeepCurrentAllocation = settings.KeepCurrentAllocation;
        ShowBackground = settings.ShowBackground;
        ShowStepNumbers = settings.ShowStepNumbers;

        var switchedOff = settings.SwitchedOff.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Named library loads always apply (including empty = clear). Session restore only overwrites
        // when the file had mechanic choices, so a blank first-run settings file stays empty.
        if (settings.Weights.Count > 0 || settings.SwitchedOff.Count > 0 || applyEmptyGoals)
        {
            foreach (var row in Weights)
            {
                var weight = settings.Weights.GetValueOrDefault(row.Category, 0d);
                row.ApplyLegacy(weight, isOn: !switchedOff.Contains(row.Category));
            }
        }

        RefreshGoalLists();
        RefreshMechanicSuggestions();
        RefreshQuickColumns();
        OnPropertyChanged(nameof(ActiveWeightSummary));

        if (settings.Tree.Length == 0)
            return settings.Weights.Count > 0 || settings.SwitchedOff.Count > 0;

        try
        {
            session.LoadUrl(settings.Tree);
        }
        catch (Exception)
        {
            // A tree from a previous league's export is not worth refusing to start over.
        }

        return true;
    }

    /// <summary>Captures the current request and tree so the next run opens where this one stopped.</summary>
    public PlannerSettings CaptureSettings() => new()
    {
        PlanName = PlanName,
        Budget = Budget,
        TimeLimitMs = TimeLimitMs,
        ExclusionWeight = (double)ExclusionWeight,
        UnwaveringVision = UnwaveringVision,
        Require = RequireText,
        Forbid = ForbidText,
        KeepCurrentAllocation = KeepCurrentAllocation,
        ShowBackground = ShowBackground,
        ShowStepNumbers = ShowStepNumbers,
        Weights = Weights
            .Where(row => row.Mode == MechanicMode.Chase)
            .ToDictionary(row => row.Category, row => (double)row.Weight, StringComparer.OrdinalIgnoreCase),
        SwitchedOff = Weights.Where(row => row.Mode == MechanicMode.Block).Select(row => row.Category).ToList(),
        Tree = Session?.ToUrl() ?? string.Empty,
    };

    public bool SaveSettings() => CaptureSettings().TrySave(_settingsPath);

    public string ActiveWeightSummary
    {
        get
        {
            var chasing = Weights
                .Where(row => row.Mode == MechanicMode.Chase)
                .OrderByDescending(row => (int)row.Priority)
                .ThenBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
                .Select(row => $"{row.Category}·{row.Priority}")
                .ToArray();

            var blocking = Weights
                .Where(row => row.Mode == MechanicMode.Block)
                .Select(row => row.Category)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (chasing.Length == 0 && blocking.Length == 0)
                return "Nothing to chase.";

            var parts = new List<string>();
            if (chasing.Length > 0)
                parts.Add("Chasing " + string.Join(", ", chasing));
            if (blocking.Length > 0)
                parts.Add("Blocking " + string.Join(", ", blocking));
            return string.Join(" · ", parts);
        }
    }

    partial void OnMechanicFilterChanged(string value) => RefreshMechanicSuggestions();

    private void RefreshGoalLists()
    {
        Replace(ChaseList, Weights.Where(row => row.Mode == MechanicMode.Chase)
            .OrderByDescending(row => (int)row.Priority)
            .ThenBy(row => row.Category, StringComparer.OrdinalIgnoreCase));

        Replace(BlockList, Weights.Where(row => row.Mode == MechanicMode.Block)
            .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase));
    }

    private void RefreshMechanicSuggestions()
    {
        var query = MechanicFilter.Trim();
        IEnumerable<WeightRow> source = Weights.Where(row => row.Mode == MechanicMode.Ignore);

        if (query.Length > 0)
        {
            source = Weights.Where(row =>
                row.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Replace(
            MechanicSuggestions,
            source
                .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
                .Take(query.Length == 0 ? 0 : 12));
    }

    [RelayCommand]
    private void ChaseMechanic(WeightRow? row)
    {
        if (row is null)
            return;

        row.ChaseWith(ChasePriority.Normal);
        MechanicFilter = string.Empty;
        Status = $"Chasing {row.Category}.";
    }

    [RelayCommand]
    private void BlockMechanic(WeightRow? row)
    {
        if (row is null)
            return;

        row.Block();
        MechanicFilter = string.Empty;
        Status = $"Blocking {row.Category}.";
    }

    [RelayCommand]
    private void ClearMechanic(WeightRow? row)
    {
        if (row is null)
            return;

        row.Clear();
        Status = $"Stopped tracking {row.Category}.";
    }

    /// <summary>
    /// Quick-goals chip / tests: Chase at High, Block, or clear back to Ignore.
    /// </summary>
    public void ApplyQuickMode(WeightRow row, MechanicMode mode)
    {
        ArgumentNullException.ThrowIfNull(row);

        switch (mode)
        {
            case MechanicMode.Chase:
                row.ChaseWith(ChasePriority.High);
                Status = $"Chasing {row.Category} · High.";
                break;
            case MechanicMode.Block:
                row.Block();
                Status = $"Blocking {row.Category}.";
                break;
            default:
                row.Clear();
                Status = $"Cleared {row.Category}.";
                break;
        }
    }

    [RelayCommand]
    private void TogglePreset(WeightRow? row)
    {
        if (row is null)
            return;

        // Click-cycle fallback for tests / callers without the swipe chip.
        var next = row.Mode switch
        {
            MechanicMode.Ignore => MechanicMode.Chase,
            MechanicMode.Chase => MechanicMode.Block,
            _ => MechanicMode.Ignore,
        };
        ApplyQuickMode(row, next);
    }

    /// <summary>Blocks every mechanic still sitting in the Quick goals tray.</summary>
    [RelayCommand]
    private void BlockRemainingQuickGoals()
    {
        var remaining = Weights.Where(row => row.Mode == MechanicMode.Ignore).ToArray();
        if (remaining.Length == 0)
        {
            Status = "Nothing left in the tray to block.";
            return;
        }

        foreach (var row in remaining)
            row.Block();

        Status = $"Blocked {remaining.Length} remaining mechanic{(remaining.Length == 1 ? "" : "s")}.";
    }

    /// <summary>Drops Must take / Never take marks without touching chase, block, or the tree.</summary>
    [RelayCommand]
    private void ClearMarkers()
    {
        RequireText = string.Empty;
        ForbidText = string.Empty;
        Status = "Cleared Must take and Never take marks.";
    }

    /// <summary>
    /// Empty Goals: nothing chased, nothing blocked, no marks, empty tree. Budget and search time stay.
    /// </summary>
    [RelayCommand]
    private void ResetGoals()
    {
        foreach (var row in Weights)
            row.Clear();

        RequireText = string.Empty;
        ForbidText = string.Empty;
        UnwaveringVision = PointGrantChoice.Auto;
        KeepCurrentAllocation = false;
        ExclusionWeight = (decimal)new SolveProfile().ExclusionWeight;

        if (Session is { } session)
        {
            session.Clear();
            PlanSteps = null;
            Order.Clear();
            LastPlan = null;
            SolveSummary = string.Empty;
            SolveNotes = string.Empty;
        }

        RefreshGoalLists();
        RefreshMechanicSuggestions();
        OnPropertyChanged(nameof(ActiveWeightSummary));
        Status = "Reset to defaults — nothing weighted, nothing selected.";
    }

    [RelayCommand]
    private void SaveNamedPlan()
    {
        if (Session is null)
            return;

        if (string.IsNullOrWhiteSpace(PlanName) || PlanName == "Unnamed plan")
        {
            Status = "Give the plan a name before saving.";
            return;
        }

        try
        {
            var path = PlanLibrary.Save(CaptureSettings(), LibraryFolder);
            RefreshSavedPlanNames();
            SelectedSavedPlan = Path.GetFileNameWithoutExtension(path);
            Status = $"Saved '{SelectedSavedPlan}'.";
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadNamedPlan()
    {
        if (Session is not { } session)
            return;

        var name = SelectedSavedPlan;
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = "Pick a saved plan to load.";
            return;
        }

        var loaded = PlanLibrary.TryLoad(name, LibraryFolder);
        if (loaded is null)
        {
            Status = $"No saved plan named '{name}'.";
            RefreshSavedPlanNames();
            return;
        }

        ApplySettings(session, loaded, applyEmptyGoals: true);
        OnAllocationChanged();
        PlanSteps = null;
        Order.Clear();
        LastPlan = null;
        SolveSummary = string.Empty;
        SolveNotes = string.Empty;
        Status = $"Loaded '{name}'.";
    }

    [RelayCommand]
    private void DeleteNamedPlan()
    {
        var name = SelectedSavedPlan;
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (PlanLibrary.Delete(name, LibraryFolder))
        {
            RefreshSavedPlanNames();
            SelectedSavedPlan = SavedPlanNames.FirstOrDefault();
            Status = $"Deleted '{name}'.";
        }
    }

    private void RefreshSavedPlanNames()
    {
        var selected = SelectedSavedPlan;
        Replace(SavedPlanNames, PlanLibrary.ListNames(LibraryFolder));
        SelectedSavedPlan = SavedPlanNames.Contains(selected ?? "")
            ? selected
            : SavedPlanNames.FirstOrDefault();
    }

    private void RefreshQuickColumns()
    {
        Replace(QuickTray, Weights.Where(row => row.Mode == MechanicMode.Ignore)
            .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase));
        Replace(QuickBlock, Weights.Where(row => row.Mode == MechanicMode.Block)
            .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase));
        Replace(QuickChase, Weights.Where(row => row.Mode == MechanicMode.Chase)
            .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void RemoveRequiredChip(MarkChip? chip)
    {
        if (chip is null || Session is not { } session)
            return;

        RequireText = WithoutReferenceText(RequireText, chip.Reference);
        Status = $"No longer required: {chip.Label}.";
    }

    [RelayCommand]
    private void RemoveForbiddenChip(MarkChip? chip)
    {
        if (chip is null)
            return;

        ForbidText = WithoutReferenceText(ForbidText, chip.Reference);
        Status = $"No longer banned: {chip.Label}.";
    }

    private static string WithoutReferenceText(string text, string reference)
    {
        var kept = SplitList(text)
            .Where(entry => !entry.Equals(reference, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return string.Join(Environment.NewLine, kept);
    }

    private void RefreshMarkChips()
    {
        RequiredChips.Clear();
        ForbiddenChips.Clear();

        if (Session is not { } session)
            return;

        foreach (var reference in SplitList(RequireText))
            RequiredChips.Add(ChipFor(session.Tree, reference));

        foreach (var reference in SplitList(ForbidText))
            ForbiddenChips.Add(ChipFor(session.Tree, reference));
    }

    private static MarkChip ChipFor(AtlasTree tree, string reference)
    {
        if (int.TryParse(reference, out var id) && tree.TryGet(id, out var node) && node is not null)
        {
            return new MarkChip
            {
                Reference = reference,
                Label = node.Name.Length > 0 ? node.Name : $"#{id}",
            };
        }

        return new MarkChip { Reference = reference, Label = reference };
    }

    private void OnAllocationChanged()
    {
        if (Session is not { } session)
            return;

        var granted = session.PointsGranted;
        var grantedLabel = granted > 0 ? $" (+{granted} granted)" : string.Empty;
        PointsLabel = $"{session.PointsSpent} / {session.Budget + granted} points{grantedLabel}";

        RefreshTally(session);
    }

    private void RefreshTally(PlannerSession session)
    {
        var tally = session.Tally();

        Replace(TallySummed, tally.Summed.Select(TallyRow.From));
        Replace(TallyRepeated, tally.Repeated.Select(TallyRow.From));
        Replace(TallyFlags, tally.Flags.Select(TallyRow.From));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    public void OnNodeActivated(int nodeId)
    {
        if (Session is not { } session)
            return;

        var added = session.AllocateTo(nodeId);
        Status = added.Count switch
        {
            0 => "That node cannot be reached from what is allocated.",
            1 => $"Allocated {Describe(session.Tree[nodeId])}.",
            _ => $"Allocated {added.Count} nodes to reach {Describe(session.Tree[nodeId])}.",
        };

        // A live order stays useful when you push further out by hand; rebuild it from what is allocated.
        if (added.Count > 0)
            RefreshOrderFromAllocation();
    }

    public void OnNodeRemoved(int nodeId)
    {
        if (Session is not { } session)
            return;

        var removed = session.Deallocate(nodeId);
        Status = removed.Count switch
        {
            0 => "Nothing to remove there.",
            1 => $"Removed {Describe(session.Tree[nodeId])}.",
            _ => $"Removed {Describe(session.Tree[nodeId])} and {removed.Count - 1} node(s) that hung off it.",
        };

        if (removed.Count > 0)
            RefreshOrderFromAllocation();
    }

    /// <summary>
    /// Ctrl+click: toggle Must take. Solve will route through every node listed here.
    /// </summary>
    public void OnNodeRequired(int nodeId)
    {
        if (Session is not { } session || !session.Tree.TryGet(nodeId, out var node) || node is null)
            return;

        if (!node.IsAllocatable || node.Kind == NodeKind.Start)
        {
            Status = "That node cannot be required.";
            return;
        }

        // Must take and Never take are opposites for the same node.
        ForbidText = WithoutNodeReference(ForbidText, session.Tree, nodeId);

        var reference = ReferenceFor(session.Tree, node);
        var (next, nowRequired) = ToggledNodeReference(RequireText, session.Tree, nodeId, reference);
        RequireText = next;
        Status = nowRequired
            ? $"Must take: {Describe(node)}. Solve will go for it."
            : $"No longer required: {Describe(node)}.";
    }

    /// <summary>
    /// Alt+click: toggle Never take. An allocated banned node is removed so the tree matches the ban.
    /// </summary>
    public void OnNodeForbidden(int nodeId)
    {
        if (Session is not { } session || !session.Tree.TryGet(nodeId, out var node) || node is null)
            return;

        if (!node.IsAllocatable || node.Kind == NodeKind.Start)
        {
            Status = "That node cannot be banned.";
            return;
        }

        RequireText = WithoutNodeReference(RequireText, session.Tree, nodeId);

        var reference = ReferenceFor(session.Tree, node);
        var (next, nowForbidden) = ToggledNodeReference(ForbidText, session.Tree, nodeId, reference);
        ForbidText = next;

        if (!nowForbidden)
        {
            Status = $"No longer banned: {Describe(node)}.";
            return;
        }

        if (session.IsAllocated(nodeId))
        {
            var removed = session.Deallocate(nodeId);
            RefreshOrderFromAllocation();
            Status = removed.Count > 1
                ? $"Never take: {Describe(node)}. Removed it and {removed.Count - 1} node(s) that hung off it."
                : $"Never take: {Describe(node)}. Removed it from the tree.";
            return;
        }

        Status = $"Never take: {Describe(node)}. Solve will avoid it.";
    }

    partial void OnRequireTextChanged(string value) => RefreshMarkSets();

    partial void OnForbidTextChanged(string value) => RefreshMarkSets();

    /// <summary>
    /// Rebuilds the step list from whatever is allocated right now, so hand edits keep a usable order
    /// without forcing a full re-solve.
    /// </summary>
    private void RefreshOrderFromAllocation()
    {
        if (Session is not { } session)
            return;

        // No order was ever produced — leave the panels alone rather than inventing one from a click.
        if (Order.Count == 0 && LastPlan is null)
            return;

        if (session.PointsSpent == 0)
        {
            PlanSteps = null;
            Order.Clear();
            LastPlan = null;
            SolveSummary = string.Empty;
            return;
        }

        try
        {
            var plan = BuildExportPlan(session);
            LastPlan = plan;
            PlanSteps = plan.Order.ToDictionary(step => step.NodeId, step => step.Step);
            Replace(Order, plan.Order.Select(step => OrderRow.From(step, session.Tree)));
        }
        catch (Exception ex)
        {
            Status = $"Could not refresh the order: {ex.Message}";
        }
    }

    private void RefreshMarkSets()
    {
        if (Session is not { } session)
        {
            RequiredNodes = null;
            ForbiddenNodes = null;
            RequiredChips.Clear();
            ForbiddenChips.Clear();
            return;
        }

        RequiredNodes = SoftResolve(session.Tree, RequireText);
        ForbiddenNodes = SoftResolve(session.Tree, ForbidText);
        RefreshMarkChips();
    }

    /// <summary>
    /// Resolves names and ids without throwing, so typing a half-finished name in the box does not
    /// wipe the rings on the tree.
    /// </summary>
    private static IReadOnlySet<int>? SoftResolve(AtlasTree tree, string text)
    {
        var ids = new HashSet<int>();
        foreach (var reference in SplitList(text))
        {
            if (int.TryParse(reference, out var id))
            {
                if (tree.TryGet(id, out var byId) && byId is { IsAllocatable: true })
                    ids.Add(id);
                continue;
            }

            foreach (var node in tree.Nodes.Values)
            {
                if (node.IsAllocatable &&
                    node.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))
                    ids.Add(node.Id);
            }
        }

        return ids.Count == 0 ? null : ids;
    }

    /// <summary>
    /// Prefer a unique name so the text box stays readable; fall back to the id when the name is shared.
    /// </summary>
    private static string ReferenceFor(AtlasTree tree, AtlasNode node)
    {
        if (node.Name.Length == 0)
            return node.Id.ToString(CultureInfo.InvariantCulture);

        var copies = tree.Nodes.Values.Count(n =>
            n.IsAllocatable && n.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase));

        return copies == 1 ? node.Name : node.Id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Adds <paramref name="reference"/> if the node is not already listed, otherwise removes every
    /// entry that points at it. Returns the new text and whether the node is now present.
    /// </summary>
    private static (string Text, bool Present) ToggledNodeReference(
        string text,
        AtlasTree tree,
        int nodeId,
        string reference)
    {
        if (SoftResolve(tree, text)?.Contains(nodeId) == true)
            return (WithoutNodeReference(text, tree, nodeId), false);

        var lines = SplitList(text);
        lines.Add(reference);
        return (string.Join(Environment.NewLine, lines), true);
    }

    private static string WithoutNodeReference(string text, AtlasTree tree, int nodeId)
    {
        var kept = SplitList(text)
            .Where(reference => !ReferenceMatches(tree, reference, nodeId))
            .ToList();
        return string.Join(Environment.NewLine, kept);
    }

    private static bool ReferenceMatches(AtlasTree tree, string reference, int nodeId)
    {
        if (int.TryParse(reference, out var id))
            return id == nodeId;

        return tree.TryGet(nodeId, out var node) &&
               node is not null &&
               node.Name.Equals(reference, StringComparison.OrdinalIgnoreCase);
    }

    public void OnScaleChanged(double scale) =>
        ZoomLabel = $"zoom {scale / Camera.MaxScale * 100:0}%";

    public void OnHoverChanged(AtlasNode? node)
    {
        if (node is null || Session is not { } session)
        {
            HoveredNode = null;
            return;
        }

        var index = session.Tree.IndexOf(node.Id);
        var distance = index < _distancesFromStart.Length ? _distancesFromStart[index] : -1;
        var mark = RequiredNodes?.Contains(node.Id) == true
            ? "Must take"
            : ForbiddenNodes?.Contains(node.Id) == true
                ? "Never take"
                : null;
        HoveredNode = NodeDetail.From(node, session.IsAllocated(node.Id), distance, mark);
    }

    [RelayCommand]
    private async Task SolveAsync()
    {
        if (Session is not { } session || IsSolving)
            return;

        var profile = BuildProfile(session);
        if (profile.Weights.Count == 0 && profile.Require.Count == 0 && profile.ExcludeMechanics.Count == 0)
        {
            Status = "Give the solver something to aim at: weight a category, switch one off, or require a node.";
            return;
        }

        IsSolving = true;
        Status = "Solving.";

        try
        {
            var outcome = await session.SolveAsync(profile);
            ApplyOutcome(session, outcome);
        }
        catch (InfeasibleRouteException ex)
        {
            Status = $"No route satisfies that request: {ex.Message}";
        }
        catch (Exception ex)
        {
            Status = $"Solve failed: {ex.Message}";
        }
        finally
        {
            IsSolving = false;
        }
    }

    private void ApplyOutcome(PlannerSession session, SolveOutcome outcome)
    {
        LastPlan = outcome.Plan;

        session.Budget = outcome.Solution.Budget - outcome.Solution.PointsGranted;
        session.SetAllocation(outcome.Plan.Nodes);

        PlanSteps = outcome.Plan.Order.ToDictionary(step => step.NodeId, step => step.Step);
        Replace(Order, outcome.Plan.Order.Select(step => OrderRow.From(step, session.Tree)));

        var stats = outcome.Solution.Stats;
        SolveSummary =
            $"Score {outcome.Plan.Prize:0} · {outcome.Plan.PointsSpent}/{outcome.Plan.Budget} points · " +
            $"{stats.ElapsedMs} ms · {stats.LocalSearchImprovements} improvements";

        SolveNotes = BuildSolveNotes(session.Tree, outcome);
        Status = $"Planned {outcome.Plan.Order.Count} steps. {SolveSummary}";
    }

    /// <summary>
    /// Explains the decisions the prize model made that are not visible in the route itself, since
    /// silently ignoring a weighted category is the kind of thing that looks like a bug.
    /// </summary>
    private static string BuildSolveNotes(AtlasTree tree, SolveOutcome outcome)
    {
        var notes = new List<string>();
        var prize = outcome.Prize;

        if (prize.PointGranter is { } granter)
        {
            var taken = outcome.Solution.Nodes.Contains(granter.Id);
            notes.Add($"{granter.Name}: {(taken ? "taken" : "not taken")}");
        }

        var offSwitches = outcome.Plan.Nodes
            .Select(id => tree[id])
            .Where(node => node.IsExclusionNotable)
            .Select(node => node.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (offSwitches.Length > 0)
            notes.Add($"Events switched off by: {string.Join(", ", offSwitches)}");

        if (prize.ExcludedWithoutOffSwitch.Count > 0)
            notes.Add(
                "No off-switch exists on the tree for: " +
                $"{string.Join(", ", prize.ExcludedWithoutOffSwitch.Order(StringComparer.OrdinalIgnoreCase))}. " +
                "Their nodes are avoided, but the encounters can still appear.");

        if (prize.ZeroedByRequirement.Count > 0)
        {
            var categories = prize.ZeroedByRequirement.Select(n => n.Category).Distinct().Order();
            notes.Add($"Zeroed by a required node: {string.Join(", ", categories)}");
        }

        if (prize.Nullified.Count > 0)
            notes.Add($"{prize.Nullified.Count} node(s) skipped for switching off a weighted category");

        return string.Join("\n", notes);
    }

    private SolveProfile BuildProfile(PlannerSession session) => new()
    {
        Name = PlanName,
        Budget = Budget,
        Weights = Weights
            .Where(row => row.IsActive)
            .ToDictionary(row => row.Category, row => (double)row.Weight, StringComparer.OrdinalIgnoreCase),
        Require = SplitList(RequireText),
        Forbid = SplitList(ForbidText),
        ExcludeMechanics = Weights.Where(row => row.Mode == MechanicMode.Block).Select(row => row.Category).ToList(),
        ExclusionWeight = (double)ExclusionWeight,
        UnwaveringVision = UnwaveringVision,
        PreAllocated = KeepCurrentAllocation
            ? session.Allocated.Where(id => id != session.Tree.StartNodeId).ToList()
            : [],
        TimeLimitMs = Math.Max(100, TimeLimitMs),
    };

    private static List<string> SplitList(string text) =>
        text.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [RelayCommand]
    private void ClearRoute()
    {
        if (Session is not { } session)
            return;

        session.Budget = session.Tree.TotalPoints;
        Budget = session.Tree.TotalPoints;
        session.Clear();
        PlanSteps = null;
        Order.Clear();
        SolveSummary = string.Empty;
        SolveNotes = string.Empty;
        LastPlan = null;
        Status = "Cleared.";
    }

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        if (Session is not { } session || WriteClipboard is null)
            return;

        // ToUrl already returns a full pathofexile.com link, ready to paste into the game or the overlay.
        await WriteClipboard(session.ToUrl());
        Status = $"Copied a {session.PointsSpent}-point tree URL to the clipboard.";
    }

    /// <summary>
    /// Writes the plan where the in-game overlay looks for it. Exports whatever is on the canvas, so a
    /// tree that was solved and then adjusted by hand exports as adjusted.
    /// </summary>
    [RelayCommand]
    private void ExportPlan()
    {
        if (Session is not { } session)
            return;

        if (session.PointsSpent == 0)
        {
            Status = "Nothing to export yet. Solve a plan or allocate some nodes first.";
            return;
        }

        var folder = ExportFolder ?? PlanFolder.ResolvePlansFolder();
        if (folder is null)
        {
            Status = "Could not find the ExileAPI folder to export into. " +
                     "Run the planner from inside your ExileAPI install, or use Copy tree URL instead.";
            return;
        }

        try
        {
            var plan = BuildExportPlan(session);
            var path = PlanFolder.Export(plan, folder);
            LastExportPath = path;
            Status = $"Exported '{plan.Name}' ({plan.Order.Count} steps) to {path}. " +
                     "Pick it up in the AtlasPlannerOverlay plugin in game.";
        }
        catch (Exception ex)
        {
            Status = $"Could not export the plan: {ex.Message}";
        }
    }

    /// <summary>
    /// Reuses the solved plan when it still matches the canvas, and rebuilds one from the current
    /// allocation when it does not.
    /// </summary>
    private AtlasPlan BuildExportPlan(PlannerSession session)
    {
        if (LastPlan is { } solved && solved.Nodes.ToHashSet().SetEquals(session.Allocated))
            return solved;

        var profile = BuildProfile(session);
        var prize = PrizeModel.Build(session.Tree, session.Scores, profile);
        return AtlasPlan.FromAllocation(session.Tree, session.Scores, profile, prize, session.Allocated);
    }

    [RelayCommand]
    private async Task PasteUrlAsync()
    {
        if (Session is not { } session || ReadClipboard is null)
            return;

        var text = await ReadClipboard();
        if (string.IsNullOrWhiteSpace(text))
        {
            Status = "The clipboard is empty.";
            return;
        }

        try
        {
            var unknown = session.LoadUrl(text.Trim());
            PlanSteps = null;
            Order.Clear();
            Status = unknown.Count == 0
                ? $"Imported {session.PointsSpent} allocated nodes."
                : $"Imported {session.PointsSpent} nodes; {unknown.Count} id(s) are not in this tree and were dropped.";
        }
        catch (Exception ex)
        {
            Status = $"That does not look like an atlas tree URL: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Search()
    {
        if (Session is not { } session)
            return;

        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            Highlighted = null;
            SearchSummary = string.Empty;
            return;
        }

        var matches = session.Tree.Nodes.Values
            .Where(node =>
                node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || node.Region.Contains(query, StringComparison.OrdinalIgnoreCase)
                || node.Stats.Any(stat => stat.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(node => node.Kind == NodeKind.Normal)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Highlighted = matches.Select(node => node.Id).ToHashSet();
        SearchSummary = matches.Length == 0
            ? "No matches."
            : $"{matches.Length} match(es). First: {Describe(matches[0])}";

        if (matches.Length > 0)
            RequestCentreOn?.Invoke(matches[0].Id);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        Highlighted = null;
        SearchSummary = string.Empty;
    }

    [RelayCommand]
    private void Fit() => RequestFit?.Invoke();

    [RelayCommand]
    private void FocusStep(OrderRow? row)
    {
        if (row is not null)
            RequestCentreOn?.Invoke(row.NodeId);
    }

    private static string Describe(AtlasNode node) =>
        node.Name.Length > 0 ? $"{node.Name} ({node.Kind})" : $"#{node.Id}";

    public string BudgetText
    {
        get => Budget.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, out var parsed))
                Budget = Math.Clamp(parsed, 0, 400);
        }
    }
}
