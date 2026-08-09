using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Stats;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public class ScoreTableTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    private ScoreTable Scores()
    {
        var scores = ScoreTable.Default();
        scores.PrepareFor(Tree);
        return scores;
    }

    [Fact]
    public void A_scarab_stat_on_a_betrayal_node_classifies_as_a_scarab_stat()
    {
        var node = Tree.Nodes.Values.First(n =>
            n.Region == "Betrayal" &&
            n.Stats.Any(s => s.Text.Contains("Betrayal Scarabs")));

        var stat = node.Stats.First(s => s.Text.Contains("Betrayal Scarabs"));

        Assert.Equal("Scarabs", Scores().Classify(stat, node));
    }

    [Fact]
    public void A_generic_stat_falls_back_to_the_nodes_region()
    {
        var node = Tree.Nodes.Values.First(n =>
            n.Region == "Bestiary" &&
            n.Stats.Any(s => s.Text.Contains("Einhar")));

        var stat = node.Stats.First(s => s.Text.Contains("Einhar"));

        Assert.Equal("Bestiary", Scores().Classify(stat, node));
    }

    [Fact]
    public void A_regionless_node_is_classified_from_mechanic_words_in_its_stat_text()
    {
        var dimensionalBarrier = Tree.Nodes.Values.Single(n => n.Name == "Dimensional Barrier");
        var stat = dimensionalBarrier.ExclusionStat!;

        Assert.Equal(string.Empty, dimensionalBarrier.Region);
        Assert.Equal("Breach", Scores().Classify(stat, dimensionalBarrier));
    }

    [Fact]
    public void Template_overrides_beat_every_keyword_rule()
    {
        var node = Tree.Nodes.Values.First(n => n.Stats.Any(s => s.Text.Contains("Scarab")));
        var stat = node.Stats.First(s => s.Text.Contains("Scarab"));

        var scores = Scores();
        scores.TemplateOverrides[stat.Template] = "Bespoke";

        Assert.Equal("Bespoke", scores.Classify(stat, node));
    }

    [Fact]
    public void The_generic_Maps_region_is_not_used_as_a_fallback_keyword()
    {
        var scores = Scores();
        var node = Tree.Nodes.Values.First(n => n.Region.Length == 0 && n.Stats.Count > 0);
        var stat = StatText.Parse("Something entirely unrelated happens in your Maps");

        Assert.NotEqual("Maps", scores.Classify(stat, node));
    }

    [Fact]
    public void Almost_every_stat_line_lands_in_a_real_category()
    {
        var scores = Scores();

        var uncategorised = Tree.Nodes.Values
            .SelectMany(node => node.Stats.Select(stat => (node, stat)))
            .Count(pair => scores.Classify(pair.stat, pair.node) == ScoreTable.Uncategorised);

        Assert.InRange(uncategorised, 0, 10);
    }

    [Fact]
    public void An_encounter_chance_stat_is_worth_a_multiple_of_its_face_value()
    {
        var scores = Scores();

        var gate = StatText.Parse("Your Maps have +10% chance to contain Breaches");
        var inhabited = StatText.Parse("Your Maps have +10% chance to be inhabited by a Mercenary");

        Assert.True(scores.Emphasis(gate) > 1d);
        Assert.True(scores.Emphasis(inhabited) > 1d);
    }

    [Fact]
    public void Quantity_of_Items_is_emphasized_five_to_one_over_Rarity()
    {
        var scores = Scores();
        var quantity = StatText.Parse("1% increased Quantity of Items found in your Maps");
        var rarity = StatText.Parse("2% increased Rarity of Items found in your Maps");

        Assert.Equal(5d, scores.Emphasis(quantity));
        Assert.Equal(1d, scores.Emphasis(rarity));
    }

    [Fact]
    public void Downstream_chances_and_off_switches_are_left_at_face_value()
    {
        var scores = Scores();

        // Not a gate on the encounter itself, just on something inside one.
        var downstream = StatText.Parse("Blight Chests in your Maps have 10% more chance to contain Blighted Maps");
        var additional = StatText.Parse("Your Maps have a 25% chance to contain an additional Rogue Exile");

        // Removing a mechanic is priced by the profile's exclusion weight, not by emphasis.
        var offSwitch = StatText.Parse("Your Maps have no chance to contain Breaches");

        Assert.Equal(1d, scores.Emphasis(downstream));
        Assert.Equal(1d, scores.Emphasis(additional));
        Assert.Equal(1d, scores.Emphasis(offSwitch));
    }

    [Fact]
    public void Overlapping_emphasis_rules_take_the_largest_rather_than_compounding()
    {
        var scores = Scores();
        scores.EmphasisRules.Add(new EmphasisRule("Your Maps have +#% chance to contain Breaches", 3d));

        var stat = StatText.Parse("Your Maps have +10% chance to contain Breaches");

        Assert.Equal(5d, scores.Emphasis(stat));
    }

    [Fact]
    public void A_saved_table_round_trips_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atlasscores-{Guid.NewGuid():N}.json");
        var original = ScoreTable.Default();
        original.TemplateOverrides["#% test"] = "Testing";
        original.IgnoredTemplates.Add("#% ignored");

        try
        {
            original.Save(path);
            var reloaded = ScoreTable.Load(path);

            Assert.Equal(original.KeywordRules.Count, reloaded.KeywordRules.Count);
            Assert.Equal("Testing", reloaded.TemplateOverrides["#% test"]);
            Assert.Contains("#% ignored", reloaded.IgnoredTemplates);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
