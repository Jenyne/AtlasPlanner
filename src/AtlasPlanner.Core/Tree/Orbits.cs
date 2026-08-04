namespace AtlasPlanner.Core.Tree;

/// <summary>
/// Orbit angle tables from the game's tree renderer. The 16- and 40-slot orbits are not evenly
/// spaced; they snap to these angles.
/// </summary>
internal static class Orbits
{
    private static readonly int[] Angles16 =
        [0, 30, 45, 60, 90, 120, 135, 150, 180, 210, 225, 240, 270, 300, 315, 330];

    private static readonly int[] Angles40 =
    [
        0, 10, 20, 30, 40, 45, 50, 60, 70, 80,
        90, 100, 110, 120, 130, 135, 140, 150, 160, 170,
        180, 190, 200, 210, 220, 225, 230, 240, 250, 260,
        270, 280, 290, 300, 310, 315, 320, 330, 340, 350,
    ];

    public static float AngleFor(int orbitIndex, int slotsInOrbit)
    {
        if (slotsInOrbit <= 0)
            return 0f;

        var index = Math.Max(orbitIndex, 0);

        return slotsInOrbit switch
        {
            16 => Angles16[Math.Min(index, Angles16.Length - 1)] * MathF.PI / 180f,
            40 => Angles40[Math.Min(index, Angles40.Length - 1)] * MathF.PI / 180f,
            _ => MathF.PI * 2f * index / slotsInOrbit,
        };
    }
}
