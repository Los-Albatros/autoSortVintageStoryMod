namespace autoSortVintageStoryMod.Sorting;

/// <summary>
/// Pure static classifier — no VS API dependency.
/// All methods operate on the item code path string only (e.g. "game:sword-copper").
/// </summary>
public static class ItemClassifier
{
    // Rules evaluated in declaration order — FIRST MATCH WINS.
    // Order is chosen to resolve substring collisions: more specific / earlier-winning
    // categories come first (e.g. Armor "leggings" before Food "egg"; Tool "fishingrod"
    // before Food "fish"; Food "sandwich"/"potato" before Material "sand" / Pottery "pot").
    private static readonly (SemanticType Type, string[] Keywords)[] Rules =
    [
        // Weapons (before Tool so "axe" weapons aren't shadowed; knife is a Tool below)
        (SemanticType.Weapon,   ["sword", "spear", "bow-", "arrow", "javelin", "sling", "falx",
                                 "halberd", "mace", "club", "dagger", "crossbow", "blade"]),

        // Tools ("fishingrod"/"rod" here so they win over Food "fish")
        (SemanticType.Tool,     ["pickaxe", "propick", "axe", "shovel", "saw", "hammer", "chisel",
                                 "knife", "scythe", "shears", "wrench", "hoe", "file", "tongs",
                                 "trowel", "ploughshare", "fishingrod", "rod", "sickle", "mallet",
                                 "paxel", "needle", "awl", "rake"]),

        // Armour ("leggings"/"boots" before Food/Material collisions)
        (SemanticType.Armor,    ["helmet", "leggings", "chestplate", "cuirass", "boots", "brigandine",
                                 "armplate", "tassets", "gambeson", "gauntlet", "greaves",
                                 "breastplate", "shield", "vambrace", "pauldron"]),

        // Clothing / wearables ("clothes" covers most vanilla wearables)
        (SemanticType.Clothing, ["clothes", "shirt", "trousers", "gloves", "scarf", "backpack",
                                 "satchel", "rucksack", "cape", "mantle", "tunic", "dress", "robe",
                                 "apron", "hood", "belt"]),

        // Mechanical power components (every gear variant clusters here)
        (SemanticType.Mechanical, ["gear", "cog", "axle", "windmill", "clutch", "toggle", "pulley",
                                   "angledgear", "brake", "transmission"]),

        // Lighting (before Ingredient so "oillamp" doesn't hit "oil")
        (SemanticType.Lighting, ["torch", "candle", "lantern", "oillamp", "chandelier", "lamp", "sconce"]),

        // Fuel
        (SemanticType.Fuel,     ["coal", "charcoal", "firewood", "peat", "coke", "lignite",
                                 "anthracite", "bituminous"]),

        // Medicine / alchemy
        (SemanticType.Medicine, ["poultice", "tincture", "salve", "bandage", "remedy", "medicine",
                                 "ointment"]),

        // Seeds & propagation
        (SemanticType.Seed,     ["seed", "sapling", "cutting", "clipping", "bulb", "spore"]),

        // Cooking ingredients (before Drink so "oilportion" wins over "portion")
        (SemanticType.Ingredient, ["flour", "dough", "oil", "butter", "fat", "sugar", "spice",
                                   "sauce", "vinegar", "syrup", "honey", "yeast", "starch", "lard"]),

        // Drinks / liquid portions
        (SemanticType.Drink,    ["portion", "juice", "cider", "wine", "spirit", "mead", "milk",
                                 "water", "lemonade", "ale", "beer", "brew", "nectar"]),

        // Food (before Pottery "pot"/Material "sand" so potato/sandwich win)
        (SemanticType.Food,     ["bread", "meat", "meal", "cheese", "grain", "fruit", "vegetable",
                                 "mushroom", "herb", "salted", "dried", "pickled", "berry", "nut",
                                 "egg", "fish", "pie", "stew", "soup", "jam", "sausage", "bacon",
                                 "jerky", "pemmican", "porridge", "sandwich", "cake", "candy",
                                 "chocolate", "marzipan", "raisin", "rusk", "dumpling", "jelly",
                                 "pumpkin", "carrot", "onion", "cabbage", "turnip", "parsnip",
                                 "soybean", "potato", "rice", "spelt"]),

        // Pottery / containers
        (SemanticType.Pottery,  ["crock", "bowl", "jug", "jar", "bucket", "basket", "vessel",
                                 "flask", "crucible", "mold", "amphora", "vial", "pot"]),

        // Building & decoration (trellis covers the large modded trellis variants)
        (SemanticType.Building, ["slab", "stairs", "fence", "door", "window", "wall", "roof",
                                 "ladder", "trapdoor", "gate", "beam", "support", "chiseled",
                                 "rug", "carpet", "banner", "drape", "trellis", "planter",
                                 "shingle", "railing", "pillar", "lattice", "pane"]),

        // Valuables / jewellery (before Material so a ring isn't caught by "metal")
        (SemanticType.Valuable, ["diamond", "emerald", "ruby", "sapphire", "gem", "jewel", "ring",
                                 "necklace", "bracelet", "amulet", "pearl", "jade", "opal", "topaz",
                                 "garnet", "peridot"]),

        // Raw materials (catch-all crafting stock — kept last among the typed rules)
        (SemanticType.Material, ["ingot", "ore", "nugget", "plank", "log", "stone", "rock", "sand",
                                 "clay", "leather", "linen", "wool", "resin", "metal", "plate",
                                 "sheet", "stick", "board", "mortar", "lime", "twine", "flax",
                                 "cloth", "fabric", "hide", "horn", "feather", "wax", "pitch", "tar",
                                 "quartz", "crystal", "fiber", "reed", "cattail", "papyrus", "glass",
                                 "brick", "nail", "fur"]),
    ];

