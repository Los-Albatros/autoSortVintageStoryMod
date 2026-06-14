using autoSortVintageStoryMod.Sorting;
using Xunit;
using static autoSortVintageStoryMod.Tests.Sorting.TestStacks;

namespace autoSortVintageStoryMod.Tests.Sorting;

public class NetworkDistributorTests
{
    // Helper: build a chest graph entry
    private static ChestData Chest(params string[] codes)
        => new ChestData(codes.Select(c => E(c, 1)).ToList());

    // ── BFS termination ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeDistribution_DoesNotInfiniteLoop_WithCyclicGraph()
    {
        // A <-> B <-> C (all neighbours of each other)
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:sword-iron", "game:sword-copper"),
            ["B"] = Chest("game:sword-flint"),
            ["C"] = Chest("game:ingot-iron"),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B", "C"],
            ["B"] = ["A", "C"],
            ["C"] = ["A", "B"],
        };

        // Should not throw or hang
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            maxDepth: 3,
            threshold: 0.70);

        Assert.NotNull(result);
    }

    [Fact]
    public void ComputeDistribution_RespectsMaxDepth()
    {
        // Chain: A -> B -> C -> D
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-iron"),
            ["B"] = Chest("game:ingot-iron"),
            ["C"] = Chest("game:ingot-iron"),
            ["D"] = Chest("game:sword-iron", "game:sword-copper", "game:sword-flint"),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = ["D"],
            ["D"] = [],
        };

        // maxDepth 2 → A, B, C visited; D should NOT be discovered
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            maxDepth: 2,
            threshold: 0.70);

        Assert.DoesNotContain("D", result.VisitedChests);
    }

    // ── Specialisation detection ─────────────────────────────────────────────

    [Fact]
    public void ComputeDistribution_DetectsSpecialisedChest()
    {
        // B has 3 weapons (100% → specialised)
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-copper"),
            ["B"] = Chest("game:sword-iron", "game:sword-copper", "game:spear-iron"),
        };
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => id == "A" ? ["B"] : [],
            maxDepth: 3,
            threshold: 0.70);

        Assert.True(result.Specialists.ContainsKey(SemanticType.Weapon));
        Assert.Contains("B", result.Specialists[SemanticType.Weapon]);
    }

    [Fact]
    public void ComputeDistribution_DoesNotMarkNonSpecialisedChest()
    {
        // B has 2 weapons + 2 materials = 50% each → not specialised at 0.70
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-copper"),
            ["B"] = Chest("game:sword-iron", "game:sword-copper", "game:ingot-iron", "game:ingot-copper"),
        };
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => id == "A" ? ["B"] : [],
            maxDepth: 3,
            threshold: 0.70);

        Assert.False(result.Specialists.ContainsKey(SemanticType.Weapon));
    }

    // ── Transfer plan ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeDistribution_PlansTransferToSpecialist()
    {
        // A has mixed items; B specialises in weapons
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-iron", "game:sword-copper"),
            ["B"] = Chest("game:sword-iron", "game:sword-flint", "game:spear-iron"),
        };
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => id == "A" ? ["B"] : [],
            maxDepth: 3,
            threshold: 0.70);

        // sword-copper from A should be planned for transfer to B
        var transfer = result.Transfers.FirstOrDefault(
            t => t.ItemIdentity.Code == "game:sword-copper" && t.SourceId == "A" && t.TargetId == "B");
        Assert.NotNull(transfer);
    }

    [Fact]
    public void ComputeDistribution_DoesNotPlanTransfer_WhenNoMatchingSpecialist()
    {
        // No specialist for Material in the network
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-iron", "game:sword-copper"),
            ["B"] = Chest("game:sword-iron", "game:sword-flint"),
        };
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => id == "A" ? ["B"] : [],
            maxDepth: 3,
            threshold: 0.70);

        // ingot-iron (Material) has no Material specialist → no transfer
        var noTransfer = result.Transfers.All(t => t.ItemIdentity.Code != "game:ingot-iron");
        Assert.True(noTransfer);
    }
}

public class CompactLayoutTests
{
    [Fact]
    public void PacksSequentially_LeavesTrailingChestsEmpty()
    {
        var pooled = new List<StackEntry>
        {
            E("game:ingot-iron", 10),
            E("game:ingot-copper", 5),
            E("game:plank-oak", 8),
        };
        var layout = NetworkDistributor.ComputeCompactLayout(pooled, new[] { 4, 4, 4 });

        Assert.Equal(3, layout[0].Count);
        Assert.Empty(layout[1]);
        Assert.Empty(layout[2]);
    }

