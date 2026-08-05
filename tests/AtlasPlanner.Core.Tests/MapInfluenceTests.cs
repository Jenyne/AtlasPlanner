using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public sealed class MapInfluenceTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    [Fact]
    public void Conqueror_pack_size_classifies_as_Conquerors_not_Pack_Size()
    {
        var scores = Scores();
        var node = Tree[64516];
        Assert.Equal("Conquerors", scores.Classify(node.Stats[0], node));
    }

    [Fact]
    public void Remnants_of_the_Past_classifies_as_Shaper_and_Elder()
    {
        var scores = Scores();
        var node = Tree[12651];
        Assert.Equal("The Shaper and Elder", scores.Classify(node.Stats[0], node));
    }

    [Fact]
    public void Unchased_influence_nodes_are_forbidden_even_when_Pack_Size_is_chased()
    {
        var profile = new SolveProfile
        {
            Budget = 138,
            TimeLimitMs = 100,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pack Size"] = 15,
                [LinkedDifficultyWeights.MapModifiers] = 25,
            },
            ExcludeMechanics = ["Conquerors", "The Shaper and Elder"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var solution = RouteSolver.Solve(Tree, prize, profile);

        Assert.Contains(12651, prize.ForbiddenIds);
        Assert.Contains(64516, prize.ForbiddenIds);
        Assert.DoesNotContain(12651, solution.Nodes);
        Assert.DoesNotContain(64516, solution.Nodes);
    }

    [Fact]
    public void Chasing_one_influence_forbids_the_other_three()
    {
        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [MapInfluenceCategories.Conquerors] = 15,
            },
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);

        Assert.DoesNotContain(64516, prize.ForbiddenIds);
        Assert.Contains(12651, prize.ForbiddenIds);
        Assert.False(prize.ForbiddenIds.Contains(12651) && prize.ForbiddenIds.Contains(16752));
    }
}
