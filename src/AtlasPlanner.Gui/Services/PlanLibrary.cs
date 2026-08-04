using AtlasPlanner.Core.Planning;

namespace AtlasPlanner.Gui.Services;

/// <summary>
/// Named snapshots of a plan request + tree, stored under the per-user app data folder so they never
/// touch the repository. Uses the same JSON shape as session settings.
/// </summary>
public static class PlanLibrary
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AtlasPlanner",
        "Library");

    public static string PathFor(string planName, string? directory = null) =>
        Path.Combine(directory ?? DefaultDirectory, PlanFolder.FileNameFor(planName));

    public static IReadOnlyList<string> ListNames(string? directory = null)
    {
        var folder = directory ?? DefaultDirectory;
        if (!Directory.Exists(folder))
            return [];

        try
        {
            return Directory
                .EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .Where(name => name.Length > 0)
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

    public static string Save(PlannerSettings settings, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var folder = directory ?? DefaultDirectory;
        Directory.CreateDirectory(folder);
        var path = PathFor(settings.PlanName, folder);
        if (!settings.TrySave(path))
            throw new IOException($"Could not write '{path}'.");
        return path;
    }

    public static PlannerSettings? TryLoad(string planName, string? directory = null)
    {
        var path = PathFor(planName, directory);
        if (!File.Exists(path))
            return null;

        return PlannerSettings.Load(path);
    }

    public static bool Delete(string planName, string? directory = null)
    {
        var path = PathFor(planName, directory);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }
}