    [Fact]
    public void MergesDuplicatesAcrossChests()
    {
        var pooled = new List<StackEntry>
        {
            E("game:bone", 12, 32),
            E("game:bone", 9, 32),
        };
        var layout = NetworkDistributor.ComputeCompactLayout(pooled, new[] { 8, 8 });

        var boneStacks = layout.SelectMany(c => c).Where(i => i.Code == "game:bone").ToList();
        Assert.Single(boneStacks);
        Assert.Equal(21, boneStacks[0].Count);
        Assert.Empty(layout[1]);
    }

    [Fact]
    public void Overflows_WhenItExceedsOneChest()
    {
        var pooled = new List<StackEntry> { E("game:bone", 200, 64) };
        var layout = NetworkDistributor.ComputeCompactLayout(pooled, new[] { 2, 2, 4 });

        Assert.Equal(2, layout[0].Count);
        Assert.Equal(2, layout[1].Count);
        Assert.Empty(layout[2]);
        Assert.Equal(200, layout.SelectMany(c => c).Sum(i => i.Count));
    }

    [Fact]
    public void SmallRoom_PacksMultipleTypesPerChest()
    {
        var pooled = new List<StackEntry>
        {
            E("game:sword-iron", 1), E("game:ingot-iron", 1),
            E("game:plank-oak", 1), E("game:bone", 1), E("game:gear-rusty", 1),
        };
        var layout = NetworkDistributor.ComputeCompactLayout(pooled, new[] { 8 });

        Assert.Equal(5, layout[0].Count);
    }

    [Fact]
    public void IsIdempotent()
    {
        var pooled = new List<StackEntry>
        {
            E("game:ingot-iron", 30), E("game:gear-rusty", 12), E("game:bone", 5, 32),
        };
        var caps = new[] { 6, 6, 6 };
        var first = NetworkDistributor.ComputeCompactLayout(pooled, caps);

        var repooled = first.SelectMany(c => c).ToList();
        var second = NetworkDistributor.ComputeCompactLayout(repooled, caps);

        Assert.Equal(Sig(first), Sig(second));
    }
}

public class ValenceLayoutTests
{
    [Fact]
    public void SpreadsOneResourcePerChest_LargeRoom_NoGaps()
    {
        // 3 resources (gear, ingot, plank), 5 chests → one per chest, last two empty.
        var pooled = new List<StackEntry>
        {
            E("game:ingot-iron", 1),
            E("game:gear-rusty", 1),
            E("game:plank-oak", 1),
        };
        var layout = NetworkDistributor.ComputeValenceLayout(pooled, new[] { 4, 4, 4, 4, 4 });

        int used = layout.Count(c => c.Count > 0);
        Assert.Equal(3, used);                                   // spread across 3 chests
        Assert.True(layout[0].Count > 0 && layout[1].Count > 0 && layout[2].Count > 0);
        Assert.Empty(layout[3]);
        Assert.Empty(layout[4]);                                 // empties only at the far end
    }

    [Fact]
    public void UsesEveryChest_BeforeDoublingUp()
    {
        // 11 distinct items, 10 chests → all 10 chests used, exactly one holds two.
        var pooled = Enumerable.Range(0, 11)
            .Select(i => E($"game:item{i:D2}", 1)).ToList();
        var caps = Enumerable.Repeat(16, 10).ToArray();

        var layout = NetworkDistributor.ComputeValenceLayout(pooled, caps);

        Assert.Equal(10, layout.Count(c => c.Count > 0));   // no empty chest
        Assert.Equal(1, layout.Count(c => c.Count == 2));   // exactly one doubled
    }

    [Fact]
    public void DoublesUp_WhenMoreResourcesThanChests()
    {
        // 4 resources, 2 chests → 2 resources per chest.
        var pooled = new List<StackEntry>
        {
            E("game:ingot-iron", 1), E("game:gear-rusty", 1),
            E("game:plank-oak", 1), E("game:bone", 1),
        };
        var layout = NetworkDistributor.ComputeValenceLayout(pooled, new[] { 10, 10 });

        Assert.Equal(2, layout[0].Count);
        Assert.Equal(2, layout[1].Count);
    }

