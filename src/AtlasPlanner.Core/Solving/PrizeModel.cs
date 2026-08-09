using AtlasPlanner.Core.Categorization;
using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Solving;

/// <summary>
/// Turns a <see cref="SolveProfile"/> into a value per node, so the solver optimises a single number
/// while the reasoning behind it stays inspectable.
/// </summary>
public sealed class PrizeModel
{
    private readonly double[] _prizeByIndex;
    private readonly AtlasTree _tree;

    private PrizeModel(
        AtlasTree tree,
        double[] prizeByIndex,
        IReadOnlySet<int> requiredIds,
        IReadOnlySet<int> forbiddenIds,
        IReadOnlyDictionary<string, double> effectiveWeights,
        IReadOnlyList<NullifiedNode> nullified,
        IReadOnlyList<NullifiedNode> zeroedByRequirement,
        IReadOnlyList<string> excludedWithoutOffSwitch,
        AtlasNode? pointGranter)
    {
        _tree = tree;
        _prizeByIndex = prizeByIndex;
        RequiredIds = requiredIds;
        ForbiddenIds = forbiddenIds;
        EffectiveWeights = effectiveWeights;
        Nullified = nullified;
        ZeroedByRequirement = zeroedByRequirement;
        ExcludedWithoutOffSwitch = excludedWithoutOffSwitch;
        PointGranter = pointGranter;
    }

    /// <summary>Nodes the route must contain, after resolving names and mechanic opt-outs.</summary>
    public IReadOnlySet<int> RequiredIds { get; }

    /// <summary>Nodes the route may never contain.</summary>
    public IReadOnlySet<int> ForbiddenIds { get; }

    /// <summary>Weights actually in force, including categories zeroed by an opt-out.</summary>
    public IReadOnlyDictionary<string, double> EffectiveWeights { get; }

    /// <summary>
    /// Nodes ruled out because they switch off a category the profile asks for. Reported rather than
    /// applied silently, since it is a decision made on the user's behalf.
    /// </summary>
    public IReadOnlyList<NullifiedNode> Nullified { get; }

    /// <summary>
    /// Categories dropped to zero because a required node switches them off, so the route does not
    /// keep paying for a mechanic it has just disabled.
    /// </summary>
    public IReadOnlyList<NullifiedNode> ZeroedByRequirement { get; }

