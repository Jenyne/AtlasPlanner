namespace AtlasPlanner.Core.Planning;

/// <summary>
/// Group 232 — Trarthan Vapours wheel. Combat nodes (Onslaught / CDR / auras) make Mercenaries
/// harder and have no farm upside, so they are Never-take. The two Mercenary Chance smalls are
/// only useful as the last +20% toward 100% inhabit chance; they sit far from the start, so the
/// solver may take them only when Mercenaries is chased and only at a reduced prize.
/// </summary>
public static class TrarthanVapoursCluster
{
    public const int GroupId = 232;

    public const int TrarthanVapours = 32750;
    public const int MercenaryCooldownRecovery = 22246;
    public const int MercenaryAuraEffect = 6886;
    public const int FaithInArms = 57913;

    /// <summary>Entry + inner Mercenary Chance smalls on the Vapours wheel (+10% each).</summary>
    public const int OuterMercenaryChance = 21423;
    public const int InnerMercenaryChance = 13324;

    /// <summary>Combat / “harder mercs” nodes — never worth allocating for farming.</summary>
    public static IReadOnlyList<int> CombatNodeIds { get; } =
        [TrarthanVapours, MercenaryCooldownRecovery, MercenaryAuraEffect, FaithInArms];

    /// <summary>Only acceptable nodes on the wheel; finisher chance toward 100% inhabit.</summary>
    public static IReadOnlyList<int> ChanceNodeIds { get; } =
        [OuterMercenaryChance, InnerMercenaryChance];

    /// <summary>
    /// Scale applied to Mercenaries (and related) prize on the distant chance nodes so the nearby
    /// Minor Fiefdoms cluster is preferred; these two only get bought to finish 100%.
    /// </summary>
    public const double DistantChancePrizeScale = 0.25d;

    public static bool IsCombatNode(int nodeId) =>
        nodeId is TrarthanVapours or MercenaryCooldownRecovery or MercenaryAuraEffect or FaithInArms;

    public static bool IsChanceNode(int nodeId) =>
        nodeId is OuterMercenaryChance or InnerMercenaryChance;
}
