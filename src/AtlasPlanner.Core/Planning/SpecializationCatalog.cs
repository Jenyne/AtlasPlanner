using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Planning;

/// <summary>How a specialization group resolves when the user picks an option.</summary>
public enum SpecializationKind
{
    /// <summary>Pick one node; all other options' nodes become Never-take.</summary>
    ExclusivePick,

    /// <summary>
    /// Prefer a facet: forbid nodes that hurt it; leave nodes that hurt other facets free for the solver.
    /// </summary>
    SpecializeBias,
}

/// <summary>One selectable lane inside a specialization group.</summary>
public sealed class SpecializationOption
{
    public required string Id { get; init; }
    public required string Label { get; init; }

    /// <summary>For ExclusivePick: the node to Must-take. For SpecializeBias: unused (empty).</summary>
    public IReadOnlyList<int> TakeNodeIds { get; init; } = [];

    /// <summary>Nodes that should be Never-take when this option is chosen.</summary>
    public IReadOnlyList<int> ForbidNodeIds { get; init; } = [];

    /// <summary>
    /// Stat/name phrases belonging to the rival lane. Every node matching one of these is
    /// Never-take, so picking Hives also bans the Unstable support nodes, not just the keystone.
    /// </summary>
    public IReadOnlyList<string> ForbidStatPhrases { get; init; } = [];

    /// <summary>Whole mechanic regions to Never-take when this option is chosen.</summary>
    public IReadOnlyList<string> ForbidCategories { get; init; } = [];
}

/// <summary>A promptable conflict/specialization cluster tied to a chased mechanic.</summary>
public sealed class SpecializationGroup
{
    public required string Id { get; init; }
    public required string Mechanic { get; init; }
    public required string Prompt { get; init; }
    public required SpecializationKind Kind { get; init; }
    public required IReadOnlyList<SpecializationOption> Options { get; init; }

    /// <summary>When set, this group only prompts/applies if the parent group has <see cref="ParentOptionId"/>.</summary>
    public string? ParentGroupId { get; init; }

    public string? ParentOptionId { get; init; }
}

/// <summary>Built-in exclusive / bias groups for PoE 3.29 atlas notables.</summary>
public static class SpecializationCatalog
{
    // Harvest smalls that reduce a plant color.
    private static readonly int[] NonYellow = [53027, 21927];
    private static readonly int[] NonBlue = [16132, 30336];
    private static readonly int[] NonPurple = [64599, 49222];

    private const int PrimalDrought = 44049; // less Blue
    private const int VividDrought = 65089;  // less Yellow
    private const int WildDrought = 27485;   // less Purple

    /// <summary>Wording that only pays off inside Breach Hives.</summary>
    private static readonly string[] HivePhrases =
    [
        "Hive", "Hiveborn", "Hiveblood", "Flammable Burrow",
        "Wombgift", "Grasping Coffer", "Ailith",
    ];

    /// <summary>Wording that only pays off inside Unstable Breaches.</summary>
    private static readonly string[] UnstablePhrases =
    [
        "Unstable Breach", "Stabilised",
    ];

    private static SpecializationGroup MapInfluenceGroup() => new()
    {
        Id = "map-influence",
        Mechanic = "Maps",
        Prompt = "Map influence to farm (a map can only run one; pick one focus or keep all chased)",
        Kind = SpecializationKind.ExclusivePick,
        Options =
        [
            new SpecializationOption
            {
                Id = "all",
                Label = "All chased influences (mixed maps OK)",
                // Empty ForbidCategories — leave every chased influence wheel available.
            },
            new SpecializationOption
            {
                Id = "conquerors",
                Label = "Conquerors only",
                ForbidCategories =
                [
                    MapInfluenceCategories.ShaperAndElder,
                    MapInfluenceCategories.EaterOfWorlds,
                    MapInfluenceCategories.SearingExarch,
                ],
            },
            new SpecializationOption
            {
                Id = "shaper-elder",
                Label = "The Shaper & Elder only",
                ForbidCategories =
                [
                    MapInfluenceCategories.Conquerors,
                    MapInfluenceCategories.EaterOfWorlds,
                    MapInfluenceCategories.SearingExarch,
                ],
            },
            new SpecializationOption
            {
                Id = "eater",
                Label = "The Eater of Worlds only",
                ForbidCategories =
                [
                    MapInfluenceCategories.Conquerors,
                    MapInfluenceCategories.ShaperAndElder,
                    MapInfluenceCategories.SearingExarch,
                ],
            },
            new SpecializationOption
            {
                Id = "exarch",
                Label = "The Searing Exarch only",
                ForbidCategories =
                [
                    MapInfluenceCategories.Conquerors,
                    MapInfluenceCategories.ShaperAndElder,
                    MapInfluenceCategories.EaterOfWorlds,
                ],
            },
        ],
    };