    [Fact]
    public void BigResourceOverflows_WithoutGaps()
    {
        // One resource of 200 bones (4 stacks), chests of 2,2,4 slots.
        var pooled = new List<StackEntry> { E("game:bone", 200, 64) };
        var layout = NetworkDistributor.ComputeValenceLayout(pooled, new[] { 2, 2, 4 });

        Assert.Equal(2, layout[0].Count);
        Assert.Equal(2, layout[1].Count);
        Assert.Empty(layout[2]);
        Assert.Equal(200, layout.SelectMany(c => c).Sum(i => i.Count));
    }

    [Fact]
    public void IsIdempotent()
    {
        var pooled = new List<StackEntry>
        {
            E("game:ingot-iron", 80), E("game:gear-rusty", 12), E("game:bone", 5, 32),
        };
        var caps = new[] { 6, 6, 6, 6 };
        var first = NetworkDistributor.ComputeValenceLayout(pooled, caps);
        var second = NetworkDistributor.ComputeValenceLayout(first.SelectMany(c => c).ToList(), caps);

        Assert.Equal(Sig(first), Sig(second));
    }
}

public class CascadeTests
{
    private static ChestData Chest(params string[] codes)
        => new ChestData(codes.Select(c => E(c, 1)).ToList());

    private static Dictionary<string, List<string>> Chain(params string[] ids)
    {
        var result = new Dictionary<string, List<string>>();
        for (int i = 0; i < ids.Length; i++)
            result[ids[i]] = i + 1 < ids.Length ? [ids[i + 1]] : [];
        return result;
    }

    [Fact]
    public void ComputeCascadePure_PropagatesItemsAcrossWaves()
    {
        var chests = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-iron", "game:sword-copper"),
            ["B"] = Chest("game:sword-iron", "game:sword-flint", "game:spear-iron"),
            ["C"] = Chest("game:sword-steel"),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["A", "C"],
            ["C"] = ["B"],
        };

        var stats = NetworkDistributor.ComputeCascadePure(
            origin: "A",
            chests: chests,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            threshold: 0.70,
            maxIterations: 20);

        Assert.True(stats.TotalMoved > 0);
        Assert.DoesNotContain(chests["A"].Items, i => i.Code == "game:sword-copper");
        Assert.Contains(chests["B"].Items, i => i.Code == "game:sword-copper");
    }

    [Fact]
    public void ComputeCascadePure_ChainPropagation_ThreeChests()
    {
        var chests = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-copper", "game:sword-iron"),
            ["B"] = new ChestData([E("game:ingot-iron", 3), E("game:ingot-copper", 3)]),
            ["C"] = Chest("game:sword-flint", "game:sword-copper", "game:spear-iron"),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["A", "C"],
            ["C"] = ["B"],
        };

        var stats = NetworkDistributor.ComputeCascadePure(
            origin: "A",
            chests: chests,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            threshold: 0.70,
            maxIterations: 20);

        Assert.DoesNotContain(chests["A"].Items, i => i.Code == "game:ingot-copper");
        Assert.Contains(chests["B"].Items, i => i.Code == "game:ingot-copper");
    }

    [Fact]
    public void ComputeCascadePure_ProcessesEachChestOnce()
    {
        var chests = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-iron", "game:torch-up"),
            ["B"] = Chest("game:bread-rye",  "game:sword-iron"),
            ["C"] = Chest("game:pickaxe-iron", "game:candle"),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B", "C"],
            ["B"] = ["A", "C"],
            ["C"] = ["A", "B"],
        };

        var stats = NetworkDistributor.ComputeCascadePure(
            origin: "A",
            chests: chests,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            threshold: 0.70,
            maxIterations: 20);

        Assert.True(stats.ProcessedChests.Count <= 3);
        Assert.Equal(0, stats.TotalMoved);
    }

    [Fact]
    public void ComputeCascadePure_StopsAtMaxIterations()
    {
        var chests = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:ingot-iron"),
            ["B"] = Chest("game:ingot-iron"),
            ["C"] = Chest("game:ingot-iron"),
            ["D"] = Chest("game:ingot-iron"),
            ["E"] = Chest("game:sword-iron", "game:sword-copper", "game:spear-iron"),
        };
        var neighbours = Chain("A", "B", "C", "D", "E");

        var stats = NetworkDistributor.ComputeCascadePure(
            origin: "A",
            chests: chests,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            threshold: 0.70,
            maxIterations: 2);

        Assert.True(stats.ProcessedChests.Count <= 2);
    }

    [Fact]
    public void ComputeCascadePure_StopsEarlyWhenNoItemsMoved()
    {
        var chests = new Dictionary<string, ChestData>
        {
            ["A"] = Chest("game:sword-iron", "game:sword-copper", "game:spear-iron"),
            ["B"] = Chest("game:ingot-iron", "game:ingot-copper", "game:ore-malachite-medium"),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["A"],
        };

        var stats = NetworkDistributor.ComputeCascadePure(
            origin: "A",
            chests: chests,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            threshold: 0.70,
            maxIterations: 50);

        Assert.Equal(0, stats.TotalMoved);
        Assert.DoesNotContain("B", stats.ProcessedChests);
    }
}

