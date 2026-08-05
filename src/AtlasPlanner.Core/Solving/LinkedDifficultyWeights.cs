namespace AtlasPlanner.Core.Solving;

/// <summary>
/// Categories the planner treats as one goal: more explicit modifiers on a map means harder monsters.
/// </summary>
public static class LinkedDifficultyWeights
{
    public const string MonsterDifficulty = "Monster Difficulty";
    public const string MapModifiers = "Map Modifiers";

    /// <summary>
    /// Chase on either category applies to both; block on either penalises both. When both are set,
    /// the stronger chase or block wins.
    /// </summary>
    public static void Apply(Dictionary<string, double> weights)
    {
        var monster = weights.GetValueOrDefault(MonsterDifficulty, 0d);
        var modifiers = weights.GetValueOrDefault(MapModifiers, 0d);

        if (monster > 0d || modifiers > 0d)
        {
            var chase = Math.Max(monster, modifiers);
            weights[MonsterDifficulty] = chase;
            weights[MapModifiers] = chase;
            return;
        }

        if (monster < 0d || modifiers < 0d)
        {
            var block = Math.Min(monster, modifiers);
            if (monster < 0d)
                weights[MapModifiers] = block;
            if (modifiers < 0d)
                weights[MonsterDifficulty] = block;
        }
    }
}
