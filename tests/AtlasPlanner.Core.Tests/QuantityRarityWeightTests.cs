using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public sealed class QuantityRarityWeightTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    [Fact]
    public void One_percent_quantity_outscores_two_percent_rarity_under_same_category_weight()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);

        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Quantity & Rarity"] = 10,
            },
        };

        var prize = PrizeModel.Build(Tree, scores, profile);

        // 30402 = 1% Item Quantity; 13284 = 2% Item Rarity
        Assert.True(
            prize.PrizeOf(30402) > prize.PrizeOf(13284),
            $"Expected 1% quant ({prize.PrizeOf(30402)}) > 2% rarity ({prize.PrizeOf(13284)})");
    }
}
