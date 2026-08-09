using System.Globalization;
using System.Numerics;
using System.Text.Json;
using AtlasPlanner.Core.Art;
using AtlasPlanner.Core.Data;
using AtlasPlanner.Core.Stats;

namespace AtlasPlanner.Core.Tree;

/// <summary>
/// The atlas passive tree as an undirected graph of allocatable nodes, anchored at a free start node.
/// </summary>
public sealed class AtlasTree
{
    /// <summary>Key of the structural sentinel in the JSON; it only points at the real start node.</summary>
    private const string RootKey = "root";

    private readonly Dictionary<int, AtlasNode> _nodes;
    private readonly Dictionary<int, AtlasGroup> _groups;
    private readonly Dictionary<int, int> _indexOf;
    private readonly int[] _ids;
    private readonly int[][] _adjacency;

    private AtlasTree(
        Dictionary<int, AtlasNode> nodes,
        IReadOnlyList<AtlasNode> masteries,
        Dictionary<int, AtlasGroup> groups,
        IReadOnlyList<AtlasEdge> edges,
        SpriteCatalog sprites,
        ConstantsJson constants,
        int startNodeId,
        int totalPoints,
        string treeName)
    {
        _nodes = nodes;
        Masteries = masteries;
        _groups = groups;
        Edges = edges;
        Sprites = sprites;
        OrbitRadii = constants.OrbitRadii;
        SkillsPerOrbit = constants.SkillsPerOrbit;
        StartNodeId = startNodeId;
        TotalPoints = totalPoints;
        TreeName = treeName;

        NodeBounds = TreeBounds.Around(
            nodes.Values.Select(n => n.Position).Concat(masteries.Select(m => m.Position)));
        ArtBounds = TreeBounds.Around(groups.Values.SelectMany(g => GroupExtent(g, constants.OrbitRadii)));

        _ids = nodes.Keys.Order().ToArray();
        _indexOf = new Dictionary<int, int>(_ids.Length);
        for (var i = 0; i < _ids.Length; i++)
            _indexOf[_ids[i]] = i;

        _adjacency = new int[_ids.Length][];
        for (var i = 0; i < _ids.Length; i++)
            _adjacency[i] = nodes[_ids[i]].Neighbours.Select(id => _indexOf[id]).ToArray();
    }

    public string TreeName { get; }

    /// <summary>Base atlas point budget reported by the tree export (138 as of 3.29).</summary>
    public int TotalPoints { get; }

    public int StartNodeId { get; }

    public AtlasNode Start => _nodes[StartNodeId];

    /// <summary>Allocatable nodes only; masteries live in <see cref="Masteries"/>.</summary>
    public IReadOnlyDictionary<int, AtlasNode> Nodes => _nodes;

    /// <summary>Region labels sitting at group centres. Not part of the graph.</summary>
    public IReadOnlyList<AtlasNode> Masteries { get; }

    /// <summary>Layout clusters, used for drawing and for orbit geometry.</summary>
    public IReadOnlyDictionary<int, AtlasGroup> Groups => _groups;

    /// <summary>Every connection once, with arcs already distinguished from straight lines.</summary>
    public IReadOnlyList<AtlasEdge> Edges { get; }

    /// <summary>The export's art manifest. Empty when the tree data ships without sprites.</summary>
    public SpriteCatalog Sprites { get; }

    public IReadOnlyList<int> OrbitRadii { get; }
    public IReadOnlyList<int> SkillsPerOrbit { get; }

    /// <summary>Tight box around every node, masteries included.</summary>
    public TreeBounds NodeBounds { get; }

    /// <summary>
    /// Box around every group's outermost orbit. This is the extent the background art is drawn to
    /// cover, so it is the right box to fit the viewport to.
    /// </summary>
    public TreeBounds ArtBounds { get; }

    public int NodeCount => _ids.Length;

    public AtlasNode this[int id] => _nodes[id];

    public bool TryGet(int id, out AtlasNode? node) => _nodes.TryGetValue(id, out node);

    // Dense index view, for solver code that wants flat arrays instead of dictionary lookups.
    public int IndexOf(int id) => _indexOf[id];
    public int IdAt(int index) => _ids[index];
    public int[] NeighboursByIndex(int index) => _adjacency[index];

    /// <summary>Radius of a node's orbit, in tree units. Zero for nodes sitting at a group centre.</summary>
    public float RadiusOf(AtlasNode node) =>
        node.Orbit >= 0 && node.Orbit < OrbitRadii.Count ? OrbitRadii[node.Orbit] : 0f;

