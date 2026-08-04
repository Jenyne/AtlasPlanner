using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

/// <summary>Loads the bundled tree once for the whole test run; parsing it is not cheap.</summary>
public sealed class TreeFixture
{
    public TreeFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "AtlasTreeData.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Test tree data missing at '{path}'.");

        Tree = AtlasTree.Load(path);
    }

    public AtlasTree Tree { get; }
}

[CollectionDefinition(Name)]
public sealed class TreeCollection : ICollectionFixture<TreeFixture>
{
    public const string Name = "atlas tree";
}