    private static readonly Dictionary<string, int> TierMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flint"]         = 1,
        ["bone"]          = 2,
        ["copper"]        = 3,
        ["tinbronze"]     = 4,
        ["bismuthbronze"] = 5,
        ["blackbronze"]   = 6,
        ["iron"]          = 7,
        ["meteoriciron"]  = 8,
        ["steel"]         = 9,
    };

    /// <summary>
    /// Returns the semantic type for an item code path.
    /// Rules are evaluated in order; first keyword match wins.
    /// </summary>
    public static SemanticType Classify(string codePath)
    {
        if (string.IsNullOrEmpty(codePath))
            return SemanticType.Other;

        // Strip the mod domain prefix ("wildcraftfruit:woodtrellis-…" → "woodtrellis-…").
        // Otherwise a domain like "wildcraftfruit" would match the Food keyword "fruit"
        // and misclassify every item from that mod.
        var path = codePath.Contains(':') ? codePath[(codePath.IndexOf(':') + 1)..] : codePath;

        foreach (var (type, keywords) in Rules)
        {
            foreach (var kw in keywords)
            {
                if (path.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return type;
            }
        }

        return SemanticType.Other;
    }

    /// <summary>
    /// Returns the base name of an item: the code with the domain prefix removed
    /// and truncated at the first dash. E.g. "game:gear-temporal" → "gear",
    /// "game:sword-copper" → "sword". Used to cluster all variants of the same
    /// item together (every gear next to every other gear), regardless of tier/colour.
    /// </summary>
    public static string BaseName(string codePath)
    {
        if (string.IsNullOrEmpty(codePath))
            return "";

        var path = codePath.Contains(':') ? codePath[(codePath.IndexOf(':') + 1)..] : codePath;
        var firstDash = path.IndexOf('-');
        return firstDash < 0 ? path : path[..firstDash];
    }

    /// <summary>
    /// Returns the material tier (0 = unknown) by inspecting the last
    /// dash-separated segment of the code path.
    /// E.g. "game:sword-tinbronze" → last segment "tinbronze" → 4.
    /// </summary>
    public static int MaterialTier(string codePath)
    {
        if (string.IsNullOrEmpty(codePath))
            return 0;

        // Strip domain prefix ("game:sword-copper" → "sword-copper")
        var path = codePath.Contains(':') ? codePath[(codePath.IndexOf(':') + 1)..] : codePath;

        var lastDash = path.LastIndexOf('-');
        if (lastDash < 0)
            return 0;

        var suffix = path[(lastDash + 1)..];
        return TierMap.TryGetValue(suffix, out var tier) ? tier : 0;
    }
}
