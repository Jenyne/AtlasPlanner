using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public sealed class TrarthanVapoursClusterTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    [Fact]
    public void Combat_nodes_are_always_forbidden()
    {
        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mercenaries"] = 20,
            },
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);

        Assert.All(TrarthanVapoursCluster.CombatNodeIds, id => Assert.Contains(id, prize.ForbiddenIds));
    }

    [Fact]
    public void Chance_nodes_forbidden_when_Mercenaries_not_chased()
    {
        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Map Modifiers"] = 15,
            },
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);

        Assert.All(TrarthanVapoursCluster.ChanceNodeIds, id => Assert.Contains(id, prize.ForbiddenIds));
    }

    [Fact]
    public void Chance_nodes_allowed_but_cheaper_when_Mercenaries_chased()
    {
        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mercenaries"] = 20,
            },
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);

        Assert.All(TrarthanVapoursCluster.ChanceNodeIds, id => Assert.DoesNotContain(id, prize.ForbiddenIds));

        // Nearby Minor Fiefdoms (+30%) should outscore one distant +10% chance node.
        Assert.True(
            prize.PrizeOf(50374) > prize.PrizeOf(TrarthanVapoursCluster.OuterMercenaryChance),
            "Minor Fiefdoms should beat a discounted Trarthan chance small");
    }

    [Fact]
    public void Soft_banned_defaults_include_Trarthan_combat_nodes()
    {
        Assert.Contains(TrarthanVapoursCluster.TrarthanVapours, SpecializationCatalog.DefaultSoftBannedKeystones);
        Assert.Contains(TrarthanVapoursCluster.FaithInArms, SpecializationCatalog.DefaultSoftBannedKeystones);
    }
}