    /// <summary>
    /// Blocked mechanics that must stay soft-weighted only. Their keywords or regions span generic
    /// travel nodes (General, Maps, Scarab lines on every wheel), so hard-forbidding them disconnects
    /// the tree. Every other blocked mechanic gets its matching nodes Never-take.
    /// </summary>
    private static readonly HashSet<string> SoftBlockOnlyCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "General",
        "Maps",
        "Map Sustain",
        "Gateways",
        "Extra Content",
        "Experience",
        "Currency",
        "Divination Cards",
        "Rogue Exiles",
        "Scarabs",
        "Shrines",
        "Strongboxes",
        "The Maven",
        "Torment",
    };

    /// <summary>
    /// Blocked mechanics the tree has no off-switch notable for. Encounters can still appear in maps;
    /// the user should hear about it. Broad categories in <see cref="SoftBlockOnlyCategories"/> are
    /// soft-weighted only so the tree stays connected.
    /// </summary>
    public IReadOnlyList<string> ExcludedWithoutOffSwitch { get; }

    /// <summary>The keystone that grants extra atlas points, if the tree still has one.</summary>
    public AtlasNode? PointGranter { get; }

    public double PrizeOfIndex(int index) => _prizeByIndex[index];

    public double PrizeOf(int nodeId) => _prizeByIndex[_tree.IndexOf(nodeId)];

    public double PrizeOf(IEnumerable<int> nodeIds) => nodeIds.Sum(PrizeOf);

    /// <summary>Per-stat breakdown of a node's value, for explaining why it was or wasn't taken.</summary>
    public IReadOnlyList<PrizeContribution> Explain(int nodeId, ScoreTable scores, SolveProfile profile)
    {
        var node = _tree[nodeId];
        var contributions = new List<PrizeContribution>();

        foreach (var stat in node.Stats)
        {
            if (scores.IsIgnored(stat))
                continue;

            var category = scores.Classify(stat, node);
            var weight = EffectiveWeights.GetValueOrDefault(category, 0d);
            var magnitude = (stat.IsSummable ? stat.Value : profile.FlatStatValue) * scores.Emphasis(stat);
            var signed = SignedMagnitude(weight, magnitude, scores.IsDownside(stat));

            contributions.Add(new PrizeContribution
            {
                Stat = stat.Text,
                Category = category,
                Weight = weight,
                Magnitude = signed,
                Value = weight * signed,
            });
        }

        var bias = NodeBias(node, profile);
        if (bias != 0d)
        {
            contributions.Add(new PrizeContribution
            {
                Stat = "(per-node bias from profile)",
                Category = "nodeWeights",
                Weight = 1d,
                Magnitude = bias,
                Value = bias,
            });
        }

        return contributions;
    }

    public static PrizeModel Build(AtlasTree tree, ScoreTable scores, SolveProfile profile)
    {
        var weights = new Dictionary<string, double>(profile.Weights, StringComparer.OrdinalIgnoreCase);
        var required = ResolveNodes(tree, profile.Require, "require").ToHashSet();
        var forbidden = ResolveNodes(tree, profile.Forbid, "forbid").ToHashSet();

        var pointGranter = tree.Nodes.Values
            .Where(node => node.GrantedPoints > 0)
            .OrderByDescending(node => node.GrantedPoints)
            .ThenBy(node => node.Id)
            .FirstOrDefault();

        // Settle the toggle before anything automatic runs, so an explicit choice is never quietly
        // overridden by the nullification rule below.
        if (pointGranter is not null)
        {
            switch (profile.UnwaveringVision)
            {
                case PointGrantChoice.Include:
                    required.Add(pointGranter.Id);
                    break;
                case PointGrantChoice.Exclude:
                    forbidden.Add(pointGranter.Id);
                    break;
            }
        }

        foreach (var region in profile.ForbidRegions)
        {
            foreach (var node in tree.Nodes.Values.Where(n => n.Region.Equals(region, StringComparison.OrdinalIgnoreCase)))
                forbidden.Add(node.Id);
        }

        // Opting out of a mechanic weights against it and buys the off-switch, rather than banning the
        // region outright, which could otherwise cut off the very notable that switches it off. The
        // negative weight does double duty: the mechanic's nodes now cost score, while its "no chance
        // to contain" notable reads as a downside and so scores positively.
        var penalty = -Math.Abs(profile.ExclusionWeight);
        var withoutOffSwitch = new List<string>();
        foreach (var mechanic in profile.ExcludeMechanics)
        {
            weights[mechanic] = penalty;

            var offSwitch = tree.Nodes.Values.FirstOrDefault(node =>
                node.ExclusionStat is { } stat &&
                scores.Classify(stat, node).Equals(mechanic, StringComparison.OrdinalIgnoreCase));

            if (offSwitch is not null)
                required.Add(offSwitch.Id);
            else
                withoutOffSwitch.Add(mechanic);
        }

        LinkedDifficultyWeights.Apply(weights);

        // Hard-forbid blocked mechanics except broad categories that would disconnect the tree.
        // Mechanics with an off-switch: forbid by stat/keyword only so travel through the wheel
        // stays legal. Mechanics with no off-switch also forbid by region (whole cluster).
        var blockedWithoutOffSwitch = withoutOffSwitch.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mechanic in profile.ExcludeMechanics.Where(m => !SoftBlockOnlyCategories.Contains(m)))
        {
            var forbidByRegion = blockedWithoutOffSwitch.Contains(mechanic);

            foreach (var node in tree.Nodes.Values)
            {
                if (required.Contains(node.Id))
                    continue;

                if (NodeMatchesBlockedMechanic(node, scores, mechanic, forbidByRegion))
                    forbidden.Add(node.Id);
            }
        }

        // Mechanic nodes not actively chased are Never-take so generic weights (Pack Size, Map
        // Sustain, …) do not spend points on Abyss pack size, influence wheels, and the like.
        var excludedMechanics = profile.ExcludeMechanics.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var category in tree.Masteries.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (SoftBlockOnlyCategories.Contains(category))
                continue;

            if (weights.GetValueOrDefault(category, 0d) > 0d)
                continue;

            if (excludedMechanics.Contains(category))
                continue;

            var forbidByRegion = MapInfluenceCategories.All.Contains(category);

            foreach (var node in tree.Nodes.Values)
            {
                if (required.Contains(node.Id))
                    continue;

                if (NodeMatchesBlockedMechanic(node, scores, category, forbidByRegion))
                    forbidden.Add(node.Id);
            }
        }

        // Trarthan Vapours wheel: combat nodes are pure downside for farming. Chance smalls on that
        // wheel are only a distant +20% toward 100% inhabit — forbid them unless Mercenaries is
        // chased, and even then score them cheaply so Minor Fiefdoms is preferred first.
        foreach (var id in TrarthanVapoursCluster.CombatNodeIds)
        {
            if (!required.Contains(id))
                forbidden.Add(id);
        }

        var mercenariesWeight = weights.GetValueOrDefault("Mercenaries", 0d);
        if (mercenariesWeight <= 0d)
        {
            foreach (var id in TrarthanVapoursCluster.ChanceNodeIds)
            {
                if (!required.Contains(id))
                    forbidden.Add(id);
            }
        }

        // Anything required that switches a category off takes that category's weight down with it.
        // Otherwise a route that bans scarabs would carry on buying scarab nodes and scoring them.
        var zeroedByRequirement = new List<NullifiedNode>();
        foreach (var id in required)
        {
            foreach (var stat in tree[id].Stats)
            {
                if (scores.IsIgnored(stat) || !scores.IsNullifying(stat))
                    continue;

                var category = scores.Classify(stat, node: tree[id]);
                if (weights.GetValueOrDefault(category, 0d) <= 0d)
                    continue;

                weights[category] = 0d;
                zeroedByRequirement.Add(new NullifiedNode
                {
                    NodeId = id,
                    Name = tree[id].Name,
                    Category = category,
                    Stat = stat.Text,
                });
            }
        }

        // A node that switches off a category the profile still wants cannot be priced, only ruled
        // out. Anything explicitly required is left alone: asking for it is answer enough.
        var nullified = new List<NullifiedNode>();
        foreach (var node in tree.Nodes.Values)
        {
            if (required.Contains(node.Id) || node.Id == tree.StartNodeId)
                continue;

            foreach (var stat in node.Stats)
            {
                if (scores.IsIgnored(stat) || !scores.IsNullifying(stat))
                    continue;

                var category = scores.Classify(stat, node);
                if (weights.GetValueOrDefault(category, 0d) <= 0d)
                    continue;

                forbidden.Add(node.Id);
                nullified.Add(new NullifiedNode { NodeId = node.Id, Name = node.Name, Category = category, Stat = stat.Text });
                break;
            }
        }

        forbidden.Remove(tree.StartNodeId);
        foreach (var id in profile.PreAllocated)
            forbidden.Remove(id);

        var conflicts = required.Intersect(forbidden).ToList();
        if (conflicts.Count > 0)
            throw new InvalidOperationException(
                "These nodes are both required and forbidden: " +
                string.Join(", ", conflicts.Select(id => tree[id].ToString())));

        var prizes = new double[tree.NodeCount];
        foreach (var node in tree.Nodes.Values)
        {
            double total = 0d;
            var scale = TrarthanVapoursCluster.IsChanceNode(node.Id)
                ? TrarthanVapoursCluster.DistantChancePrizeScale
                : 1d;

            foreach (var stat in node.Stats)
            {
                if (scores.IsIgnored(stat))
                    continue;

                var weight = weights.GetValueOrDefault(scores.Classify(stat, node), 0d);
                if (weight == 0d)
                    continue;

                var magnitude = (stat.IsSummable ? stat.Value : profile.FlatStatValue) * scores.Emphasis(stat);
                total += weight * SignedMagnitude(weight, magnitude, scores.IsDownside(stat)) * scale;
            }

            prizes[tree.IndexOf(node.Id)] = total + NodeBias(node, profile);
        }

        return new PrizeModel(
            tree,
            prizes,
            required,
            forbidden,
            weights,
            nullified,
            zeroedByRequirement,
            withoutOffSwitch,
            pointGranter);
    }

    private static double NodeBias(AtlasNode node, SolveProfile profile)
    {
        if (profile.NodeWeights.TryGetValue(node.Id.ToString(), out var byId))
            return byId;

        return node.Name.Length > 0 && profile.NodeWeights.TryGetValue(node.Name, out var byName) ? byName : 0d;
    }

    /// <summary>
    /// When chasing (weight &gt; 0), downsides subtract. When blocking (weight &lt; 0), downsides must
    /// not flip to a reward — that is what made "reduced incarnation" notables score well under Block.
    /// </summary>
    private static double SignedMagnitude(double weight, double magnitude, bool isDownside) =>
        weight > 0d && isDownside ? -magnitude : magnitude;

    private static bool NodeBelongsToCategory(AtlasNode node, ScoreTable scores, string category)
    {
        foreach (var stat in node.Stats)
        {
            if (scores.IsIgnored(stat))
                continue;

            if (scores.Classify(stat, node).Equals(category, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when a blocked mechanic owns this node. Stat lines always count; region matches only when
    /// the mechanic has no off-switch, so blocking Blight does not seal off its wheel as a connector.
    /// </summary>
    private static bool NodeMatchesBlockedMechanic(
        AtlasNode node,
        ScoreTable scores,
        string mechanic,
        bool forbidByRegion)
    {
        if (NodeBelongsToCategory(node, scores, mechanic))
            return true;

        return forbidByRegion && node.Region.Equals(mechanic, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves node ids or exact names, failing loudly with suggestions rather than silently.
    /// </summary>
    /// <remarks>
    /// A name that appears on several nodes resolves to all of them. The tree repeats a name for every
    /// copy of the same small passive, so "Mercenary Chance" means the seven that add up to +70%
    /// encounter chance, not an ambiguity to complain about. Use ids to pick out individual copies.
    /// </remarks>
    private static IEnumerable<int> ResolveNodes(AtlasTree tree, IEnumerable<string> references, string field)
    {
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            if (int.TryParse(reference, out var id))
            {
                if (!tree.Nodes.ContainsKey(id))
                    throw new InvalidOperationException($"{field}: no allocatable node with id {id}.");

                yield return id;
                continue;
            }

            var matches = tree.Nodes.Values
                .Where(n => n.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n.Id)
                .ToList();

            if (matches.Count > 0)
            {
                foreach (var match in matches)
                    yield return match.Id;

                continue;
            }

            var near = tree.Nodes.Values
                .Where(n => n.Name.Contains(reference, StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Name)
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToList();

            throw new InvalidOperationException(
                $"{field}: no node named '{reference}'." +
                (near.Count > 0 ? $" Did you mean: {string.Join(", ", near)}?" : string.Empty));
        }
    }
}

/// <summary>A node ruled out because it switches off a category the profile asks for.</summary>
public sealed class NullifiedNode
{
    public required int NodeId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Stat { get; init; }
}

public sealed class PrizeContribution
{
    public required string Stat { get; init; }
    public required string Category { get; init; }
    public required double Weight { get; init; }
    public required double Magnitude { get; init; }
    public required double Value { get; init; }
}
