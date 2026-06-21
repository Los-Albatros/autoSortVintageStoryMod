using autoSortVintageStoryMod.Config;
using Xunit;

namespace autoSortVintageStoryMod.Tests.Config;

/// <summary>
/// Covers the container-recognition and grouping logic that decides whether a container
/// is sorted at all and which other containers it pools with. Regression cover for the
/// wooden-trunk case: trunks report inventory class "trunk" (not the JSON's "chest"), so
/// the class must be a default or trunks are never recognised.
/// </summary>
public class SortConfigTests
{
    // ── EnsureDefaults ─────────────────────────────────────────────────────────

    [Fact]
    public void EnsureDefaults_IncludesTrunk_SoTrunksAreRecognised()
    {
        var cfg = new SortConfig();
        cfg.EnsureDefaults();

        Assert.Contains("trunk", cfg.SupportedInventoryClasses);
        Assert.Contains("chest", cfg.SupportedInventoryClasses);
    }

    [Fact]
    public void EnsureDefaults_EveryDefaultKindBelongsToAGroup()
    {
        var cfg = new SortConfig();
        cfg.EnsureDefaults();

        var grouped = cfg.ContainerGroups.SelectMany(g => g).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("chest", grouped);
        Assert.Contains("basket", grouped);
        Assert.Contains("crate", grouped);
        Assert.Contains("storagevessel", grouped);
        Assert.Contains("trunk", grouped);
    }

    [Fact]
    public void EnsureDefaults_IsIdempotent_NoDuplicateEntries()
    {
        var cfg = new SortConfig();
        cfg.EnsureDefaults();
        int classes = cfg.SupportedInventoryClasses.Count;
        int groups = cfg.ContainerGroups.Count;
        int crates = cfg.CrateInventoryClasses.Count;

        cfg.EnsureDefaults();

        Assert.Equal(classes, cfg.SupportedInventoryClasses.Count);
        Assert.Equal(groups, cfg.ContainerGroups.Count);
        Assert.Equal(crates, cfg.CrateInventoryClasses.Count);
    }

    [Fact]
    public void EnsureDefaults_LeavesUserProvidedListsUntouched()
    {
        var cfg = new SortConfig();
        cfg.SupportedInventoryClasses.Add("onlymine");
        cfg.ContainerGroups.Add(new List<string> { "onlymine" });

        cfg.EnsureDefaults();

        Assert.Equal(new[] { "onlymine" }, cfg.SupportedInventoryClasses);
        Assert.Single(cfg.ContainerGroups);
    }

    // ── GetContainerGroup ──────────────────────────────────────────────────────

    [Fact]
    public void GetContainerGroup_ReturnsTheGroupContainingTheKind()
    {
        var cfg = new SortConfig();
        cfg.EnsureDefaults();

        var group = cfg.GetContainerGroup("chest");
        Assert.Contains("chest", group);
        Assert.Contains("crate", group); // chest+basket+crate share one default group
    }

    [Fact]
    public void GetContainerGroup_MatchesBlockKindBySubstring()
    {
        var cfg = new SortConfig();
        cfg.EnsureDefaults();

        // Called with the block kind, e.g. "chest-normal" / "trunk-normal-generic".
        Assert.Contains("chest", cfg.GetContainerGroup("chest-normal"));
    }

    [Fact]
    public void GetContainerGroup_UngroupedKind_FallsBackToOwnSingleton()
    {
        var cfg = new SortConfig
        {
            ContainerGroups = new List<List<string>> { new() { "chest" } }
        };

        // "trunk" is in no group → its own singleton: it still sorts its own contents
        // on close, but never pools with chests.
        Assert.Equal(new[] { "trunk" }, cfg.GetContainerGroup("trunk"));
    }

    [Fact]
    public void GetContainerGroup_TrunkGroupedWithChest_SharesOneNetwork()
    {
        var cfg = new SortConfig
        {
            ContainerGroups = new List<List<string>> { new() { "chest", "trunk" } }
        };

        var fromChest = cfg.GetContainerGroup("chest");
        var fromTrunk = cfg.GetContainerGroup("trunk");

        Assert.Contains("trunk", fromChest);
        Assert.Same(fromChest, fromTrunk); // both resolve to the same group instance
    }
}
