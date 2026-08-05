using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Solving;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public sealed class BreachRitualReproTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private static readonly string[] Chase =
    [
        "Beyond", "Breach", "Map Modifiers", "Monster Difficulty", "Pack Size",
        "Quantity & Rarity", "Ritual", "The Eater of Worlds",
    ];

    private static readonly string[] Block =
    [
        "Abyss", "Atlas Memories", "Bestiary", "Betrayal", "Blight", "Conquerors", "Currency",
        "Delirium", "Delve", "Divination Cards", "Essence", "Expedition", "Experience",
        "Extra Content", "Gateways", "General", "Harvest", "Heist", "Incursion", "Labyrinth",
        "Legion", "Map Sustain", "Maps", "Mercenaries", "Rogue Exiles", "Scarabs",
        "Settlers of Kalguur", "Shrines", "Strongboxes", "Synthesis", "The Maven",
        "The Searing Exarch", "The Shaper and Elder", "Torment", "Ultimatum",
    ];

    private SolveProfile BreachRitualProfile()
    {
        SpecializationCatalog.CollectMarks(
            Tree,
            new Dictionary<string, string> { ["breach-encounter"] = "hives" },
            out var require,
            out var forbid);

        var weights = Chase.ToDictionary(c => c, _ => 10d, StringComparer.OrdinalIgnoreCase);
        return new SolveProfile
        {
            Budget = 138,
            TimeLimitMs = 100,
            Weights = weights,
            ExcludeMechanics = Block.ToList(),
            Require = require.Select(id => id.ToString()).ToList(),
            Forbid = forbid.Select(id => id.ToString()).ToList(),
        };
    }

    [Fact]
    public void Breach_Ritual_profile_solves_without_infeasibility()
    {
        var profile = BreachRitualProfile();
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        var prize = PrizeModel.Build(Tree, scores, profile);

        var solution = RouteSolver.Solve(Tree, prize, profile);
        Assert.NotEmpty(solution.Nodes);
        Assert.Contains(12551, solution.Nodes);
    }
}