public class CapacityRoutingTests
{
    private static ChestData Full(int slots, params (string code, int count)[] items)
    {
        var list = items.Select(i => E(i.code, i.count)).ToList();
        return new ChestData(list, slots);
    }

    [Fact]
    public void ComputeDistribution_SpillsToSecondaryWhenPrimaryFull()
    {
        // B is completely full (36/36 slots, each at max stack). Wood from A should go to C instead.
        var bItems = Enumerable.Range(0, 36).Select(_ => E("game:log-birch", 64)).ToList();
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Full(36, ("game:log-birch", 5)),
            ["B"] = new ChestData(bItems, 36),
            ["C"] = Full(36, ("game:log-oak", 2), ("game:log-birch", 1)),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B", "C"],
            ["B"] = ["A", "C"],
            ["C"] = ["A", "B"],
        };
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            maxDepth: 1,
            threshold: 0.70);

        var transfer = result.Transfers.FirstOrDefault(
            t => t.ItemIdentity.Code == "game:log-birch" && t.SourceId == "A");
        Assert.NotNull(transfer);
        Assert.Equal("C", transfer!.TargetId);
    }

    [Fact]
    public void ComputeDistribution_PartialFillThenSpill()
    {
        // B has 34/36 slots used (can take 2 more). A has 5 wood items. B takes 2, C takes 3.
        var bItems = Enumerable.Range(0, 34).Select(_ => E("game:log-birch", 1)).ToList();
        var graph = new Dictionary<string, ChestData>
        {
            ["A"] = Full(36, ("game:log-birch", 5)),
            ["B"] = new ChestData(bItems, 36),
            ["C"] = Full(36, ("game:log-oak", 1)),
        };
        var neighbours = new Dictionary<string, List<string>>
        {
            ["A"] = ["B", "C"],
            ["B"] = ["A", "C"],
            ["C"] = ["A", "B"],
        };
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: graph,
            neighboursOf: id => neighbours.GetValueOrDefault(id, []),
            maxDepth: 1,
            threshold: 0.70);

        // B has 2 empty slots → capacity = 2 * 64 = 128 items; all 5 go to primary (B)
        var toB = result.Transfers.Where(t => t.SourceId == "A" && t.TargetId == "B" && t.ItemIdentity.Code == "game:log-birch").Sum(t => t.Count);
        var toC = result.Transfers.Where(t => t.SourceId == "A" && t.TargetId == "C" && t.ItemIdentity.Code == "game:log-birch").Sum(t => t.Count);
        Assert.Equal(5, toB);
        Assert.Equal(0, toC);
        Assert.Equal(5, toB + toC); // all 5 items accounted for
    }

    [Fact]
    public void AvailableCapacity_CountsInItems_NotSlots()
    {
        // B (wood specialist) has 1 slot with 30/64 items → partial space = 34, 35 empty slots * 64 = 2240 total
        // A is mixed (wood + weapons) so it's not a specialist → it sends log-birch to the wood specialist B
        var chest = new ChestData(
            [E("game:log-birch", 30)],
            SlotCount: 36);
        var result = NetworkDistributor.ComputeDistribution(
            origin: "A",
            chests: new Dictionary<string, ChestData>
            {
                // A has wood + weapons (mixed) → not a wood specialist → sends log-birch away
                ["A"] = new ChestData(
                    [E("game:log-birch", 100), E("game:sword-iron", 1, 1), E("game:sword-copper", 1, 1)],
                    36),
                ["B"] = chest,
            },
            neighboursOf: id => id == "A" ? ["B"] : [],
            maxDepth: 1,
            threshold: 0.50);

        // B has 1 slot with 30 items + 35 empty slots → capacity = 34 + 35*64 = 2274; all 100 fit
        var transfer = result.Transfers.FirstOrDefault(t => t.ItemIdentity.Code == "game:log-birch" && t.SourceId == "A");
        Assert.NotNull(transfer);
        Assert.Equal(100, transfer!.Count); // all 100 should go to B
    }
}