    private const int EndlessTide = 58043;
    private const int TornVeil = 11966;
    private const int DemonicPower = 28227;
    private const int SwarmingHive = 25892;
    private const int PaleClarion = 37533;
    private const int VoraciousThrong = 25151;

    /// <summary>Boss-focus Beyond nodes; mutually exclusive with Endless Tide.</summary>
    private static readonly int[] BeyondBossNotables = [SwarmingHive, PaleClarion, VoraciousThrong];

    private static readonly string[] BeyondBossPhrases =
    [
        "spawn a Unique Boss",
        "Gain Demonic Power",
        "followers of K'tash",
        "followers of Beidat",
        "followers of Ghorr",
    ];

    public static IReadOnlyList<SpecializationGroup> Default { get; } =
    [
        new SpecializationGroup
        {
            Id = "beyond-encounter",
            Mechanic = "Beyond",
            Prompt = "Beyond farming style",
            Kind = SpecializationKind.ExclusivePick,
            Options =
            [
                new SpecializationOption
                {
                    Id = "endless",
                    Label = "Endless Tide (no Unique Bosses)",
                    TakeNodeIds = [EndlessTide],
                    ForbidNodeIds = [TornVeil, DemonicPower, ..BeyondBossNotables],
                    ForbidStatPhrases = BeyondBossPhrases,
                },
                new SpecializationOption
                {
                    Id = "bosses",
                    Label = "Boss focus (spawn & farm Unique Bosses)",
                    TakeNodeIds = [TornVeil, DemonicPower],
                    ForbidNodeIds = [EndlessTide],
                    ForbidStatPhrases = ["cannot spawn Unique Bosses"],
                },
            ],
        },
        new SpecializationGroup
        {
            Id = "beyond-boss",
            Mechanic = "Beyond",
            Prompt = "Beyond boss to specialize",
            Kind = SpecializationKind.ExclusivePick,
            ParentGroupId = "beyond-encounter",
            ParentOptionId = "bosses",
            Options =
            [
                new SpecializationOption
                {
                    Id = "ktash",
                    Label = "K'tash (Swarming Hive — Divination Cards)",
                    TakeNodeIds = [SwarmingHive],
                    ForbidNodeIds = [PaleClarion, VoraciousThrong],
                    ForbidStatPhrases = ["followers of Beidat", "followers of Ghorr"],
                },
                new SpecializationOption
                {
                    Id = "beidat",
                    Label = "Beidat (Pale Clarion — Currency)",
                    TakeNodeIds = [PaleClarion],
                    ForbidNodeIds = [SwarmingHive, VoraciousThrong],
                    ForbidStatPhrases = ["followers of K'tash", "followers of Ghorr"],
                },
                new SpecializationOption
                {
                    Id = "ghorr",
                    Label = "Ghorr (Voracious Throng — Uniques)",
                    TakeNodeIds = [VoraciousThrong],
                    ForbidNodeIds = [SwarmingHive, PaleClarion],
                    ForbidStatPhrases = ["followers of K'tash", "followers of Beidat"],
                },
            ],
        },
        new SpecializationGroup
        {
            Id = "breach-encounter",
            Mechanic = "Breach",
            Prompt = "Breach encounter type",
            Kind = SpecializationKind.ExclusivePick,
            Options =
            [
                new SpecializationOption
                {
                    Id = "hives",
                    Label = "Hives (Dimensional Foothold)",
                    TakeNodeIds = [12551],
                    ForbidNodeIds = [21908],
                    ForbidStatPhrases = UnstablePhrases,
                },
                new SpecializationOption
                {
                    Id = "unstable",
                    Label = "Unstable Breaches (Enemy at the Gates)",
                    TakeNodeIds = [21908],
                    ForbidNodeIds = [12551],
                    ForbidStatPhrases = HivePhrases,
                },
            ],
        },
        new SpecializationGroup
        {
            Id = "merc-house",
            Mechanic = "Mercenaries",
            Prompt = "Mercenary house",
            Kind = SpecializationKind.ExclusivePick,
            Options =
            [
                new SpecializationOption
                {
                    Id = "none",
                    Label = "No house preference (skip all house notables)",
                    ForbidNodeIds = [21485, 62710, 31314],
                    ForbidStatPhrases = ["House Azadi", "House Cyaxan", "House Keita"],
                },
                new SpecializationOption
                {
                    Id = "azadi",
                    Label = "House Azadi",
                    TakeNodeIds = [21485],
                    ForbidNodeIds = [62710, 31314],
                    ForbidStatPhrases = ["House Cyaxan", "House Keita"],
                },
                new SpecializationOption
                {
                    Id = "cyaxan",
                    Label = "House Cyaxan",
                    TakeNodeIds = [62710],
                    ForbidNodeIds = [21485, 31314],
                    ForbidStatPhrases = ["House Azadi", "House Keita"],
                },
                new SpecializationOption
                {
                    Id = "keitan",
                    Label = "House Keitan",
                    TakeNodeIds = [31314],
                    ForbidNodeIds = [21485, 62710],
                    ForbidStatPhrases = ["House Azadi", "House Cyaxan"],
                },
            ],
        },
        new SpecializationGroup
        {
            Id = "merc-attribute",
            Mechanic = "Mercenaries",
            Prompt = "Mercenary attribute",
            Kind = SpecializationKind.SpecializeBias,
            Options =
            [
                new SpecializationOption
                {
                    Id = "none",
                    Label = "No attribute preference (keep all / skip cuts)",
                },
                // Prefer STR → forbid less-STR; allow less-INT / less-DEX.
                new SpecializationOption
                {
                    Id = "strength",
                    Label = "Strength",
                    ForbidNodeIds = [40595], // Absent Warriors
                },
                new SpecializationOption
                {
                    Id = "intelligence",
                    Label = "Intelligence",
                    ForbidNodeIds = [15007], // Education Cuts
                },
                new SpecializationOption
                {
                    Id = "dexterity",
                    Label = "Dexterity",
                    ForbidNodeIds = [24912], // Skills Shortage
                },
            ],
        },
        new SpecializationGroup
        {
            Id = "harvest-color",
            Mechanic = "Harvest",
            Prompt = "Harvest plant color",
            Kind = SpecializationKind.SpecializeBias,
            Options =
            [
                new SpecializationOption
                {
                    Id = "yellow",
                    Label = "Yellow",
                    ForbidNodeIds = Concat(VividDrought, NonYellow),
                    TakeNodeIds = [PrimalDrought, WildDrought],
                },
                new SpecializationOption
                {
                    Id = "blue",
                    Label = "Blue",
                    ForbidNodeIds = Concat(PrimalDrought, NonBlue),
                    TakeNodeIds = [VividDrought, WildDrought],
                },
                new SpecializationOption
                {
                    Id = "purple",
                    Label = "Purple",
                    ForbidNodeIds = Concat(WildDrought, NonPurple),
                    TakeNodeIds = [PrimalDrought, VividDrought],
                },
            ],
        },
        new SpecializationGroup
        {
            Id = "memories-incarnation",
            Mechanic = "Atlas Memories",
            Prompt = "Memory incarnation to avoid less",
            Kind = SpecializationKind.SpecializeBias,
            Options =
            [
                // Prefer Fear path → forbid reduced-Fear; allow the other two cuts.
                new SpecializationOption
                {
                    Id = "fear",
                    Label = "Toward Fear (avoid Neglect & Dread cuts less)",
                    ForbidNodeIds = [5723], // Educated Upbringing — less Fear
                    TakeNodeIds = [58502, 40503],
                },
                new SpecializationOption
                {
                    Id = "neglect",
                    Label = "Toward Neglect",
                    ForbidNodeIds = [58502],
                    TakeNodeIds = [5723, 40503],
                },
                new SpecializationOption
                {
                    Id = "dread",
                    Label = "Toward Dread",
                    ForbidNodeIds = [40503],
                    TakeNodeIds = [5723, 58502],
                },
            ],
        },
        MapInfluenceGroup(),
        MapTierGroup(),
    ];

