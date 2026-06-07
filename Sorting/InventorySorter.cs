using Vintagestory.API.Common;

namespace autoSortVintageStoryMod.Sorting;

public static class InventorySorter
{
    /// <summary>
    /// Pure sorting + compaction function. No VS API dependency.
    /// Input: a sequence of (Code, Count, MaxStack) tuples representing non-empty slots.
    /// Returns a new list sorted by (SemanticType, MaterialTier, Code) with stacks compacted.
    /// </summary>
    public static List<(string Code, int Count, int MaxStack)> SortItems(
        IReadOnlyList<(string Code, int Count, int MaxStack)> items)
    {
        // 1. Compact: merge equal codes up to MaxStack
        var grouped = new Dictionary<string, (int Total, int MaxStack)>(StringComparer.Ordinal);

        foreach (var (code, count, max) in items)
        {
            if (string.IsNullOrEmpty(code) || count <= 0) continue;
            if (grouped.TryGetValue(code, out var existing))
                grouped[code] = (existing.Total + count, max);
            else
                grouped[code] = (count, max);
        }

        // 2. Split stacks exceeding MaxStack
        var flat = new List<(string Code, int Count, int MaxStack)>();
        foreach (var (code, (total, max)) in grouped)
        {
            var remaining = total;
            while (remaining > 0)
            {
                var take = Math.Min(remaining, max);
                flat.Add((code, take, max));
                remaining -= take;
            }
        }

        // 3. Sort by (SemanticType, BaseName, MaterialTier, Code).
        //    BaseName clusters every variant of an item together (all gears next to
        //    each other) so the layout is compact per object type; the full key is
        //    deterministic so identical inventories always produce the same layout
        //    (top→bottom, left→right) and items never drift between sorts.
        flat.Sort((a, b) =>
        {
            var ta = (int)ItemClassifier.Classify(a.Code);
            var tb = (int)ItemClassifier.Classify(b.Code);
            if (ta != tb) return ta.CompareTo(tb);

            var na = ItemClassifier.BaseName(a.Code);
            var nb = ItemClassifier.BaseName(b.Code);
            var nameCmp = string.Compare(na, nb, StringComparison.Ordinal);
            if (nameCmp != 0) return nameCmp;

            var ma = ItemClassifier.MaterialTier(a.Code);
            var mb = ItemClassifier.MaterialTier(b.Code);
            if (ma != mb) return ma.CompareTo(mb);

            return string.Compare(a.Code, b.Code, StringComparison.Ordinal);
        });

        return flat;
    }

    /// <summary>
    /// VS-aware adapter: reads all slots from <paramref name="inventory"/>,
    /// calls SortItems, writes sorted stacks back, clears trailing slots.
    /// </summary>
    public static void Sort(IInventory inventory)
    {
        // Snapshot non-empty slots
        var snapshot = new List<(string Code, int Count, int MaxStack)>();
        // Pre-build a frozen clone lookup BEFORE any write-back (keyed by code → queue of clones)
        var clonePool = new Dictionary<string, Queue<ItemStack>>(StringComparer.Ordinal);

        foreach (var slot in inventory)
        {
            if (slot.Itemstack == null) continue;
            var code = slot.Itemstack.Collectible.Code.Path;
            var count = slot.Itemstack.StackSize;
            var max = slot.Itemstack.Collectible.MaxStackSize;

            snapshot.Add((code, count, max));

            // Clone the stack now, before any modification
            var clone = slot.Itemstack.Clone();
            if (!clonePool.TryGetValue(code, out var queue))
                clonePool[code] = queue = new Queue<ItemStack>();
            queue.Enqueue(clone);
        }

        var sorted = SortItems(snapshot);

        // Write back using the frozen clone pool
        var slotList = inventory.ToList();
        int slotIndex = 0;

        foreach (var (code, count, _) in sorted)
        {
            if (slotIndex >= slotList.Count) break;
            var slot = slotList[slotIndex];

            // Pick a clone from the frozen pool (first available for this code)
            ItemStack? source = null;
            if (clonePool.TryGetValue(code, out var pool) && pool.Count > 0)
            {
                source = pool.Dequeue();
                source.StackSize = count;
            }

            if (source != null)
            {
                slot.Itemstack = source;
                slot.MarkDirty();
            }

            slotIndex++;
        }

        // Clear remaining slots
        while (slotIndex < slotList.Count)
        {
            var slot = slotList[slotIndex];
            if (slot.Itemstack != null)
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
            slotIndex++;
        }
    }
}
