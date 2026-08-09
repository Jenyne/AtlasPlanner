using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasPlanner.Core.Stats;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Categorization;

/// <summary>
/// Assigns a category to each stat line so the tally can be grouped and the solver can be steered.
/// </summary>
/// <remarks>
/// Classification runs stat-first, node-second on purpose: a Betrayal-region node granting
/// "Scarabs dropped in your Maps have 10% increased chance to be Betrayal Scarabs" is a Scarab stat,
/// not a Betrayal one, and weighting Scarabs should pick it up.
/// </remarks>
public sealed class ScoreTable
{
    public const string Uncategorised = "General";

    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    /// <summary>
    /// Ordered; the first rule whose keyword appears in the stat template wins. Order matters, so
    /// narrow rules must come before broad ones.
    /// </summary>
    [JsonPropertyName("keywordRules")]
    public List<KeywordRule> KeywordRules { get; set; } = [];

    /// <summary>Exact template to category, overriding every keyword rule.</summary>
    [JsonPropertyName("templateOverrides")]
    public Dictionary<string, string> TemplateOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Templates that should never be scored or summed, e.g. pure downside text on keystones that
    /// is better captured by a hand-set node weight.
    /// </summary>
    [JsonPropertyName("ignoredTemplates")]
    public HashSet<string> IgnoredTemplates { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Phrases that mean a stat works against its own category, so that weighting Scarabs highly does
    /// not make "Scarabs cannot be found in Your Maps" look like a prize. Matched against the
    /// template, which is why the numeric forms are written with <c>#</c>.
    /// </summary>
    [JsonPropertyName("downsidePhrases")]
    public List<string> DownsidePhrases { get; set; } =
        ["cannot", "no chance to contain", "no longer", "#% reduced", "#% less", "#% fewer"];

    /// <summary>
    /// Phrases that switch a category off entirely. Deliberately narrower than
    /// <see cref="DownsidePhrases"/>: "Scarabs cannot be found in Your Maps" removes the mechanic,
    /// whereas "Scarabs found in your Maps cannot be Breach Scarabs" only rules out one variety and
    /// is merely a downside.
    /// </summary>
    [JsonPropertyName("nullifyingPhrases")]
    public List<string> NullifyingPhrases { get; set; } =
        ["no chance to contain", "cannot be found"];

    /// <summary>
    /// Stats that decide whether an encounter shows up at all, and how much to multiply their value
    /// by. Matched against the template, so the phrases carry the <c>#</c> and the leading sign.
    /// </summary>
    /// <remarks>
    /// A percent of encounter chance is not worth the same as a percent of anything the encounter
    /// then drops: getting to 100% is usually the whole point of a farming tree, and until you are
    /// there every other node in that mechanic is only paying out on a fraction of your maps. The
    /// phrases are deliberately anchored to "Your Maps have +", which is what the map-level gates
    /// read, so the many "chance to contain an additional ..." lines are left alone.
    /// Quantity of Items is weighted 5× Rarity of Items — PoE map loot values quant far above rarity,
    /// and rarity smalls print higher face values (2% vs 1%) that would otherwise win the circle.
    /// </remarks>
    [JsonPropertyName("emphasisRules")]
    public List<EmphasisRule> EmphasisRules { get; set; } =
    [
        new("Your Maps have +#% chance to contain", 5d),
        new("Your Maps have +#% chance to be inhabited", 5d),
        new("Quantity of Items", 5d),
    ];

    /// <summary>
    /// Region names too generic to be used as a last-resort keyword. "Maps" appears in nearly every
    /// atlas stat line, so matching on it would swallow the whole tree.
    /// </summary>
    [JsonPropertyName("regionVocabularyExclusions")]
    public HashSet<string> RegionVocabularyExclusions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { "Maps", "Atlas Memories" };

    private string[] _regionVocabulary = [];

    /// <summary>
    /// Caches the tree's region names for the last-resort match in <see cref="Classify"/>. Call once
    /// after loading; classification still works without it, just with a larger General bucket.
    /// </summary>
    public void PrepareFor(AtlasTree tree)
    {
        _regionVocabulary = tree.Masteries
            .Select(m => m.Name)
            .Where(name => name.Length > 0 && !RegionVocabularyExclusions.Contains(name))
            .Distinct(StringComparer.Ordinal)
            // Longest first so "The Searing Exarch" is preferred over any shorter substring match.
            .OrderByDescending(name => name.Length)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public string Classify(ParsedStat stat, AtlasNode node)
    {
        if (TemplateOverrides.TryGetValue(stat.Template, out var mapped))
            return mapped;

        foreach (var rule in KeywordRules)
        {
            if (rule.Matches(stat.Template))
                return rule.Category;
        }

        if (!string.IsNullOrEmpty(node.Region))
            return node.Region;

        // Nodes in groups without a mastery label (keystones, connectors, the exclusion notables)
        // still name their mechanic in the stat text.
        foreach (var region in _regionVocabulary)
        {
            if (stat.Template.Contains(region, StringComparison.OrdinalIgnoreCase))
                return region;
        }

        return Uncategorised;
    }

    public bool IsIgnored(ParsedStat stat) => IgnoredTemplates.Contains(stat.Template);

    /// <summary>
    /// True when the stat reads as working against its category, so its contribution should be scored
    /// with the sign flipped.
    /// </summary>
    public bool IsDownside(ParsedStat stat) =>
        DownsidePhrases.Any(phrase => stat.Template.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the stat switches its category off outright rather than reducing it by some amount.
    /// </summary>
    /// <remarks>
    /// The distinction matters because a linear score cannot express it. "10% reduced Scarabs" is
    /// worth a negative number, but "Scarabs cannot be found in Your Maps" makes every other Scarab
    /// node in the route worthless, and no finite penalty models that correctly. The solver treats
    /// these as constraints instead.
    /// </remarks>
    public bool IsNullifying(ParsedStat stat) =>
        stat.Numbers.Count == 0 &&
        NullifyingPhrases.Any(phrase => stat.Template.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How much to scale a stat's magnitude by before weighting it. One for ordinary stats; more for
    /// the encounter gates named in <see cref="EmphasisRules"/>. The largest matching rule wins, so
    /// overlapping phrases cannot compound into an absurd number.
    /// </summary>
    public double Emphasis(ParsedStat stat)
    {
        var multiplier = 1d;
        foreach (var rule in EmphasisRules)
        {
            if (rule.Multiplier > multiplier && rule.Matches(stat.Template))
                multiplier = rule.Multiplier;
        }

        return multiplier;
    }

    /// <summary>Every category that can come out of <see cref="Classify"/> for this tree.</summary>
    public IReadOnlyList<string> KnownCategories(AtlasTree tree)
    {
        var categories = new SortedSet<string>(StringComparer.Ordinal) { Uncategorised };

        foreach (var rule in KeywordRules)
            categories.Add(rule.Category);
        foreach (var category in TemplateOverrides.Values)
            categories.Add(category);
        foreach (var mastery in tree.Masteries)
            categories.Add(mastery.Name);

        return categories.ToArray();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ScoreTable Load(string path) =>
        JsonSerializer.Deserialize<ScoreTable>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"'{path}' deserialised to null.");

    /// <summary>Loads the table if present, otherwise falls back to <see cref="Default"/>.</summary>
    public static ScoreTable LoadOrDefault(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? Load(path) : Default();

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>
    /// Built-in rules for stats that cut across regions. Region names supply everything else, so
    /// this list only needs to grow when a stat family spans multiple mechanics.
    /// </summary>
    public static ScoreTable Default() => new()
    {
        KeywordRules =
        [
            new KeywordRule("Scarabs", ["Scarab"]),
            new KeywordRule("Atlas Points", ["Atlas Passive Skill Point"]),
            new KeywordRule("Gateways", ["Connects to"]),
            new KeywordRule("Extra Content", ["other Extra Content"]),
            new KeywordRule("Atlas Memories", ["Memory Tear", "Memory Thread", "Memory Strand", "Memory Influence", "Incarnation of"]),
            new KeywordRule("Blight", ["Blight", "Blighted"]),
            new KeywordRule("Conquerors", ["Conqueror", "Baran", "Veritania", "Al-Hezmin", "Drox"]),
            new KeywordRule("The Shaper and Elder", ["The Shaper", "The Elder", "Shaper Guardian", "Elder Guardian", "Shaper Influenced", "Elder Influenced"]),
            new KeywordRule("The Eater of Worlds", ["The Eater of Worlds", "Eldritch Ichor"]),
            new KeywordRule("The Searing Exarch", ["The Searing Exarch", "Eldritch Ember"]),
            new KeywordRule("Synthesis", ["Synthesis", "Synthesised"]),

            // Mechanics whose stat text never says the region's own name — before generic Pack Size.
            new KeywordRule("The Maven", ["Maven", "Witnessed", "Witnessing", "Atlas Bosses"]),
            new KeywordRule("Breach", ["Hive", "Hiveborn", "Ailith"]),
            new KeywordRule("Abyss", ["Abyss", "Abysses", "Pit", "Abyssal"]),
            new KeywordRule("Harvest", ["Crop", "Lifeforce", "Plants", "Sacred Grove"]),
            new KeywordRule("Legion", ["Timeless Splinter", "Karui army", "Maraketh army", "Templar army", "Eternal Empire army", "include a General"]),
            new KeywordRule("Heist", ["Smuggler's Cache", "Rogue's Marker", "Contract", "Blueprint"]),
            new KeywordRule("Delve", ["Sulphite", "Azurite", "Fossil", "Niko", "Voltaxic"]),
            new KeywordRule("Incursion", ["Alva", "Incursion", "Temple"]),
            new KeywordRule("Essence", ["Essence", "Imprisoned Monster"]),
            new KeywordRule("Settlers of Kalguur", ["Ore Deposit", "Kalguuran", "increased Ore"]),
            new KeywordRule("Torment", ["Tormented Spirit", "Possess"]),
            new KeywordRule("Ritual", ["Tribute", "Favours", "Ritual Altar"]),
            new KeywordRule("Ultimatum", ["Rounds", "final Round", "Ultimatum Modifier"]),
            new KeywordRule("Delirium", ["Mirror of Delirium", "Mirrors of Delirium", "Simulacrum"]),
            new KeywordRule("Strongboxes", ["Strongbox", "Strongboxes"]),

            new KeywordRule("Map Sustain", ["Maps found", "Map found", "tier higher", "Map Drops"]),
            new KeywordRule("Quantity & Rarity", ["Quantity of Items", "Rarity of Items"]),
            new KeywordRule("Pack Size", ["Pack Size"]),
            new KeywordRule("Map Modifiers", ["Explicit Modifiers", "additional random Modifier"]),
            new KeywordRule("Divination Cards", ["Divination Card"]),
            new KeywordRule("Currency", ["Currency Item"]),
            new KeywordRule("Experience", ["Experience"]),
            new KeywordRule("Monster Difficulty", ["Monsters in your Maps deal", "Monsters in your Maps have"]),
        ],
    };
}

/// <summary>A template phrase whose stats are worth a multiple of their face value.</summary>
public sealed class EmphasisRule
{
    public EmphasisRule()
    {
    }

    public EmphasisRule(string contains, double multiplier)
    {
        Contains = contains;
        Multiplier = multiplier;
    }

    [JsonPropertyName("contains")] public string Contains { get; set; } = string.Empty;

    [JsonPropertyName("multiplier")] public double Multiplier { get; set; } = 1d;

    public bool Matches(string template) =>
        Contains.Length > 0 && template.Contains(Contains, StringComparison.OrdinalIgnoreCase);
}

public sealed class KeywordRule
{
    public KeywordRule()
    {
    }

    public KeywordRule(string category, IReadOnlyList<string> contains)
    {
        Category = category;
        Contains = [..contains];
    }

    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;

    [JsonPropertyName("contains")] public List<string> Contains { get; set; } = [];

    public bool Matches(string template) =>
        Contains.Any(keyword => template.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
