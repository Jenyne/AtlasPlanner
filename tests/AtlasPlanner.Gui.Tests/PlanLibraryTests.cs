using AtlasPlanner.Gui.Services;

namespace AtlasPlanner.Gui.Tests;

public sealed class PlanLibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        $"atlasplanner-library-{Guid.NewGuid():N}");

    [Fact]
    public void Save_list_load_and_delete_use_the_plan_name()
    {
        var settings = new PlannerSettings
        {
            PlanName = "Abyss push",
            Budget = 120,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Abyss"] = 15,
            },
        };

        var path = PlanLibrary.Save(settings, _folder);
        Assert.True(File.Exists(path));
        Assert.Equal(["Abyss push"], PlanLibrary.ListNames(_folder));

        var loaded = PlanLibrary.TryLoad("Abyss push", _folder);
        Assert.NotNull(loaded);
        Assert.Equal(120, loaded.Budget);
        Assert.Equal(15, loaded.Weights["Abyss"]);

        Assert.True(PlanLibrary.Delete("Abyss push", _folder));
        Assert.Empty(PlanLibrary.ListNames(_folder));
        Assert.Null(PlanLibrary.TryLoad("Abyss push", _folder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
