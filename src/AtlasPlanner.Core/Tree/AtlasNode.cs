using System.Numerics;
using AtlasPlanner.Core.Stats;

namespace AtlasPlanner.Core.Tree;

public enum NodeKind
{
    /// <summary>The free centre node the whole tree hangs off. Costs no points.</summary>
    Start,
    Normal,
    Notable,
    Keystone,

    /// <summary>Decorative region label sitting at a group centre. Unconnected and unallocatable.</summary>
    Mastery,
}

public sealed class AtlasNode
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required NodeKind Kind { get; init; }
    public string Icon { get; init; } = string.Empty;

    /// <summary>One of the paired "gateway" nodes that stitch distant regions together.</summary>
    public bool IsGateway { get; init; }

    /// <summary>Extra atlas points this node hands back when allocated (only Unwavering Vision, +20).</summary>
    public int GrantedPoints { get; init; }

    public required IReadOnlyList<ParsedStat> Stats { get; init; }
    public IReadOnlyList<string> ReminderText { get; init; } = [];
    public IReadOnlyList<string> FlavourText { get; init; } = [];

    public required int GroupId { get; init; }
    public int Orbit { get; init; }
    public int OrbitIndex { get; init; }

    /// <summary>Position in tree space, as used by the in-game canvas.</summary>
    public Vector2 Position { get; init; }

    /// <summary>Mastery name of this node's group, e.g. "Bestiary". Empty when the group has no mastery.</summary>
    public string Region { get; internal set; } = string.Empty;

    public int[] Neighbours { get; internal set; } = [];

    /// <summary>Points required to allocate. Every real atlas node costs one; the start node is free.</summary>
    public int Cost => Kind == NodeKind.Start ? 0 : 1;

    /// <summary>False for masteries, which exist only as labels.</summary>
    public bool IsAllocatable => Kind != NodeKind.Mastery;

    /// <summary>
    /// Nodes that shut a mechanic off in exchange for extra content chance
    /// (Dimensional Barrier, Black Thumb, and the like).
    /// </summary>
    public bool IsExclusionNotable => ExclusionStat is not null;

    /// <summary>The stat line that makes this an exclusion notable, if it is one.</summary>
    public ParsedStat? ExclusionStat =>
        Kind != NodeKind.Notable
            ? null
            : Stats.FirstOrDefault(s =>
                s.Text.StartsWith("Your Maps have no chance to contain", StringComparison.OrdinalIgnoreCase));

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? $"#{Id} ({Kind})" : $"{Name} (#{Id})";
}
