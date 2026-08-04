using CommunityToolkit.Mvvm.ComponentModel;

namespace AtlasPlanner.Gui.ViewModels;

/// <summary>How a mechanic is treated in the Goals panel.</summary>
public enum MechanicMode
{
    /// <summary>Weight 0, not blocked. The default for most categories.</summary>
    Ignore,

    /// <summary>Farm this; priority maps to a solve weight.</summary>
    Chase,

    /// <summary>Buy the off-switch and avoid its nodes.</summary>
    Block,
}

/// <summary>
/// Compact priority for chased mechanics. The numeric values are the solve weights they map to.
/// </summary>
public enum ChasePriority
{
    Low = 3,
    Normal = 8,
    High = 15,
    Critical = 25,
}

public static class ChasePriorityMap
{
    public static ChasePriority Nearest(decimal weight)
    {
        var value = Math.Abs(weight);
        if (value <= 5m) return ChasePriority.Low;
        if (value <= 11m) return ChasePriority.Normal;
        if (value <= 20m) return ChasePriority.High;
        return ChasePriority.Critical;
    }
}
