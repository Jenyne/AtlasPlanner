using AtlasPlanner.Core;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Tree;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared;
using ExileCore.Shared.AtlasHelper;

namespace AtlasPlannerOverlay;

/// <summary>
/// Shows a plan from the Atlas Planner on the in-game atlas tree, and places the points in the order the
/// planner worked out.
/// </summary>
/// <remarks>
/// Everything that decides anything lives in AtlasPlanner.Core, which has tests. This class is the part
/// that cannot be tested without a game attached: reading the tree panel, drawing, and clicking.
/// </remarks>
public partial class AtlasPlannerOverlay : BaseSettingsPlugin<AtlasPlannerOverlaySettings>
{
    private const string TreeDataFile = "AtlasTreeData.json";

    private AtlasTree? _tree;
    private AtlasPlan? _plan;
    private string? _planPath;
    private string _loadStatus = string.Empty;
    private string _applyStatus = string.Empty;

    private List<string> _planFiles = [];
    private DateTime _planFilesCheckedUtc = DateTime.MinValue;
    private DateTime _planWrittenUtc = DateTime.MinValue;

    private AtlasTexture? _ringImage;
    private SyncTask<bool>? _applyTask;
    private bool _stopRequested;

    /// <summary>
    /// Tracked here rather than inferred from the task, so the button comes back even if the run is
    /// abandoned rather than finished.
    /// </summary>
    private bool _isPlacing;

    /// <summary>Where the planner drops plans: config\AtlasPlannerOverlay\Plans.</summary>
    private string PlansDirectory => Directory.CreateDirectory(
        Path.Combine(ConfigDirectory, PlanFolder.PlansFolderName)).FullName;

    public override void OnLoad()
    {
        Name = PlanFolder.PluginName;
        _ringImage = GetAtlasTexture("AtlasMapCircle");
    }

    public override bool Initialise()
    {
        LoadTree();
        RefreshPlanFiles(force: true);
        LoadNewestPlan();

        Input.RegisterKey(Settings.ApplyHotkey.Value);
        Settings.ApplyHotkey.OnValueChanged += () => Input.RegisterKey(Settings.ApplyHotkey.Value);
        Input.RegisterKey(Settings.StopHotkey.Value);
        Settings.StopHotkey.OnValueChanged += () => Input.RegisterKey(Settings.StopHotkey.Value);

        return true;
    }

    private void LoadTree()
    {
        var path = Path.Combine(DirectoryFullName, TreeDataFile);
        if (!File.Exists(path))
        {
            _loadStatus = $"Cannot find {TreeDataFile} beside the plugin, so nodes cannot be placed on screen.";
            LogError($"{Name}: {_loadStatus}", 10f);
            return;
        }

        try
        {
            _tree = AtlasTree.Load(path);
        }
        catch (Exception ex)
        {
            _loadStatus = $"Could not read {TreeDataFile}: {ex.Message}";
            LogError($"{Name}: {_loadStatus}", 10f);
        }
    }

    private void RefreshPlanFiles(bool force = false)
    {
        // Re-scanning every frame would hit the disk constantly; once a second is plenty to notice an export.
        if (!force && DateTime.UtcNow - _planFilesCheckedUtc < TimeSpan.FromSeconds(1))
            return;

        _planFilesCheckedUtc = DateTime.UtcNow;
        _planFiles = PlanFolder.List(PlansDirectory).ToList();

        // Pick up an export that overwrote the plan we are already showing.
        if (_planPath is not null && File.Exists(_planPath) &&
            File.GetLastWriteTimeUtc(_planPath) > _planWrittenUtc)
            LoadPlan(_planPath);
    }

    private void LoadNewestPlan()
    {
        if (_planFiles.Count == 0)
        {
            _loadStatus = $"No plans yet. Press 'Send to game' in the Atlas Planner and they appear in {PlansDirectory}.";
            return;
        }

        LoadPlan(_planFiles[0]);
    }

    private void LoadPlan(string path)
    {
        try
        {
            _plan = AtlasPlan.Load(path);
            _planPath = path;
            _planWrittenUtc = File.GetLastWriteTimeUtc(path);
            _applyStatus = string.Empty;
            _loadStatus = _plan.Order.Count == 0
                ? $"'{_plan.Name}' has no steps in it."
                : string.Empty;
        }
        catch (Exception ex)
        {
            _plan = null;
            _planPath = null;
            _loadStatus = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
            LogError($"{Name}: {_loadStatus}", 10f);
        }
    }

    /// <summary>
    /// Atlas passives that already count toward the plan: confirmed on the character, plus anything
    /// selected on the open tree that is waiting for the user to press Apply.
    /// </summary>
    private HashSet<int> TakenIds(TreePanel? panel)
    {
        var taken = AllocatedIds();
        if (panel is null)
            return taken;

        foreach (var passive in panel.Passives)
        {
            if (passive?.IsAllocatedForPlan == true &&
                passive.PassiveSkill is { } skill)
                taken.Add(skill.PassiveId);
        }

        return taken;
    }

    /// <summary>Atlas passives the server says are allocated (after Apply).</summary>
    private HashSet<int> AllocatedIds()
    {
        var ids = GameController?.Game?.IngameState?.ServerData?.AtlasPassiveSkillIds;
        if (ids is null)
            return [];

        var allocated = new HashSet<int>();
        foreach (var id in ids)
            allocated.Add(id);

        return allocated;
    }

    private TreePanel? AtlasPanel()
    {
        var ui = GameController?.Game?.IngameState?.IngameUi;
        if (ui?.AtlasTreePanel is null)
            return null;

        var panel = ((RemoteMemoryObject)ui.AtlasTreePanel).AsObject<TreePanel>();
        return panel is not null && ((Element)panel).IsVisible ? panel : null;
    }

    public override void Render()
    {
        try
        {
            var panel = AtlasPanel();
            if (panel is null)
            {
                // Leaving the tree cancels a run rather than letting it click at nothing.
                _applyTask = null;
                _isPlacing = false;
                return;
            }

            RefreshPlanFiles();

            if (Settings.StopHotkey.PressedOnce() && _isPlacing)
            {
                _stopRequested = true;
                _applyStatus = "Stopping.";
            }

            var taken = TakenIds(panel);
            var progress = _plan is null ? null : PlanProgress.For(_plan, taken);

            if (Settings.ApplyHotkey.PressedOnce())
                StartPlacing(panel, progress);

            if (Settings.ShowHighlights && progress is not null)
                DrawPlan(panel, progress);

            if (Settings.ShowPanel)
                DrawPanel(panel, progress);

            TaskUtils.RunOrRestart(ref _applyTask, () => null);
        }
        catch (Exception ex)
        {
            LogError($"{Name} render failed: {ex.Message}", 5f);
        }
    }
}
