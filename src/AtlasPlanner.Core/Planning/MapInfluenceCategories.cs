namespace AtlasPlanner.Core.Planning;

/// <summary>
/// Old atlas map influences — a map can only run one at a time, so the solver must never mix them.
/// </summary>
public static class MapInfluenceCategories
{
    public const string Conquerors = "Conquerors";
    public const string ShaperAndElder = "The Shaper and Elder";
    public const string EaterOfWorlds = "The Eater of Worlds";
    public const string SearingExarch = "The Searing Exarch";

    public static IReadOnlyList<string> All { get; } =
        [Conquerors, ShaperAndElder, EaterOfWorlds, SearingExarch];

    public static int CountChased(IReadOnlyDictionary<string, double> weights) =>
        All.Count(category => weights.GetValueOrDefault(category, 0d) > 0d);
}