    private const string MapInfluenceGroupId = "map-influence";
    private const string MapTierGroupId = "map-tiers";

    private static SpecializationGroup MapTierGroup() => new()
    {
        Id = MapTierGroupId,
        Mechanic = "Map Sustain",
        Prompt = "Map sustain focus",
        Kind = SpecializationKind.ExclusivePick,
        Options =
        [
            new SpecializationOption
            {
                Id = "higher-tiers",
                Label = "Higher map tiers (Shaping the Mountains / Skies / World)",
                TakeNodeIds = [ShapingTheMountains, ShapingTheSkies, ShapingTheWorld],
            },
            new SpecializationOption
            {
                Id = "quantity",
                Label = "Map quantity (skip tier-upgrade clusters)",
                ForbidNodeIds = [ShapingTheMountains, ShapingTheSkies, ShapingTheWorld],
                ForbidStatPhrases = ["tier higher"],
            },
            new SpecializationOption
            {
                Id = "either",
                Label = "No preference (solver decides)",
            },
        ],
    };

    private const int ShapingTheMountains = 24609;
    private const int ShapingTheSkies = 35608;
    private const int ShapingTheWorld = 61358;

    /// <summary>Mechanic-tied groups plus map-influence when multiple influences are chased.</summary>
    public static IReadOnlyList<SpecializationGroup> ForSolve(
        IEnumerable<string> chasedMechanics,
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyDictionary<string, string> existingChoices)
    {
        var chased = chasedMechanics.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = ForChasedMechanics(chased, existingChoices).ToList();

        if (MapInfluenceCategories.CountChased(weights) > 1
            && !existingChoices.ContainsKey(MapInfluenceGroupId)
            && list.All(g => g.Id != MapInfluenceGroupId))
        {
            list.Add(MapInfluenceGroup());
        }

        // Higher-tier shaping lives under Maps / Map Sustain — prompt for either chase.
        if (!existingChoices.ContainsKey(MapTierGroupId)
            && (chased.Contains("Map Sustain") || chased.Contains("Maps"))
            && list.All(g => g.Id != MapTierGroupId))
        {
            list.Add(MapTierGroup());
        }

        return list;
    }

