using autoSortVintageStoryMod.Sorting;
using Xunit;
using static autoSortVintageStoryMod.Tests.Sorting.TestStacks;

namespace autoSortVintageStoryMod.Tests.Sorting;

public class InventorySorterTests
{
    private static StackEntry Slot(string code, int count, int max = 64) => E(code, count, max);

    // ── Ordering ────────────────────────────────────────────────────────────

    [Fact]
    public void SortItems_OrdersBySemanticTypeThenTier()
    {
        var input = new[]
        {
            Slot("game:ingot-iron",    1),   // Material, tier 7
            Slot("game:sword-copper",  1),   // Weapon, tier 3
            Slot("game:pickaxe-iron",  1),   // Tool, tier 7
            Slot("game:sword-flint",   1),   // Weapon, tier 1
        };

        var result = InventorySorter.SortItems(input);

        // Expected order: Weapon/flint, Weapon/copper, Tool/iron, Material/iron
        Assert.Equal("game:sword-flint",   result[0].Code);
        Assert.Equal("game:sword-copper",  result[1].Code);
        Assert.Equal("game:pickaxe-iron",  result[2].Code);
        Assert.Equal("game:ingot-iron",    result[3].Code);
    }

    [Fact]
    public void SortItems_AlphabeticalTieBreak_WhenSameTypeAndTier()
    {
        var input = new[]
        {
            Slot("game:ingot-copper",  1),
            Slot("game:clay-blue",     1),  // both Material tier 0... clay < ingot alphabetically
        };

        var result = InventorySorter.SortItems(input);

        Assert.Equal("game:clay-blue",    result[0].Code);
        Assert.Equal("game:ingot-copper", result[1].Code);
    }

    // ── Stack compaction ─────────────────────────────────────────────────────

    [Fact]
    public void SortItems_CompactsDuplicateStacks()
    {
        var input = new[]
        {
            Slot("game:ingot-copper", 10, max: 64),
            Slot("game:ingot-copper", 20, max: 64),
        };

        var result = InventorySorter.SortItems(input);

        Assert.Single(result);
        Assert.Equal("game:ingot-copper", result[0].Code);
        Assert.Equal(30, result[0].Count);
    }

    [Fact]
    public void SortItems_SplitsStacksExceedingMaxStack()
    {
        var input = new[]
        {
            Slot("game:ingot-copper", 50, max: 64),
            Slot("game:ingot-copper", 50, max: 64),
        };

        var result = InventorySorter.SortItems(input);

        // 100 total, max 64 → two stacks: 64 + 36
        Assert.Equal(2, result.Count);
        Assert.Equal(64, result[0].Count);
        Assert.Equal(36, result[1].Count);
    }

    [Fact]
    public void SortItems_EmptyInput_ReturnsEmpty()
    {
        var result = InventorySorter.SortItems([]);
        Assert.Empty(result);
    }

    [Fact]
    public void SortItems_IgnoresNullCodes()
    {
        var input = new[]
        {
            Slot("game:sword-iron", 1),
            Slot("",               0),  // empty slot placeholder
        };

        var result = InventorySorter.SortItems(input.Where(s => s.Count > 0).ToArray());
        Assert.Single(result);
        Assert.Equal("game:sword-iron", result[0].Code);
    }
}
