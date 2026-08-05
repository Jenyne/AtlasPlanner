namespace AtlasPlanner.Gui.Rendering;

/// <summary>
/// Theme colors for atlas mechanic categories in the Tally panel.
/// </summary>
/// <remarks>
/// Every colour sits between roughly 6:1 and 10:1 contrast against the panel background
/// (<c>#1A1713</c>). That is comfortably readable while staying dimmer than body text at 13:1,
/// so an accent never outshouts the line it belongs to.
/// </remarks>
public static class MechanicPalette
{
    public sealed record Entry(string Hex, params string[] HighlightTerms);

    private static readonly Dictionary<string, Entry> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abyss"] = new("#6BCE6B", "Abyss", "Abyssal", "Pit"),
        ["Breach"] = new("#C77DFF", "Breach", "Breaches", "Hive", "Hiveborn", "Ailith", "Unstable"),
        ["Blight"] = new("#8BC34A", "Blight", "Blighted", "Fungal", "Blightencounter"),
        ["Harvest"] = new("#DFBE55", "Harvest", "Crop", "Lifeforce", "Plant", "Sacred Grove", "Grove"),
        ["Legion"] = new("#FF7043", "Legion", "Timeless", "Karui", "Maraketh", "Templar army", "Eternal Empire"),
        ["Heist"] = new("#4DD0E1", "Heist", "Smuggler", "Rogue", "Contract", "Blueprint"),
        ["Delve"] = new("#FFA726", "Delve", "Sulphite", "Azurite", "Fossil"),
        ["Ritual"] = new("#CE93D8", "Ritual", "Tribute", "Favour"),
        ["Ultimatum"] = new("#EE7B78", "Ultimatum", "Round"),
        ["Delirium"] = new("#90A4AE", "Delirium", "Mirror of Delirium", "Simulacrum"),
        ["Torment"] = new("#B0BEC5", "Torment", "Tormented", "Possess"),
        ["Scarabs"] = new("#E8AF25", "Scarab", "Scarabs"),
        ["The Maven"] = new("#B39DDB", "Maven", "Witnessed", "Witnessing"),
        ["Settlers of Kalguur"] = new("#9CCB9E", "Kalguur", "Ore Deposit", "Ore"),
        ["Map Sustain"] = new("#81C784", "Maps found", "Map found", "tier higher", "Map Drops"),
        ["Quantity & Rarity"] = new("#D3BC82", "Quantity", "Rarity"),
        ["Pack Size"] = new("#BCAAA4", "Pack Size"),
        ["Map Modifiers"] = new("#89BFEE", "Modifier", "Explicit Modifier"),
        ["Divination Cards"] = new("#F48FB1", "Divination Card"),
        ["Currency"] = new("#D6C273", "Currency"),
        ["Experience"] = new("#79C9D4", "Experience"),
        ["Atlas Points"] = new("#DDBC46", "Atlas Passive Skill Point"),
        ["Gateways"] = new("#94AAB6", "Connects to"),
        ["Extra Content"] = new("#BAA498", "Extra Content"),
        ["Monster Difficulty"] = new("#E57373", "Monsters in your Maps"),
        ["Beyond"] = new("#AC93E0", "Beyond", "Demon", "K'tash", "Ghorr"),
        ["Mercenaries"] = new("#4DB6AC", "Mercenary", "Mercenaries", "Azadi", "Cyaxan", "Keita"),
        ["Expedition"] = new("#FFAB91", "Expedition", "Logbook", "Dannig"),
        ["Betrayal"] = new("#FF8A65", "Betrayal", "Syndicate", "Immortal Syndicate"),
        ["Incursion"] = new("#FF7043", "Incursion", "Temple", "Alva"),
        ["Metamorph"] = new("#A4C978", "Metamorph", "Organ", "Sample"),
        ["Atlas Memories"] = new("#8E9BDB", "Memory", "Memories", "Fear", "Neglect", "Dread"),
    };

    public const string DefaultHex = "#CBBBA0";

    public static string ColorFor(string category) =>
        Map.TryGetValue(category, out var entry) ? entry.Hex : DefaultHex;

    public static IReadOnlyList<string> HighlightTermsFor(string category)
    {
        if (Map.TryGetValue(category, out var entry))
            return entry.HighlightTerms;

        return category.Length > 0 ? [category] : [];
    }
}