    /// <summary>Meticulous Appraiser — soft-banned by default in the GUI unless Must-taken.</summary>
    public const int MeticulousAppraiserId = 65205;

    /// <summary>Monster damage/life tradeoff keystones — opt-in only, like Meticulous Appraiser.</summary>
    public const int DanceOfDestructionId = 36386;

    public const int WellspringOfCreationId = 2493;

    /// <summary>Mercenary wager gold tradeoff — only if the player wants High Stakes gambling.</summary>
    public const int HighStakesId = 46212;

    /// <summary>
    /// Nodes soft-banned until the user Must-takes them by name or id. Includes optional keystones,
    /// High Stakes, and the Trarthan Vapours combat wheel (Onslaught / CDR / auras).
    /// </summary>
    public static IReadOnlyList<int> DefaultSoftBannedKeystones { get; } =
    [
        MeticulousAppraiserId,
        DanceOfDestructionId,
        WellspringOfCreationId,
        HighStakesId,
        ..TrarthanVapoursCluster.CombatNodeIds,
    ];

    /// <summary>
    /// Groups that are not driven by a single chased mechanic name — they are added in
    /// <see cref="ForSolve"/> from multi-influence / Maps+Map Sustain rules instead.
    /// </summary>
    private static readonly HashSet<string> SolveOnlyGroupIds = new(StringComparer.OrdinalIgnoreCase)
    {
        MapInfluenceGroupId,
        MapTierGroupId,
    };

