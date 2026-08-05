using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public sealed class LinkedDifficultyWeightsTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    [Fact]
    public void Chasing_monster_difficulty_also_weights_map_modifiers()
    {
        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [LinkedDifficultyWeights.MonsterDifficulty] = 25,
            },
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);

        Assert.Equal(25, prize.EffectiveWeights[LinkedDifficultyWeights.MapModifiers]);
        Assert.True(prize.PrizeOf(63758) > 0, "Map Modifier Effect hub should score under difficulty chase");
    }

    [Fact]
    public void Chasing_map_modifiers_also_weights_monster_difficulty()
    {
        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [LinkedDifficultyWeights.MapModifiers] = 15,
            },
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);

        Assert.Equal(15, prize.EffectiveWeights[LinkedDifficultyWeights.MonsterDifficulty]);
    }

    [Fact]
    public void Difficulty_chase_prefers_hat_nodes_over_blight_quantity()
    {
        var profile = new SolveProfile
        {
            Budget = 138,
            TimeLimitMs = 100,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [LinkedDifficultyWeights.MonsterDifficulty] = 25,
            },
            ExcludeMechanics = ["Blight"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var solution = RouteSolver.Solve(Tree, prize, profile);

        Assert.Contains(63758, solution.Nodes);
        Assert.DoesNotContain(22085, solution.Nodes);
    }
}