    /// <summary>
    /// A node's angle around its group centre, measured clockwise from straight up. Needed to draw
    /// orbital connectors as arcs.
    /// </summary>
    public float AngleOf(AtlasNode node) =>
        node.Orbit >= 0 && node.Orbit < SkillsPerOrbit.Count
            ? Orbits.AngleFor(node.OrbitIndex, SkillsPerOrbit[node.Orbit])
            : 0f;

    private static IEnumerable<Vector2> GroupExtent(AtlasGroup group, IReadOnlyList<int> orbitRadii)
    {
        var radius = group.MaxOrbit >= 0 && group.MaxOrbit < orbitRadii.Count
            ? orbitRadii[group.MaxOrbit]
            : 0;
        var offset = new Vector2(radius, radius);
        yield return group.Position - offset;
        yield return group.Position + offset;
    }

    public static AtlasTree Load(string path) => Parse(File.ReadAllText(path));

    public static AtlasTree Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<TreeJson>(json)
                  ?? throw new InvalidDataException("Tree JSON deserialised to null.");

        var startNodeId = ResolveStartNode(raw);
        var masteries = new List<AtlasNode>();
        var nodes = new Dictionary<int, AtlasNode>(raw.Nodes.Count);
        var regionByGroup = new Dictionary<int, string>();

        foreach (var (key, nodeJson) in raw.Nodes)
        {
            if (key == RootKey)
                continue;

            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException($"Unexpected non-numeric node key '{key}'.");

            var kind = Classify(nodeJson, id == startNodeId);
            var node = new AtlasNode
            {
                Id = id,
                Name = nodeJson.Name ?? string.Empty,
                Kind = kind,
                Icon = nodeJson.Icon ?? string.Empty,
                IsGateway = nodeJson.IsWormhole,
                GrantedPoints = nodeJson.GrantedPassivePoints,
                Stats = nodeJson.Stats.Select(StatText.Parse).Where(s => s.Text.Length > 0).ToArray(),
                ReminderText = nodeJson.ReminderText.Select(StatText.Clean).ToArray(),
                FlavourText = nodeJson.FlavourText.Select(StatText.Clean).ToArray(),
                GroupId = nodeJson.Group,
                Orbit = nodeJson.Orbit,
                OrbitIndex = nodeJson.OrbitIndex,
                Position = ComputePosition(raw, nodeJson),
            };

            if (kind == NodeKind.Mastery)
            {
                masteries.Add(node);
                regionByGroup[node.GroupId] = node.Name;
            }
            else
            {
                nodes.Add(id, node);
            }
        }

        var edges = BuildAdjacency(raw, nodes, startNodeId);

        foreach (var node in nodes.Values)
            node.Region = regionByGroup.GetValueOrDefault(node.GroupId, string.Empty);

        var groups = BuildGroups(raw, nodes, masteries, regionByGroup);
        ClassifyArcs(edges, nodes, groups);

