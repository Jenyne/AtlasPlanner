using System.Reflection;

namespace AtlasPlanner.Core.Io;

/// <summary>
/// Finds the bundled tree and score data without the caller having to pass a path every time.
/// Searches the working directory and the app directory, then walks up from each.
/// </summary>
public static class DataLocator
{
    public const string TreeFileName = "AtlasTreeData.json";
    public const string ScoresFileName = "atlasscores.json";
    public const string TreeEnvironmentVariable = "ATLASPLANNER_TREE";

    public static string ResolveTree(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath)
                ? explicitPath
                : throw new FileNotFoundException($"Tree data not found at '{explicitPath}'.");

        var fromEnvironment = Environment.GetEnvironmentVariable(TreeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
            return fromEnvironment;

        return Search(TreeFileName)
               ?? throw new FileNotFoundException(
                   $"Could not find {TreeFileName}. Pass an explicit path or set {TreeEnvironmentVariable}.");
    }

    public static string? ResolveScores(string? explicitPath = null) =>
        !string.IsNullOrWhiteSpace(explicitPath) ? explicitPath : Search(ScoresFileName);

    public static string? Search(string fileName)
    {
        foreach (var root in CandidateRoots())
        {
            var directory = new DirectoryInfo(root);
            while (directory is not null)
            {
                var direct = Path.Combine(directory.FullName, fileName);
                if (File.Exists(direct))
                    return direct;

                var underData = Path.Combine(directory.FullName, "data", fileName);
                if (File.Exists(underData))
                    return underData;

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Directory.GetCurrentDirectory();

        var assembly = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (!string.IsNullOrEmpty(assembly))
            yield return assembly;

        var appContext = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appContext))
            yield return appContext;
    }
}
