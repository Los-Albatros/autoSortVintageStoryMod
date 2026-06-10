namespace autoSortVintageStoryMod.Config;

public class SortConfig
{
    /// <summary>Master switch. When false the mod registers no event handlers.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Delay (milliseconds) after a container closes before the room is sorted. Each new
    /// close within the delay restarts the timer, so the sort runs ONCE after the player
    /// finishes — not on every individual chest. Keeps a big room from being re-laid-out
    /// dozens of times in a row.
    /// </summary>
    public int SortDebounceMs { get; set; } = 1500;

    /// <summary>
    /// When true, the whole container group in the room is laid out as one compact
    /// block: items are pooled, sorted, and packed into the chests in order starting
    /// from the room's door (then a fixed corner if several doors, else the triggering
    /// chest), filling each chest fully and leaving the trailing chests empty. Duplicate
    /// stacks across chests are merged. When false, the legacy specialist-chest
    /// distribution is used instead.
    /// </summary>
    public bool CompactRoom { get; set; } = false;

    /// <summary>
    /// When true, a player's backpack content is sorted when they close their inventory.
    /// The bag slots (where backpacks are equipped) and the hotbar are never touched.
    /// </summary>
    public bool SortPlayerBackpack { get; set; } = false;

    /// <summary>
    /// Block-code substrings of containers that must never be touched (neither read nor
    /// written) — e.g. collapsed / ruined trunks that don't allow the player to place
    /// items. Prevents items from being pushed into them.
    /// </summary>
    public List<string> IgnoredContainerCodes { get; set; } = ["collapsed"];

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
    public int MaxNetworkChests { get; set; } = 1024;

    /// <summary>
    /// Safety cap on how many air cells the room flood-fill may visit. If the open space
    /// reaches this many cells without closing off (an unenclosed / huge area), the area
    /// is treated as "not a room" and only the triggering chest is sorted — nothing is
    /// moved between containers out in the open.
    /// </summary>
    public int MaxRoomCells { get; set; } = 10000;

    /// <summary>
    /// When true, two chests separated by a solid block layer (a floor / ceiling) are
    /// treated as being on different storeys and are not sorted together — even when a
    /// ladder or open stairwell makes the room system see them as one room. Works at any
    /// ceiling height, no tuning needed.
    /// </summary>
    public bool SeparateFloors { get; set; } = true;

    /// <summary>
    /// Optional hard cap on the vertical distance (in blocks) a chest may be from the
    /// triggering chest to join its network. 0 = no cap (rely on <see cref="SeparateFloors"/>).
    /// </summary>
    public int MaxVerticalSpan { get; set; } = 0;

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
