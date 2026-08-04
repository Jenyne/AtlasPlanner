using AtlasPlanner.Core.Planning;

namespace AtlasPlanner.Core.Tests;

public class PlanFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"atlasfolder-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Fabricates enough of an ExileAPI install to be recognised as one.</summary>
    private string FakeInstall()
    {
        var install = Path.Combine(_root, "ExileApi");
        Directory.CreateDirectory(Path.Combine(install, "Plugins"));
        File.WriteAllText(Path.Combine(install, "ExileCore.dll"), "not really a dll");
        return install;
    }

    [Fact]
    public void An_install_is_recognised_from_a_folder_buried_inside_it()
    {
        var install = FakeInstall();
        var deep = Path.Combine(install, "AtlasPlanner", "src", "AtlasPlanner.Gui", "bin", "Debug");
        Directory.CreateDirectory(deep);

        Assert.Equal(install, PlanFolder.FindExileApiRoot(deep));
    }

    [Fact]
    public void A_folder_that_only_looks_like_an_install_is_not_one()
    {
        // The marker dll without the Plugins folder beside it is not enough.
        var pretender = Path.Combine(_root, "Pretender");
        Directory.CreateDirectory(pretender);
        File.WriteAllText(Path.Combine(pretender, "ExileCore.dll"), "not really a dll");

        Assert.Null(PlanFolder.FindExileApiRoot(pretender));
    }

    [Fact]
    public void Somewhere_outside_an_install_reports_no_root_rather_than_guessing()
    {
        var orphan = Path.Combine(_root, "Elsewhere");
        Directory.CreateDirectory(orphan);

        Assert.Null(PlanFolder.FindExileApiRoot(orphan));
    }

    [Fact]
    public void The_plans_folder_is_the_plugins_own_config_folder()
    {
        var install = FakeInstall();

        var plans = PlanFolder.PlansFolderIn(install);

        Assert.Equal(Path.Combine(install, "config", PlanFolder.PluginName, "Plans"), plans);
    }

    [Theory]
    [InlineData("Juiced Delirium", "Juiced Delirium.json")]
    [InlineData("bad/name:here?", "bad_name_here_.json")]
    [InlineData("", "AtlasPlan.json")]
    [InlineData("   ", "AtlasPlan.json")]
    [InlineData("...", "AtlasPlan.json")]
    [InlineData(null, "AtlasPlan.json")]
    public void Plan_names_become_file_names_that_survive_the_disk(string? planName, string expected)
    {
        Assert.Equal(expected, PlanFolder.FileNameFor(planName));
    }

    [Fact]
    public void Listing_a_folder_that_is_not_there_is_empty_rather_than_a_crash()
    {
        Assert.Empty(PlanFolder.List(Path.Combine(_root, "nope")));
        Assert.Empty(PlanFolder.List(null));
        Assert.Empty(PlanFolder.List(string.Empty));
    }

    [Fact]
    public void Plans_are_listed_newest_first_so_the_last_export_is_offered_by_default()
    {
        var folder = Path.Combine(_root, "Plans");
        Directory.CreateDirectory(folder);

        var older = Path.Combine(folder, "older.json");
        var newer = Path.Combine(folder, "newer.json");
        File.WriteAllText(older, "{}");
        File.WriteAllText(newer, "{}");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        File.WriteAllText(Path.Combine(folder, "notes.txt"), "ignored");

        Assert.Equal([newer, older], PlanFolder.List(folder));
    }

    [Fact]
    public void Exporting_writes_a_plan_the_plugin_can_load_back()
    {
        var install = FakeInstall();
        var plan = new AtlasPlan
        {
            Name = "Export Me",
            TreeName = "atlas",
            Nodes = [1, 2, 3],
            Order = [new PlanStep { Step = 1, NodeId = 2, Name = "Second" }],
        };

        var path = PlanFolder.Export(plan, PlanFolder.PlansFolderIn(install));

        Assert.Equal("Export Me.json", Path.GetFileName(path));
        Assert.True(File.Exists(path));

        var reloaded = AtlasPlan.Load(path);
        Assert.Equal("Export Me", reloaded.Name);
        Assert.Equal([1, 2, 3], reloaded.Nodes);
        Assert.Equal(2, reloaded.Order.Single().NodeId);
    }

    [Fact]
    public void Exporting_the_same_plan_twice_overwrites_rather_than_piling_up()
    {
        var folder = PlanFolder.PlansFolderIn(FakeInstall());
        var plan = new AtlasPlan { Name = "Repeat", Nodes = [1] };

        PlanFolder.Export(plan, folder);
        plan.Nodes = [1, 2];
        var second = PlanFolder.Export(plan, folder);

        Assert.Single(PlanFolder.List(folder));
        Assert.Equal([1, 2], AtlasPlan.Load(second).Nodes);
    }

    [Fact]
    public void Exporting_creates_the_drop_folder_the_first_time()
    {
        var folder = PlanFolder.PlansFolderIn(FakeInstall());
        Assert.False(Directory.Exists(folder));

        PlanFolder.Export(new AtlasPlan { Name = "First" }, folder);

        Assert.True(Directory.Exists(folder));
    }
}
