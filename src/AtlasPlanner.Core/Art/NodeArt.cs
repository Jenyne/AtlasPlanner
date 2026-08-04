using AtlasPlanner.Core.Tree;

namespace AtlasPlanner.Core.Art;

/// <summary>Sprite category names as they appear in the tree export.</summary>
public static class SpriteCategories
{
    public const string NormalActive = "normalActive";
    public const string NormalInactive = "normalInactive";
    public const string NotableActive = "notableActive";
    public const string NotableInactive = "notableInactive";
    public const string KeystoneActive = "keystoneActive";
    public const string KeystoneInactive = "keystoneInactive";
    public const string GatewayActive = "wormholeActive";
    public const string GatewayInactive = "wormholeInactive";
    public const string Mastery = "mastery";
    public const string MasteryOverlay = "masteryOverlay";
    public const string GroupBackground = "groupBackground";
    public const string StartNode = "startNode";
    public const string Frame = "frame";
    public const string Line = "line";
    public const string Background = "background";
    public const string AtlasBackground = "atlasBackground";
}

/// <summary>How a node is currently being presented, which selects its frame art.</summary>
public enum NodeVisualState
{
    /// <summary>Not taken and not adjacent to anything taken.</summary>
    Unallocated,

    /// <summary>Not taken, but reachable in one point from the current allocation.</summary>
    CanAllocate,

    Allocated,

    /// <summary>Hovered, or called out as part of a planned route.</summary>
    Highlighted,
}

/// <summary>Whether a connector links two taken nodes, one, or none.</summary>
public enum ConnectorState
{
    Normal,
    Intermediate,
    Active,
}

/// <summary>Maps tree nodes and connectors onto sprite keys from the export's art manifest.</summary>
public static class NodeArt
{
    public const string GatewayIconKey = "Wormhole";
    public const string StartNodeKey = "AtlasPassiveSkillScreenStart";
    public const string BackgroundTileKey = "Background2";
    public const string AtlasBackgroundKey = "AtlasPassiveBackground";

    /// <summary>Groups whose region ships bespoke background art instead of the generic rings.</summary>
    private static readonly Dictionary<string, string> RegionBackgrounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cleansing Fire"] = "GroupBackgroundCleansingFire",
        ["The Tangle"] = "GroupBackgroundTangle",
        ["Tangle"] = "GroupBackgroundTangle",
    };

    public static string IconCategory(AtlasNode node, bool allocated) => node.Kind switch
    {
        NodeKind.Mastery => SpriteCategories.Mastery,
        _ when node.IsGateway => allocated ? SpriteCategories.GatewayActive : SpriteCategories.GatewayInactive,
        NodeKind.Keystone => allocated ? SpriteCategories.KeystoneActive : SpriteCategories.KeystoneInactive,
        NodeKind.Notable => allocated ? SpriteCategories.NotableActive : SpriteCategories.NotableInactive,
        _ => allocated ? SpriteCategories.NormalActive : SpriteCategories.NormalInactive,
    };

    /// <summary>The sprite key for a node's icon, or null when it has no icon of its own.</summary>
    public static string? IconKey(AtlasNode node)
    {
        if (node.Kind == NodeKind.Start)
            return null;
        if (node.IsGateway)
            return GatewayIconKey;

        return node.Icon.Length > 0 ? node.Icon : null;
    }

    /// <summary>The frame drawn over a node's icon, or null when the art has no frame for it.</summary>
    public static string? FrameKey(AtlasNode node, NodeVisualState state)
    {
        if (node.Kind is NodeKind.Start or NodeKind.Mastery)
            return null;

        if (node.IsGateway)
            return state switch
            {
                NodeVisualState.Allocated => "WormholeFrameAllocated",
                NodeVisualState.CanAllocate => "WormholeFrameCanAllocate",
                NodeVisualState.Highlighted => "WormholeFrameHighlight",
                _ => "WormholeFrameUnallocated",
            };

        // Notable and keystone art has no dedicated highlight frame, so highlight borrows
        // the brighter "can allocate" variant.
        var prefix = node.Kind switch
        {
            NodeKind.Keystone => "Keystone",
            NodeKind.Notable => "Notable",
            _ => null,
        };

        if (prefix is not null)
            return state switch
            {
                NodeVisualState.Allocated => prefix + "FrameAllocated",
                NodeVisualState.CanAllocate or NodeVisualState.Highlighted => prefix + "FrameCanAllocate",
                _ => prefix + "FrameUnallocated",
            };

        return state switch
        {
            NodeVisualState.Allocated => "PSSkillFrameActive",
            NodeVisualState.CanAllocate or NodeVisualState.Highlighted => "PSSkillFrameHighlighted",
            _ => "PSSkillFrame",
        };
    }

    /// <summary>The decorative ring behind a group, sized by the outermost orbit it uses.</summary>
    public static string? GroupBackgroundKey(int maxOrbit, string region)
    {
        if (region.Length > 0 && RegionBackgrounds.TryGetValue(region, out var bespoke))
            return bespoke;

        return maxOrbit switch
        {
            <= 0 => null,
            1 => "PSGroupBackground1",
            2 => "PSGroupBackground2",
            // The art stops at three rings; wider groups reuse the largest.
            _ => "PSGroupBackground3",
        };
    }

    public static string ConnectorKey(ConnectorState state) => "LineConnector" + state;

    /// <summary>The ring sprite for an orbital connector, or null when that orbit has no art.</summary>
    public static string? OrbitKey(int orbit, ConnectorState state) =>
        orbit is >= 1 and <= 6 ? $"Orbit{orbit}{state}" : null;

    public static ConnectorState StateFor(bool fromAllocated, bool toAllocated) =>
        (fromAllocated, toAllocated) switch
        {
            (true, true) => ConnectorState.Active,
            (false, false) => ConnectorState.Normal,
            _ => ConnectorState.Intermediate,
        };
}
