using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Tests;

[Collection(TreeCollection.Name)]
public sealed class SpecializationCatalogTests(TreeFixture fixture)
{
    private AtlasTree Tree => fixture.Tree;

    [Fact]
    public void Choosing_Endless_Tide_bans_boss_nodes()
    {
        SpecializationCatalog.CollectMarks(
            Tree,
            new Dictionary<string, string> { ["beyond-encounter"] = "endless" },
            out var require,
            out var forbid);

        Assert.Contains(58043, require);
        Assert.Contains(11966, forbid);
        Assert.Contains(25892, forbid);
        Assert.Contains(37533, forbid);
        Assert.Contains(25151, forbid);
        Assert.DoesNotContain(58043, forbid);
    }

    [Fact]
    public void Choosing_boss_focus_requires_torn_veil_and_one_boss_notable()
    {
        SpecializationCatalog.CollectMarks(
            Tree,
            new Dictionary<string, string>
            {
                ["beyond-encounter"] = "bosses",
                ["beyond-boss"] = "ghorr",
            },
            out var require,
            out var forbid);

        Assert.Contains(11966, require);
        Assert.Contains(28227, require);
        Assert.Contains(25151, require);
        Assert.Contains(58043, forbid);
        Assert.Contains(25892, forbid);
        Assert.Contains(37533, forbid);
    }

    [Fact]
    public void Boss_choice_is_ignored_when_Endless_Tide_is_selected()
    {
        SpecializationCatalog.CollectMarks(
            Tree,
            new Dictionary<string, string>
            {
                ["beyond-encounter"] = "endless",
                ["beyond-boss"] = "ghorr",
            },
            out var require,
            out var forbid);

        Assert.Contains(58043, require);
        Assert.DoesNotContain(25151, require);
        Assert.Contains(25151, forbid);
    }

    [Fact]
    public void ForChasedMechanics_shows_boss_prompt_only_after_boss_focus_chosen()
    {
        var none = SpecializationCatalog.ForChasedMechanics(["Beyond"], new Dictionary<string, string>());
        Assert.Contains(none, g => g.Id == "beyond-encounter");
        Assert.DoesNotContain(none, g => g.Id == "beyond-boss");

        var afterBossFocus = SpecializationCatalog.ForChasedMechanics(
            ["Beyond"],
            new Dictionary<string, string> { ["beyond-encounter"] = "bosses" });

        Assert.Contains(afterBossFocus, g => g.Id == "beyond-boss");
        Assert.DoesNotContain(afterBossFocus, g => g.Id == "beyond-encounter");
    }

    [Fact]
    public void Choosing_Hives_bans_every_Unstable_Breach_node()
    {
        SpecializationCatalog.CollectMarks(
            Tree,
            new Dictionary<string, string> { ["breach-encounter"] = "hives" },
            out var require,
            out var forbid);

        Assert.Contains(12551, require);

        var unstableNodes = Tree.Nodes.Values
            .Where(n => n.IsAllocatable && n.Stats.Any(s =>
                s.Text.Contains("Unstable Breach", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.NotEmpty(unstableNodes);
        Assert.All(unstableNodes, node => Assert.Contains(node.Id, forbid));

        // The Hive keystone and its own support nodes stay available.
        Assert.DoesNotContain(12551, forbid);
        var hiveSupport = Tree.Nodes.Values.First(n =>
            n.Stats.Any(s => s.Text.Contains("Flammable Burrow", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(hiveSupport.Id, forbid);
    }

    [Fact]
    public void Choosing_Unstable_bans_hive_support_nodes()
    {
        SpecializationCatalog.CollectMarks(
            Tree,
            new Dictionary<string, string> { ["breach-encounter"] = "unstable" },
            out var require,
            out var forbid);

        Assert.Contains(21908, require);

        var hiveNodes = Tree.Nodes.Values
            .Where(n => n.IsAllocatable && n.Stats.Any(s =>
                s.Text.Contains("Wombgift", StringComparison.OrdinalIgnoreCase)
                || s.Text.Contains("Hiveblood", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.NotEmpty(hiveNodes);
        Assert.All(hiveNodes, node => Assert.Contains(node.Id, forbid));
    }

    [Fact]
    public void Choosing_a_mercenary_house_bans_the_other_houses_nodes()
    {
        SpecializationCatalog.CollectMarks(
            Tree,
            new Dictionary<string, string> { ["merc-house"] = "azadi" },
            out var require,
            out var forbid);

        Assert.Contains(21485, require);
        Assert.Contains(62710, forbid);
        Assert.Contains(31314, forbid);
    }
}

public sealed class SpecializationCatalogDataTests
{
    [Fact]
    public void Default_catalog_covers_beyond_breach_merc_harvest_and_memories()
    {
        var mechanics = SpecializationCatalog.Default
            .Select(group => group.Mechanic)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(["Atlas Memories", "Beyond", "Breach", "Harvest", "Maps", "Mercenaries"], mechanics);
        Assert.Contains(SpecializationCatalog.Default, g => g.Id == "map-influence");
        Assert.Contains(SpecializationCatalog.Default, g => g.Id == "beyond-encounter");
        Assert.Contains(SpecializationCatalog.Default, g => g.Id == "beyond-boss");
        Assert.Contains(SpecializationCatalog.Default, g => g.Id == "breach-encounter");
        Assert.Contains(SpecializationCatalog.Default, g => g.Id == "merc-house");
        Assert.Contains(SpecializationCatalog.Default, g => g.Id == "merc-attribute");
        Assert.Contains(SpecializationCatalog.Default, g => g.Id == "harvest-color");
    }

    [Fact]
    public void ForChasedMechanics_skips_groups_that_already_have_a_choice()
    {
        var unresolved = SpecializationCatalog.ForChasedMechanics(
            ["Breach", "Harvest"],
            new Dictionary<string, string> { ["breach-encounter"] = "hives" });

        Assert.DoesNotContain(unresolved, g => g.Id == "breach-encounter");
        Assert.Contains(unresolved, g => g.Id == "harvest-color");
        Assert.DoesNotContain(unresolved, g => g.Mechanic == "Mercenaries");
    }

    [Fact]
    public void CollectMarks_exclusive_pick_requires_chosen_and_forbids_rivals()
    {
        SpecializationCatalog.CollectMarks(
            new Dictionary<string, string> { ["breach-encounter"] = "hives" },
            out var require,
            out var forbid);

        Assert.Contains(12551, require);
        Assert.Contains(21908, forbid);
        Assert.DoesNotContain(12551, forbid);
    }

    [Fact]
    public void CollectMarks_specialize_bias_forbids_hurt_preferred_and_takes_other_droughts()
    {
        SpecializationCatalog.CollectMarks(
            new Dictionary<string, string> { ["harvest-color"] = "yellow" },
            out var require,
            out var forbid);

        Assert.Contains(65089, forbid); // Vivid Drought
        Assert.Contains(53027, forbid); // non-Yellow small
        Assert.Contains(44049, require); // Primal Drought
        Assert.Contains(27485, require); // Wild Drought
        Assert.DoesNotContain(65089, require);
    }

    [Fact]
    public void Default_soft_banned_keystones_include_damage_life_tradeoffs()
    {
        Assert.Contains(SpecializationCatalog.DanceOfDestructionId, SpecializationCatalog.DefaultSoftBannedKeystones);
        Assert.Contains(SpecializationCatalog.WellspringOfCreationId, SpecializationCatalog.DefaultSoftBannedKeystones);
    }
}