    public static IReadOnlyList<SpecializationGroup> ForChasedMechanics(
        IEnumerable<string> chasedMechanics,
        IReadOnlyDictionary<string, string> existingChoices)
    {
        var chased = new HashSet<string>(chasedMechanics, StringComparer.OrdinalIgnoreCase);
        return Default
            .Where(group => !SolveOnlyGroupIds.Contains(group.Id))
            .Where(group => chased.Contains(group.Mechanic) && !existingChoices.ContainsKey(group.Id))
            .Where(group => IsActive(group, existingChoices))
            .ToArray();
    }

    private static bool IsActive(SpecializationGroup group, IReadOnlyDictionary<string, string> choices)
    {
        if (group.ParentGroupId is not { } parentId || group.ParentOptionId is not { } parentOption)
            return true;

        return choices.TryGetValue(parentId, out var chosen)
            && chosen.Equals(parentOption, StringComparison.OrdinalIgnoreCase);
    }

    public static SpecializationOption? FindOption(string groupId, string optionId) =>
        Default.FirstOrDefault(g => g.Id == groupId)?.Options
            .FirstOrDefault(o => string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolves stored choices into Must-take / Never-take node ids.</summary>
    public static void CollectMarks(
        IReadOnlyDictionary<string, string> choices,
        out HashSet<int> require,
        out HashSet<int> forbid)
    {
        require = [];
        forbid = [];

        foreach (var group in Default)
        {
            if (!choices.TryGetValue(group.Id, out var optionId))
                continue;
            if (!IsActive(group, choices))
                continue;

            var option = FindOption(group.Id, optionId);
            if (option is null)
                continue;

            foreach (var id in option.TakeNodeIds)
                require.Add(id);
            foreach (var id in option.ForbidNodeIds)
                forbid.Add(id);
        }

        // Must-take wins when the same node appears on both sides.
        forbid.ExceptWith(require);
    }

    /// <summary>
    /// Resolves stored choices against the tree, so a lane pick also bans every node whose text
    /// only serves the rival lane (all the Unstable Breach smalls when Hives is chosen, etc.).
    /// </summary>
    public static void CollectMarks(
        AtlasTree tree,
        IReadOnlyDictionary<string, string> choices,
        out HashSet<int> require,
        out HashSet<int> forbid)
    {
        CollectMarks(choices, out require, out forbid);

        var activeOptions = Default
            .Where(group => choices.TryGetValue(group.Id, out _) && IsActive(group, choices))
            .Select(group => FindOption(group.Id, choices[group.Id]))
            .Where(option => option is not null)
            .Cast<SpecializationOption>()
            .ToArray();

        var phrases = activeOptions
            .SelectMany(option => option.ForbidStatPhrases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var categories = activeOptions
            .SelectMany(option => option.ForbidCategories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (phrases.Length == 0 && categories.Length == 0)
        {
            forbid.ExceptWith(require);
            return;
        }

        foreach (var node in tree.Nodes.Values)
        {
            if (!node.IsAllocatable || require.Contains(node.Id))
                continue;

            if (phrases.Length > 0 && MatchesAny(node, phrases))
                forbid.Add(node.Id);
            else if (categories.Any(category =>
                node.Region.Equals(category, StringComparison.OrdinalIgnoreCase)))
                forbid.Add(node.Id);
        }

        forbid.ExceptWith(require);
    }

    private static bool MatchesAny(AtlasNode node, IReadOnlyList<string> phrases)
    {
        foreach (var phrase in phrases)
        {
            if (node.Name.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var stat in node.Stats)
            {
                if (stat.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static int[] Concat(int head, params int[][] rest)
    {
        var list = new List<int> { head };
        foreach (var chunk in rest)
            list.AddRange(chunk);
        return list.ToArray();
    }
}
