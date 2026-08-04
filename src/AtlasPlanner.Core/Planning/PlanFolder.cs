using System.Reflection;

namespace AtlasPlanner.Core.Planning;

/// <summary>
/// The drop folder the planner writes plans to and the overlay plugin reads them from.
/// </summary>
/// <remarks>
/// ExileAPI hands each plugin a per-plugin config directory, so the plugin never needs to search for
/// this. The planner does, because it runs as its own application and wants exporting to be one click
/// rather than a file dialog and a path the user has to remember.
/// </remarks>
public static class PlanFolder
{
    /// <summary>Must match the overlay plugin's folder name, which is what ExileAPI names its config by.</summary>
    public const string PluginName = "AtlasPlannerOverlay";

    public const string PlansFolderName = "Plans";
    public const string DefaultPlanName = "AtlasPlan";

    /// <summary>A file ExileAPI installs at its root, used to recognise the root when walking up.</summary>
    private const string RootMarker = "ExileCore.dll";

    /// <summary>
    /// Finds the ExileAPI install by walking up from the running application, or null when the planner
    /// has been copied somewhere outside one.
    /// </summary>
    public static string? FindExileApiRoot(string? startingAt = null)
    {
        var starts = startingAt is { Length: > 0 }
            ? Enumerable.Repeat(startingAt, 1)
            : CandidateRoots();

        foreach (var start in starts)
        {
            for (var directory = SafeDirectory(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, RootMarker)) &&
                    Directory.Exists(Path.Combine(directory.FullName, "Plugins")))
                    return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>Where plans belong inside a known install. Does not create the folder.</summary>
    public static string PlansFolderIn(string exileApiRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exileApiRoot);
        return Path.Combine(exileApiRoot, "config", PluginName, PlansFolderName);
    }

    /// <summary>
    /// Where plans belong for the plugin to see them, or null when the planner is not running from
    /// inside an ExileAPI install and so cannot know.
    /// </summary>
    public static string? ResolvePlansFolder() =>
        FindExileApiRoot() is { } root ? PlansFolderIn(root) : null;

    /// <summary>Turns a plan name into a file name that is safe on disk and recognisable in game.</summary>
    public static string FileNameFor(string? planName)
    {
        var trimmed = (planName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return DefaultPlanName + ".json";

        var cleaned = new string(trimmed
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
            .ToArray())
            .Trim('.', ' ');

        return (cleaned.Length == 0 ? DefaultPlanName : cleaned) + ".json";
    }

    /// <summary>
    /// Plans in a folder, most recently written first, so the plugin can offer the newest by default.
    /// Returns empty rather than throwing when the folder is absent.
    /// </summary>
    public static IReadOnlyList<string> List(string? folder)
    {
        if (folder is null or "" || !Directory.Exists(folder))
            return [];

        try
        {
            return Directory
                .EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes a plan where the plugin will find it, creating the folder if needed, and returns the path
    /// written. Re-exporting a plan of the same name replaces it.
    /// </summary>
    public static string Export(AtlasPlan plan, string plansFolder)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plansFolder);

        Directory.CreateDirectory(plansFolder);
        var path = Path.Combine(plansFolder, FileNameFor(plan.Name));
        plan.Save(path);
        return path;
    }

    private static DirectoryInfo? SafeDirectory(string path)
    {
        try
        {
            return new DirectoryInfo(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var assembly = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (!string.IsNullOrEmpty(assembly))
            yield return assembly;

        if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
            yield return AppContext.BaseDirectory;

        yield return Directory.GetCurrentDirectory();
    }
}
