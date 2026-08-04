using System.Reflection;

namespace AtlasPlanner.Core;

/// <summary>App version shown in titles and the overlay. Driven by Directory.Build.props.</summary>
public static class AppVersion
{
    public const string PoELeague = "3.26";

    public static string Number { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static string DisplayName => $"Atlas Planner {Number}";

    public static string WindowTitle => $"{DisplayName} · PoE {PoELeague}";
}
