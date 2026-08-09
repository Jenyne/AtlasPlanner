using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public sealed class BlockedMechanicTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    [Fact]
    public void Blocking_Atlas_Memories_forbids_memory_tear_nodes()
    {
        var profile = new SolveProfile
        {
            Budget = 80,
            TimeLimitMs = 50,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Map Modifiers"] = 10,
            },
            ExcludeMechanics = ["Atlas Memories"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);

        Assert.Contains("Atlas Memories", prize.ExcludedWithoutOffSwitch);

        var tearIds = Tree.Nodes.Values
            .Where(n => n.Name.Contains("Memory Tear", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .ToArray();

        Assert.NotEmpty(tearIds);
        Assert.All(tearIds, id => Assert.Contains(id, prize.ForbiddenIds));

        var solution = RouteSolver.Solve(Tree, prize, profile);
        Assert.All(tearIds, id => Assert.DoesNotContain(id, solution.Nodes));
    }

    [Fact]
    public void Blocking_Atlas_Memories_forbids_incarnation_reduction_notables()
    {
        var profile = new SolveProfile
        {
            Budget = 80,
            TimeLimitMs = 50,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Map Modifiers"] = 10,
            },
            ExcludeMechanics = ["Atlas Memories"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var incarnationIds = new[] { 5723, 58502, 40503 };

        Assert.All(incarnationIds, id => Assert.Contains(id, prize.ForbiddenIds));
    }

    [Fact]
    public void Blocked_downside_stats_do_not_score_positive()
    {
        var profile = new SolveProfile
        {
            ExcludeMechanics = ["Atlas Memories"],
            ExclusionWeight = 20,
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        Assert.True(prize.PrizeOf(5723) <= 0, "Educated Upbringing should not reward a blocked route");
    }

    [Fact]
    public void Blocking_General_does_not_hard_forbid_travel_nodes()
    {
        var scores = Scores();
        var profile = new SolveProfile
        {
            ExcludeMechanics = ["General"],
        };

        var prize = PrizeModel.Build(Tree, scores, profile);

        Assert.Contains("General", prize.ExcludedWithoutOffSwitch);

        var generalForbidden = prize.ForbiddenIds.Count(id =>
            Tree.Nodes.ContainsKey(id)
            && Tree[id].Stats.Any(stat => scores.Classify(stat, Tree[id]) == ScoreTable.Uncategorised));

        Assert.True(generalForbidden < 10,
            $"Blocking General should not forbid the whole tree (got {generalForbidden} General-tagged nodes)");
    }

    [Fact]
    public void Blocking_Essence_hard_forbids_essence_nodes()
    {
        var profile = new SolveProfile
        {
            Budget = 138,
            TimeLimitMs = 50,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Map Modifiers"] = 15,
            },
            ExcludeMechanics = ["Essence"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var essenceIds = Tree.Nodes.Values
            .Where(n => n.IsAllocatable && n.Region.Equals("Essence", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .Take(5)
            .ToArray();

        Assert.NotEmpty(essenceIds);
        Assert.All(essenceIds, id => Assert.Contains(id, prize.ForbiddenIds));
    }

    [Fact]
    public void Blocking_Incursion_hard_forbids_alva_chance_nodes()
    {
        var profile = new SolveProfile
        {
            ExcludeMechanics = ["Incursion"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var alvaIds = Tree.Nodes.Values
            .Where(n => n.Name.Equals("Incursion Chance", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .Take(3)
            .ToArray();

        Assert.NotEmpty(alvaIds);
        Assert.All(alvaIds, id => Assert.Contains(id, prize.ForbiddenIds));
    }

    [Fact]
    public void Delve_chance_stats_classify_as_Delve()
    {
        var scores = Scores();
        var node = Tree[64104];
        var category = scores.Classify(node.Stats[0], node);

        Assert.Equal("Delve", category);
    }

    [Fact]
    public void Blocking_Blight_forbids_quantity_nodes_in_blight_region()
    {
        var profile = new SolveProfile
        {
            Budget = 138,
            TimeLimitMs = 100,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Quantity & Rarity"] = 15,
                ["Map Modifiers"] = 15,
            },
            ExcludeMechanics = ["Blight"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var solution = RouteSolver.Solve(Tree, prize, profile);

        var blightQuantityIds = new[] { 22085, 38113, 17889 };
        Assert.All(blightQuantityIds, id => Assert.Contains(id, prize.ForbiddenIds));
        Assert.All(blightQuantityIds, id => Assert.DoesNotContain(id, solution.Nodes));
    }

    [Fact]
    public void Blight_quantity_stats_classify_as_Blight_not_Quantity()
    {
        var scores = Scores();
        var node = Tree[22085];
        var category = scores.Classify(node.Stats[0], node);

        Assert.Equal("Blight", category);
    }

    [Fact]
    public void Strongbox_item_quantity_classifies_as_Strongboxes_not_Quantity()
    {
        var scores = Scores();
        var node = Tree[20440];
        Assert.Equal("Strongboxes", scores.Classify(node.Stats[0], node));
    }

    [Fact]
    public void Strongbox_item_quantity_has_no_prize_when_only_Quantity_is_chased()
    {
        var profile = new SolveProfile
        {
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Quantity & Rarity"] = 15,
            },
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        Assert.Equal(0d, prize.PrizeOf(20440));
    }

    [Fact]
    public void Synthesis_monster_pack_size_classifies_as_Synthesis_not_Pack_Size()
    {
        var scores = Scores();
        var node = Tree[25963];
        Assert.Equal("Synthesis", scores.Classify(node.Stats[0], node));
    }

    [Fact]
    public void Abyss_monster_count_classifies_as_Abyss_not_Pack_Size()
    {
        var scores = Scores();
        var node = Tree[30611];
        Assert.Equal("Abyss", scores.Classify(node.Stats[0], node));
    }

    [Fact]
    public void Generic_map_pack_size_still_classifies_as_Pack_Size()
    {
        var scores = Scores();
        var node = Tree[58167];
        Assert.Equal("Pack Size", scores.Classify(node.Stats[0], node));
    }

    [Fact]
    public void Unchased_Abyss_forbidden_when_Pack_Size_is_chased()
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
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var solution = RouteSolver.Solve(Tree, prize, profile);

        Assert.Contains(30611, prize.ForbiddenIds);
        Assert.DoesNotContain(30611, solution.Nodes);
    }

    [Fact]
    public void Blocking_Synthesis_hard_forbids_synthesis_pack_size_nodes()
    {
        var profile = new SolveProfile
        {
            Budget = 138,
            TimeLimitMs = 100,
            Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pack Size"] = 15,
            },
            ExcludeMechanics = ["Synthesis"],
        };

        var prize = PrizeModel.Build(Tree, Scores(), profile);
        var solution = RouteSolver.Solve(Tree, prize, profile);

        Assert.Contains(25963, prize.ForbiddenIds);
        Assert.DoesNotContain(25963, solution.Nodes);
    }
}