        return new AtlasTree(
            nodes,
            masteries,
            groups,
            edges,
            SpriteCatalog.From(raw),
            raw.Constants,
            startNodeId,
            raw.Points?.TotalPoints ?? 0,
            raw.Tree ?? "Unknown");
    }

    private static Dictionary<int, AtlasGroup> BuildGroups(
        TreeJson raw,
        Dictionary<int, AtlasNode> nodes,
        IReadOnlyList<AtlasNode> masteries,
        Dictionary<int, string> regionByGroup)
    {
        var groups = new Dictionary<int, AtlasGroup>(raw.Groups.Count);

        foreach (var (key, groupJson) in raw.Groups)
        {
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;

            var orbits = groupJson.Orbits.ToArray();
            groups[id] = new AtlasGroup
            {
                Id = id,
                Position = new Vector2(groupJson.X, groupJson.Y),
                Orbits = orbits,
                MaxOrbit = orbits.Length > 0 ? orbits.Max() : 0,
                Region = regionByGroup.GetValueOrDefault(id, string.Empty),
            };
        }

        // Masteries are excluded from the graph but still anchor their group's identity, so they are
        // counted here to keep group membership complete.
        var membership = new Dictionary<int, List<int>>();
        foreach (var node in nodes.Values.Concat(masteries))
        {
            if (!membership.TryGetValue(node.GroupId, out var list))
                membership[node.GroupId] = list = [];
            list.Add(node.Id);
        }

        foreach (var (groupId, members) in membership)
        {
            if (groups.TryGetValue(groupId, out var group))
                group.NodeIds = members.Order().ToArray();
        }

        return groups;
    }

    /// <summary>
    /// Marks the connections that run along an orbit rather than across the tree, so they can be
    /// drawn as arcs. Both endpoints must share a group and a non-central orbit.
    /// </summary>
    private static void ClassifyArcs(
        List<AtlasEdge> edges,
        Dictionary<int, AtlasNode> nodes,
        Dictionary<int, AtlasGroup> groups)
    {
        for (var i = 0; i < edges.Count; i++)
        {
            var from = nodes[edges[i].FromId];
            var to = nodes[edges[i].ToId];

            if (from.GroupId != to.GroupId
                || from.Orbit != to.Orbit
                || from.Orbit <= 0
                || !groups.ContainsKey(from.GroupId))
            {
                continue;
            }

            edges[i] = new AtlasEdge(from.Id, to.Id, from.GroupId, from.Orbit);
        }
    }

    private static NodeKind Classify(NodeJson node, bool isStart)
    {
        if (node.IsMastery) return NodeKind.Mastery;
        if (isStart) return NodeKind.Start;
        if (node.IsKeystone) return NodeKind.Keystone;
        if (node.IsNotable) return NodeKind.Notable;
        return NodeKind.Normal;
    }

    private static int ResolveStartNode(TreeJson raw)
    {
        if (raw.Nodes.TryGetValue(RootKey, out var root))
        {
            var first = root.Out.Concat(root.In)
                .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                .FirstOrDefault(v => v.HasValue);
            if (first.HasValue)
                return first.Value;
        }

        // Fall back to the only unnamed, statless node with real connections.
        var candidate = raw.Nodes
            .Where(kv => kv.Key != RootKey
                         && string.IsNullOrEmpty(kv.Value.Name)
                         && kv.Value.Stats.Count == 0
                         && kv.Value.Out.Count + kv.Value.In.Count > 1)
            .Select(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))
            .FirstOrDefault(-1);

        if (candidate < 0)
            throw new InvalidDataException("Could not resolve the atlas tree start node.");

        return candidate;
    }

    /// <summary>
    /// The export's in/out split is not a reliable direction, so edges are unioned into an
    /// undirected adjacency list.
    /// </summary>
    private static List<AtlasEdge> BuildAdjacency(TreeJson raw, Dictionary<int, AtlasNode> nodes, int startNodeId)
    {
        var neighbours = nodes.Keys.ToDictionary(id => id, _ => new HashSet<int>());

        foreach (var (key, nodeJson) in raw.Nodes)
        {
            if (key == RootKey || !int.TryParse(key, out var from) || !neighbours.ContainsKey(from))
                continue;

            foreach (var raw2 in nodeJson.Out.Concat(nodeJson.In))
            {
                if (raw2 == RootKey || !int.TryParse(raw2, out var to) || !neighbours.ContainsKey(to) || to == from)
                    continue;

                neighbours[from].Add(to);
                neighbours[to].Add(from);
            }
        }

        // The sentinel's edge to the start node carries no meaning once the sentinel is dropped.
        neighbours[startNodeId].Remove(startNodeId);

        foreach (var (id, set) in neighbours)
            nodes[id].Neighbours = set.Order().ToArray();

        var edges = new List<AtlasEdge>();
        foreach (var id in neighbours.Keys.Order())
        {
            foreach (var other in neighbours[id].Order())
            {
                // Emit each undirected pair once.
                if (id < other)
                    edges.Add(AtlasEdge.Straight(id, other));
            }
        }

        return edges;
    }

    private static Vector2 ComputePosition(TreeJson raw, NodeJson node)
    {
        if (!raw.Groups.TryGetValue(node.Group.ToString(CultureInfo.InvariantCulture), out var group))
            return Vector2.Zero;

        var centre = new Vector2(group.X, group.Y);
        var radii = raw.Constants.OrbitRadii;
        var perOrbit = raw.Constants.SkillsPerOrbit;

        if (node.Orbit < 0 || node.Orbit >= radii.Count || node.Orbit >= perOrbit.Count)
            return centre;

        var radius = radii[node.Orbit];
        if (radius == 0)
            return centre;

        var angle = Orbits.AngleFor(node.OrbitIndex, perOrbit[node.Orbit]);
        return centre + radius * new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
    }
}
