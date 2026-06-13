using Vintagestory.API.Common;

namespace autoSortVintageStoryMod.Sorting;

public static class InventorySorter
{
    /// <summary>
    /// Sorting + compaction function for stack-aware entries.
    /// Input: a sequence of non-empty stack entries.
    /// Returns a new list sorted by (SemanticType, MaterialTier, Code) with only
    /// stack-compatible entries compacted.
    /// </summary>
    public static List<StackEntry> SortItems(IReadOnlyList<StackEntry> items)
    {
        // 1. Compact: merge only truly stack-compatible entries up to MaxStack
        var grouped = new Dictionary<StackIdentity, (int Total, int Order)>();

        foreach (var entry in items)
        {
            if (string.IsNullOrEmpty(entry.Code) || entry.Count <= 0) continue;
            if (grouped.TryGetValue(entry.Identity, out var existing))
                grouped[entry.Identity] = (existing.Total + entry.Count, existing.Order);
            else
                grouped[entry.Identity] = (entry.Count, entry.Order);
        }

        // 2. Split stacks exceeding MaxStack
        var flat = new List<StackEntry>();
        foreach (var (identity, group) in grouped)
        {
            var remaining = group.Total;
            while (remaining > 0)
            {
                var take = Math.Min(remaining, identity.MaxStackSize);
                flat.Add(new StackEntry(identity, take, group.Order));
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

            var codeCmp = string.Compare(a.Code, b.Code, StringComparison.Ordinal);
            if (codeCmp != 0) return codeCmp;

            return a.Order.CompareTo(b.Order);
        });

        return flat;
    }

    /// <summary>
    /// Reads all slots from <paramref name="inventory"/>,
    /// calls SortItems, writes sorted stacks back, clears trailing slots.
    /// </summary>
    public static void Sort(IInventory inventory) => SortSlots(inventory.ToList());

    /// <summary>
    /// Sorts a specific set of slots in place (pool → SortItems → write back, trailing
    /// slots cleared). Lets callers sort a sub-range of an inventory — e.g. a player's
    /// backpack content while leaving the bag slots and hotbar untouched.
    /// </summary>
    public static void SortSlots(IReadOnlyList<ItemSlot> slotList)
    {
        // Snapshot non-empty slots
        var snapshot = new List<StackEntry>();
        // Pre-build a frozen clone lookup BEFORE any write-back (keyed by stack identity → queue of clones)
        var clonePool = new Dictionary<StackIdentity, Queue<ItemStack>>();
        int order = 0;

        foreach (var slot in slotList)
        {
            if (slot.Itemstack == null) continue;
            var identity = new StackIdentity(slot.Itemstack);

            snapshot.Add(new StackEntry(identity, slot.Itemstack.StackSize, order++));

            // Clone the stack now, before any modification
            var clone = slot.Itemstack.Clone();
            if (!clonePool.TryGetValue(identity, out var queue))
                clonePool[identity] = queue = new Queue<ItemStack>();
            queue.Enqueue(clone);
        }

        var sorted = SortItems(snapshot);

        // Write back using the frozen clone pool
        int slotIndex = 0;

        foreach (var entry in sorted)
        {
            if (slotIndex >= slotList.Count) break;
            var slot = slotList[slotIndex];

            // Pick a clone from the frozen pool (first available for this exact stack identity)
            ItemStack? source = null;
            if (clonePool.TryGetValue(entry.Identity, out var pool) && pool.Count > 0)
            {
                source = pool.Dequeue();
                source.StackSize = entry.Count;
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
