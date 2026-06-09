namespace autoSortVintageStoryMod.Config;

public class SortConfig
{
    /// <summary>Master switch. When false the mod registers no event handlers.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, sorting only spans chests that share the same enclosed room as the
    /// triggering chest, so a closed door separates two storage rooms. Falls back to
    /// the full radius network when the area isn't an enclosed room (open base).
    /// </summary>
    public bool RestrictToSameRoom { get; set; } = true;

    /// <summary>
    /// When true, the whole container group in the room is laid out as one compact
    /// block: items are pooled, sorted, and packed into the chests in order starting
    /// from the room's door (then a fixed corner if several doors, else the triggering
    /// chest), filling each chest fully and leaving the trailing chests empty. Duplicate
    /// stacks across chests are merged. When false, the legacy specialist-chest
    /// distribution is used instead.
    /// </summary>
    public bool CompactRoom { get; set; } = true;

    /// <summary>
    /// When true, a player's backpack content is sorted when they close their inventory.
    /// The bag slots (where backpacks are equipped) and the hotbar are never touched.
    /// </summary>
    public bool SortPlayerBackpack { get; set; } = true;

    /// <summary>
    /// Euclidean radius (in blocks) of each chest's neighbourhood. The network grows
    /// by cascade: every discovered chest scans its own radius, so the reachable
    /// network can extend well beyond this distance from the origin.
    /// </summary>
    public int SearchRadiusBlocks { get; set; } = 10;

    /// <summary>
    /// Safety cap on how many chests a single cascade may pull into one virtual
    /// network. Prevents a pathological flood-fill from scanning the whole world.
    /// </summary>
    public int MaxNetworkChests { get; set; } = 256;

    /// <summary>
    /// Maximum vertical distance (in blocks) a chest may be from the triggering chest to
    /// join its network. Keeps sorting on the current floor even when a ladder or open
    /// stairwell makes the room system see several storeys as one room. Set to 0 to
    /// disable the vertical limit.
    /// </summary>
    public int MaxVerticalSpan { get; set; } = 3;

    /// <summary>
    /// Fraction of non-empty slots that must share a SemanticType for a chest
    /// to be considered specialised in that type (0.0–1.0).
    /// </summary>
    public double SpecialisationThreshold { get; set; } = 0.70;

    /// <summary>
    /// Maximum number of cascade waves. Each wave processes one chest and may
    /// queue its neighbours. The cascade stops early when no items moved.
    /// Default 50 is a safe upper bound for typical chest rooms.
    /// </summary>
    public int MaxCascadeIterations { get; set; } = 10;

    /// <summary>
    /// Inventory class names that qualify as supported containers.
    /// Checked against IInventory.ClassName.
    /// </summary>
    public List<string> SupportedInventoryClasses { get; set; } =
    [
        "chest",
        "largecrate",
        "storagevessel"
    ];

    /// <summary>
    /// Container groups. Distribution stays within a group — jars never mix with chests.
    /// Each inner list is a set of ClassName substrings that belong to the same group.
    /// </summary>
    public List<List<string>> ContainerGroups { get; set; } =
    [
        ["chest", "largecrate"],
        ["storagevessel"]
    ];

    /// <summary>
    /// Returns the group class list that contains <paramref name="className"/>,
    /// or a single-element list with the class itself as a fallback.
    /// </summary>
    public IReadOnlyList<string> GetContainerGroup(string className)
    {
        foreach (var group in ContainerGroups)
            if (group.Any(cls => className.Contains(cls, System.StringComparison.OrdinalIgnoreCase)))
                return group;
        return [className];
    }
}
