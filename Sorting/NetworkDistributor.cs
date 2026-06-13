using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using autoSortVintageStoryMod.Config;

namespace autoSortVintageStoryMod.Sorting;

// ── Data types used by the pure BFS layer ─────────────────────────────────────

/// <summary>Represents a chest's contents as a list of stack-aware entries.</summary>
public record ChestData(List<StackEntry> Items, int SlotCount = 27);

/// <summary>A planned item transfer between two chest IDs.</summary>
public record TransferPlan(string SourceId, string TargetId, StackIdentity ItemIdentity, int Count)
{
}

/// <summary>Result of the pure BFS computation.</summary>
public record DistributionResult(
    HashSet<string> VisitedChests,
    Dictionary<SemanticType, List<string>> Specialists,
    List<TransferPlan> Transfers);

/// <summary>Statistics returned by the pure cascade simulation.</summary>
public record CascadeStats(int Iterations, int TotalMoved, HashSet<string> ProcessedChests);

// ── NetworkDistributor ────────────────────────────────────────────────────────

public static class NetworkDistributor
{
    /// <summary>
    /// Pure BFS-style distribution function over stack-aware chest snapshots.
    /// Discovers the chest network, identifies specialised chests, and produces
    /// a list of transfer plans.
    /// </summary>
    public static DistributionResult ComputeDistribution(
        string origin,
        IReadOnlyDictionary<string, ChestData> chests,
        System.Func<string, IEnumerable<string>> neighboursOf,
        int maxDepth,
        double threshold)
    {
        var visited = new HashSet<string>();
        var specialists = new Dictionary<SemanticType, List<string>>();
        var transfers = new List<TransferPlan>();

        // BFS
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((origin, 0));

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            if (!visited.Add(id)) continue;
            if (!chests.TryGetValue(id, out var data)) continue;

            // Compute type histogram for this chest
            var histogram = new Dictionary<SemanticType, int>();
            int total = 0;
            foreach (var entry in data.Items)
            {
                if (entry.Count <= 0) continue;
                var t = ItemClassifier.Classify(entry.Code);
                histogram[t] = histogram.GetValueOrDefault(t) + 1;
                total++;
            }

            // Specialisation check
            if (total > 0)
            {
                foreach (var (type, cnt) in histogram)
                {
                    if ((double)cnt / total >= threshold)
                    {
                        if (!specialists.ContainsKey(type))
                            specialists[type] = [];
                        specialists[type].Add(id);
                    }
                }
            }

            // Enqueue neighbours if within depth limit
            if (depth < maxDepth)
            {
                foreach (var neighbour in neighboursOf(id))
                {
                    if (!visited.Contains(neighbour))
                        queue.Enqueue((neighbour, depth + 1));
                }
            }
        }

        // Build transfer plans: for each visited chest, find items that belong elsewhere
        foreach (var chestId in visited)
        {
            if (!chests.TryGetValue(chestId, out var data)) continue;

            // Determine this chest's own specialisation (if any)
            SemanticType? ownSpeciality = null;
            {
                var histogram = new Dictionary<SemanticType, int>();
                int total = 0;
                foreach (var entry in data.Items)
                {
                    if (entry.Count <= 0) continue;
                    var t = ItemClassifier.Classify(entry.Code);
                    histogram[t] = histogram.GetValueOrDefault(t) + 1;
                    total++;
                }
                if (total > 0)
                {
                    foreach (var (type, cnt) in histogram)
                    {
                        if ((double)cnt / total >= threshold)
                        { ownSpeciality = type; break; }
                    }
                }
            }

            // Items that don't match this chest's speciality → send to the ONE primary
            // specialist (the one with the most existing items of that type).
            // Overflow items stay in source; subsequent passes try again.
            foreach (var entry in data.Items)
            {
                if (entry.Count <= 0) continue;
                var itemType = ItemClassifier.Classify(entry.Code);

                if (!specialists.TryGetValue(itemType, out var targets)) continue;

                // Consolidation target: the specialist already holding the most items
                // of the same BASE NAME (all gears land together), exact stack identity
                // as the tiebreak. Then spill to the next.
                var baseName = ItemClassifier.BaseName(entry.Code);
                var ordered = targets
                    .Where(t => t != chestId)
                    .OrderByDescending(t =>
                        chests.TryGetValue(t, out var td)
                            ? td.Items.Where(i => ItemClassifier.BaseName(i.Code) == baseName).Sum(i => i.Count)
                            : 0)
                    .ThenByDescending(t =>
                        chests.TryGetValue(t, out var td)
                            ? td.Items.Where(i => i.Identity.Equals(entry.Identity)).Sum(i => i.Count)
                            : 0)
                    .ToList();

                // If this chest is a specialist for this type, only move if another specialist
                // has more of this exact item (consolidation) or this chest is the origin
                // and a better-fitting specialist exists.
                if (ownSpeciality.HasValue && ownSpeciality.Value == itemType)
                {
                    // Check if any other specialist has more of this exact stack identity
                    int ownCount = data.Items.Where(i => i.Identity.Equals(entry.Identity)).Sum(i => i.Count);
                    bool betterExists = ordered.Any(t =>
                        chests.TryGetValue(t, out var td) &&
                        td.Items.Where(i => i.Identity.Equals(entry.Identity)).Sum(i => i.Count) > ownCount);
                    if (!betterExists) continue;
                }

                int remaining = entry.Count;
                foreach (var target in ordered)
                {
                    if (remaining <= 0) break;
                    if (!chests.TryGetValue(target, out var tData)) continue;

                    int canAccept = AvailableCapacity(tData, entry.Identity);
                    if (canAccept <= 0) continue;

                    int send = Math.Min(remaining, canAccept);
                    transfers.Add(new TransferPlan(chestId, target, entry.Identity, send));
                    remaining -= send;
                }
            }
        }

        return new DistributionResult(visited, specialists, transfers);
    }

    /// <summary>
    /// VS-aware distribution entry point.
    /// Takes repeated global passes over the entire same-group network until
    /// no items move (stable) or <see cref="SortConfig.MaxCascadeIterations"/> is reached.
    /// Each pass: fresh snapshot → plan all transfers globally → apply → sort changed chests.
    /// </summary>
    public static void DistributeCascade(BlockPos origin, ICoreServerAPI api, SortConfig cfg)
    {
        try
        {
            var originInv = GetInventory(origin, api, cfg);
            if (originInv == null) return;

            // Group by BLOCK code, not inventory ClassName: chests and storage vessels
            // (jars) share the inventory class "chest", so only the block code
            // (chest- vs storagevessel-) tells them apart and keeps them separate.
            var originKind = BlockKind(origin, api);

            // Never act on an excluded container (collapsed/ruined trunk, or any
            // retrieve-only loot container the player can't place items into).
            if (IsExcludedContainer(origin, api, cfg)) return;

            var containerGroup = cfg.GetContainerGroup(originKind);

            // Sort the triggering chest first
            try { InventorySorter.Sort(originInv); }
            catch (Exception ex) { api.Logger.Warning($"[AutoSort] Sort error at {origin}: {ex.Message}"); }

            // Flood the room's open space to find the same-group containers it contains.
            // Walls/doors/stairs bound the flood, so the network is exactly the room and
            // never leaks into the next one. An open/huge area returns just the origin.
            var containerPositions = ScanContainerPositions(origin, api, cfg, containerGroup);
            if (containerPositions.Count <= 1) return; // alone → already sorted internally

            // If any OTHER container in the network is still open (another player, or
            // another chest left open), defer: the last one to close triggers the sort.
            // The just-closed origin is excluded — at this point it may still report
            // itself as open, which would otherwise block its own sort forever.
            if (FirstOpenContainer(containerPositions, api, cfg, origin) != null) return;

            // Lay out the whole group as one unit, anchored to the lowest container (a
            // stable reference, same whichever chest you close). Default ("valence")
            // spreads each item kind across its own chest; CompactRoom packs densely.
            // Both leave no empty chest between filled ones and are deterministic.
            var anchor = ResolveLayoutAnchor(containerPositions);
            try { ApplyLayout(containerPositions, anchor, api, cfg); }
            catch (Exception ex) { api.Logger.Warning($"[AutoSort] Layout error at {origin}: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            api.Logger.Warning($"[AutoSort] Distribution error at {origin}: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Stable, trigger-independent reference the layout packs from: the lexicographically
    /// smallest container in the network, so the result is identical whichever chest was
    /// just closed.
    /// </summary>
    private static BlockPos ResolveLayoutAnchor(List<BlockPos> positions)
        => positions.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).First();

    /// <summary>
    /// Pure compaction layout. Pools all items, sorts and compacts them (same keys as
    /// the in-chest sort), then packs the result into the given chests in order, filling
    /// each up to its slot count and leaving trailing chests empty. Duplicate stacks
    /// scattered across chests merge naturally. Deterministic, hence idempotent.
    /// </summary>
    public static List<List<StackEntry>> ComputeCompactLayout(
        IReadOnlyList<StackEntry> pooled,
        IReadOnlyList<int> chestSlotCounts)
    {
        var sorted = InventorySorter.SortItems(pooled);
        var result = new List<List<StackEntry>>(chestSlotCounts.Count);

        int idx = 0;
        foreach (var cap in chestSlotCounts)
        {
            var chest = new List<StackEntry>();
            for (int i = 0; i < cap && idx < sorted.Count; i++)
                chest.Add(sorted[idx++]);
            result.Add(chest);
        }

        // Safety: leftover stacks (shouldn't happen, pool came from these chests) go last.
        while (idx < sorted.Count && result.Count > 0)
            result[^1].Add(sorted[idx++]);

        return result;
    }

    /// <summary>
    /// Pure "valence" layout. Each distinct base item family is a resource that ideally
    /// gets its own chest, spreading across as many chests as the room offers. When there
    /// are more distinct items than chests, chests take 2, then 3… resources (balanced),
    /// like filling electron shells. Chests fill in anchor order with no empty chest
    /// between filled ones; a resource too big for one chest overflows into the next.
    /// Deterministic, hence idempotent.
    /// </summary>
    public static List<List<StackEntry>> ComputeValenceLayout(
        IReadOnlyList<StackEntry> pooled,
        IReadOnlyList<int> chestSlotCounts,
        IReadOnlyList<Dictionary<string, int>> existingFamilyCounts)
    {
        var sorted = InventorySorter.SortItems(pooled);
        int n = chestSlotCounts.Count;
        var result = new List<List<StackEntry>>(n);
        for (int i = 0; i < n; i++) result.Add(new());
        if (n == 0 || sorted.Count == 0) return result;

        // Group the sorted stacks into resources: one resource per distinct base item family.
        // Trait-distinct stacks and variant codes must stay separate for merging, but
        // related variants should still lay out together so currant cuttings don't peel
        // away from the rest of the cuttings into a crock vessel.
        var resources = new List<List<StackEntry>>();
        string? curBaseName = null;
        foreach (var stack in sorted)
        {
            var baseName = ItemClassifier.BaseName(stack.Code);
            if (curBaseName == null || baseName != curBaseName)
            {
                resources.Add(new());
                curBaseName = baseName;
            }
            resources[^1].Add(stack);
        }

        var remainingSlots = chestSlotCounts.ToArray();
        var resourceCounts = new int[n];

        foreach (var resource in resources)
        {
            var familyKey = ItemClassifier.BaseName(resource[0].Code);
            var preferredChests = Enumerable.Range(0, n)
                .Where(i => i < existingFamilyCounts.Count &&
                            existingFamilyCounts[i].TryGetValue(familyKey, out var count) &&
                            count > 0)
                .OrderByDescending(i => existingFamilyCounts[i][familyKey])
                .ThenBy(i => i)
                .ToList();

            var fallbackChests = Enumerable.Range(0, n)
                .Where(i => !preferredChests.Contains(i))
                .OrderBy(i => resourceCounts[i])
                .ThenBy(i => i)
                .ToList();

            var candidateChests = preferredChests.Concat(fallbackChests).ToList();
            int candidateIndex = 0;
            int? primaryChest = null;

            foreach (var stack in resource)
            {
                while (candidateIndex < candidateChests.Count &&
                       remainingSlots[candidateChests[candidateIndex]] <= 0)
                    candidateIndex++;

                if (candidateIndex >= candidateChests.Count) break;

                int chest = candidateChests[candidateIndex];
                result[chest].Add(stack);
                remainingSlots[chest]--;
                primaryChest ??= chest;
            }

            if (primaryChest.HasValue)
                resourceCounts[primaryChest.Value]++;
        }

        return result;
    }

    /// <summary>
    /// Pools every container in the network (clearing them), computes the target layout —
    /// dense compaction or the spread "valence" layout depending on
    /// <see cref="SortConfig.CompactRoom"/> — and writes the items back, chests ordered
    /// from the anchor. Pooling real (cloned) stacks prevents duplication.
    /// </summary>
    private static void ApplyLayout(List<BlockPos> positions, BlockPos anchor, ICoreServerAPI api, SortConfig cfg)
    {
        var ordered = positions
            .OrderBy(p => DistSq(p, anchor))
            .ThenBy(PosKey, StringComparer.Ordinal)
            .ToList();

        var invs = new List<IInventory>();
        var orderedPos = new List<BlockPos>();
        foreach (var p in ordered)
        {
            var inv = GetInventory(p, api, cfg);
            if (inv == null) continue;
            invs.Add(inv);
            orderedPos.Add(p);
        }
        if (invs.Count == 0) return;

        // Pool every stack (clearing slots), keeping frozen clones keyed by stack identity.
        var pooled = new List<StackEntry>();
        var existingFamilyCounts = new List<Dictionary<string, int>>(invs.Count);
        var clonePool = new Dictionary<StackIdentity, Queue<ItemStack>>();
        int order = 0;
        foreach (var inv in invs)
        {
            var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var slot in inv)
            {
                if (slot.Itemstack == null) continue;
                var identity = new StackIdentity(slot.Itemstack);
                pooled.Add(new StackEntry(identity, slot.Itemstack.StackSize, order++));
                var familyKey = ItemClassifier.BaseName(identity.Code);
                familyCounts[familyKey] = familyCounts.GetValueOrDefault(familyKey) + slot.Itemstack.StackSize;
                var clone = slot.Itemstack.Clone();
                if (!clonePool.TryGetValue(identity, out var q))
                    clonePool[identity] = q = new Queue<ItemStack>();
                q.Enqueue(clone);
                slot.Itemstack = null;
                slot.MarkDirty();
            }
            existingFamilyCounts.Add(familyCounts);
        }

        var slotCounts = invs.Select(i => i.Count).ToList();
        var layout = cfg.CompactRoom
            ? ComputeCompactLayout(pooled, slotCounts)
            : ComputeValenceLayout(pooled, slotCounts, existingFamilyCounts);

        for (int c = 0; c < invs.Count; c++)
        {
            var slotList = invs[c].ToList();
            int idx = 0;
            foreach (var entry in layout[c])
            {
                if (idx >= slotList.Count) break;
                if (!clonePool.TryGetValue(entry.Identity, out var q) || q.Count == 0) continue;
                var stack = q.Dequeue();
                stack.StackSize = entry.Count;
                slotList[idx].Itemstack = stack;
                slotList[idx].MarkDirty();
                idx++;
            }
        }

        // Push updated contents to nearby clients (real-time overlay).
        foreach (var p in orderedPos)
            api.World.BlockAccessor.GetBlockEntity(p)?.MarkDirty(true);
    }

    private static long DistSq(BlockPos a, BlockPos b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Block code path at <paramref name="pos"/> (e.g. "chest-…", "storagevessel-…").</summary>
    private static string BlockKind(BlockPos pos, ICoreServerAPI api)
        => api.World.BlockAccessor.GetBlock(pos)?.Code?.Path ?? "";

    /// <summary>
    /// True if a container must be left untouched: its block code matches one of
    /// <see cref="SortConfig.IgnoredContainerCodes"/>, or the block is retrieve-only
    /// (a read-only loot container the player can't place items into).
    /// </summary>
    private static bool IsExcludedContainer(BlockPos pos, ICoreServerAPI api, SortConfig cfg)
    {
        var block = api.World.BlockAccessor.GetBlock(pos);
        if (block?.Code == null) return false;

        var path = block.Code.Path;
        if (cfg.IgnoredContainerCodes.Any(ig => path.Contains(ig, StringComparison.OrdinalIgnoreCase)))
            return true;

        try
        {
            var ro = block.Attributes?["retrieveOnly"];
            if (ro != null && ro.Exists)
            {
                if (ro.AsBool(false)) return true;
                var s = ro.AsString(null);
                if (s != null && (s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                                  s.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { /* attribute shape varies across mods — ignore on error */ }

        return false;
    }

    /// <summary>
    /// True if <paramref name="pos"/> is on the same storey as <paramref name="origin"/>:
    /// no solid floor/ceiling layer lies between their Y levels (checked in the
    /// candidate's own column), and the optional vertical cap isn't exceeded.
    /// </summary>
    private static bool SameStorey(BlockPos origin, BlockPos pos, ICoreServerAPI api, SortConfig cfg)
    {
        if (cfg.MaxVerticalSpan > 0 && Math.Abs(pos.Y - origin.Y) > cfg.MaxVerticalSpan)
            return false;
        if (!cfg.SeparateFloors || pos.Y == origin.Y)
            return true;

        int lo = Math.Min(origin.Y, pos.Y);
        int hi = Math.Max(origin.Y, pos.Y);
        var ba = api.World.BlockAccessor;
        for (int y = lo + 1; y < hi; y++)
        {
            var b = ba.GetBlock(new BlockPos(pos.X, y, pos.Z));
            if (b != null && b.Id != 0 && b.SideSolid[BlockFacing.UP.Index])
                return false; // a solid floor/ceiling separates the two storeys
        }
        return true;
    }

    /// <summary>
    /// True if any container in the network is currently opened by a player. Used to hold
    /// off sorting until the last open container in the room is closed.
    /// </summary>
    private static BlockPos? FirstOpenContainer(List<BlockPos> positions, ICoreServerAPI api, SortConfig cfg, BlockPos exclude)
    {
        // Only count a container as "open" if a currently-connected player has it open.
        // Ignores stale entries (e.g. a player who disconnected with a chest open) that
        // would otherwise block the whole room's sorting forever.
        var online = new HashSet<string>();
        foreach (var p in api.World.AllOnlinePlayers) online.Add(p.PlayerUID);

        foreach (var pos in positions)
        {
            if (pos.Equals(exclude)) continue;
            if (GetInventory(pos, api, cfg) is InventoryBase inv &&
                inv.openedByPlayerGUIds.Any(uid => online.Contains(uid)))
                return pos;
        }
        return null;
    }

    private record ChestGraphData(
        Dictionary<string, ChestData> Data,
        Dictionary<string, List<string>> Neighbours,
        Dictionary<string, BlockPos> PosLookup);

    private static readonly Vec3i[] Offsets6 =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>
    /// Discovers the room's container network. First tries flooding the OPEN SPACE around
    /// the chest (stops at walls/doors/stairs → clean per-room separation, a fridge behind
    /// its door stays separate). If the space doesn't close off within
    /// <see cref="SortConfig.MaxRoomCells"/> (an open or not-fully-sealed area), it falls
    /// back to the radius cascade so large/open halls still get full coverage.
    /// </summary>
    private static List<BlockPos> ScanContainerPositions(
        BlockPos origin, ICoreServerAPI api, SortConfig cfg,
        IReadOnlyList<string>? groupFilter = null)
    {
        var byAir = FloodByAir(origin, api, cfg, groupFilter, out bool enclosed);
        if (enclosed && byAir.Count > 1) return byAir;     // sealed room → clean separation
        return FloodByRadius(origin, api, cfg, groupFilter); // open/large → full coverage
    }

    private static List<BlockPos> FloodByAir(
        BlockPos origin, ICoreServerAPI api, SortConfig cfg,
        IReadOnlyList<string>? groupFilter, out bool enclosed)
    {
        var ba = api.World.BlockAccessor;
        int chestCap = cfg.MaxNetworkChests;
        int cellCap = cfg.MaxRoomCells;

        var found = new Dictionary<string, BlockPos> { [PosKey(origin)] = origin };
        var airSeen = new HashSet<string>();
        var queue = new Queue<BlockPos>();

        foreach (var o in Offsets6)
        {
            var p = origin.AddCopy(o.X, o.Y, o.Z);
            if (IsPassable(ba.GetBlock(p)) && airSeen.Add(PosKey(p))) queue.Enqueue(p);
        }

        bool capped = false;
        while (queue.Count > 0 && found.Count < chestCap)
        {
            if (airSeen.Count >= cellCap) { capped = true; break; }
            var cell = queue.Dequeue();

            foreach (var o in Offsets6)
            {
                var nb = cell.AddCopy(o.X, o.Y, o.Z);
                var key = PosKey(nb);
                if (airSeen.Contains(key) || found.ContainsKey(key)) continue;

                if (GetInventory(nb, api, cfg) != null)
                {
                    // No storey check needed here: a solid floor/ceiling is impassable, so
                    // the air flood already can't cross between storeys. Whichever chest
                    // triggers, the same connected air → the same set of containers.
                    if (!IsExcludedContainer(nb, api, cfg) &&
                        (groupFilter == null ||
                         groupFilter.Any(cls => BlockKind(nb, api).Contains(cls, StringComparison.OrdinalIgnoreCase))))
                        found[key] = nb;
                    continue;
                }

                if (IsPassable(ba.GetBlock(nb)) && airSeen.Add(key))
                    queue.Enqueue(nb);
            }
        }

        enclosed = !capped;
        return found.Values.ToList();
    }

    /// <summary>
    /// Radius cascade flood-fill: every found chest scans its own
    /// <see cref="SortConfig.SearchRadiusBlocks"/> sphere. Full coverage of connected
    /// chests regardless of walls — used when the air flood can't seal a room.
    /// </summary>
    private static List<BlockPos> FloodByRadius(
        BlockPos origin, ICoreServerAPI api, SortConfig cfg, IReadOnlyList<string>? groupFilter)
    {
        int radius = cfg.SearchRadiusBlocks;
        int r2 = radius * radius;
        int cap = cfg.MaxNetworkChests;

        var found = new Dictionary<string, BlockPos> { [PosKey(origin)] = origin };
        var queue = new Queue<BlockPos>();
        queue.Enqueue(origin);

        while (queue.Count > 0 && found.Count < cap)
        {
            var center = queue.Dequeue();
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dy * dy + dz * dz > r2) continue;
                var pos = center.AddCopy(dx, dy, dz);
                var key = PosKey(pos);
                if (found.ContainsKey(key)) continue;
                // Pairwise storey check (center↔candidate, not vs the origin) so the
                // connected component is the same whichever chest triggered the sort.
                if (!SameStorey(center, pos, api, cfg)) continue;
                if (GetInventory(pos, api, cfg) == null) continue;
                if (IsExcludedContainer(pos, api, cfg)) continue;
                if (groupFilter != null &&
                    !groupFilter.Any(cls => BlockKind(pos, api).Contains(cls, StringComparison.OrdinalIgnoreCase)))
                    continue;

                found[key] = pos;
                queue.Enqueue(pos);
                if (found.Count >= cap) break;
            }
        }

        return found.Values.ToList();
    }

    /// <summary>
    /// Whether the room flood may pass through this block. Air and non-solid decoration
    /// are passable; walls (solid blocks) and explicit boundaries (doors, stairs, gates,
    /// trapdoors, ladders) are not — they delimit rooms and storeys.
    /// </summary>
    private static bool IsPassable(Block? b)
    {
        if (b == null || b.Id == 0) return true;
        var code = b.Code?.Path;
        if (code != null && (
                code.Contains("door", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("stairs", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("gate", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("trapdoor", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("ladder", StringComparison.OrdinalIgnoreCase)))
            return false;
        return !b.SideSolid[BlockFacing.UP.Index] && !b.SideSolid[BlockFacing.NORTH.Index];
    }

    /// <summary>
    /// Builds a fresh content snapshot from already-known container
    /// <paramref name="positions"/>. Cheap — only reads inventories, no world scan.
    /// </summary>
    private static ChestGraphData BuildGraphFromPositions(
        IReadOnlyList<BlockPos> positions, ICoreServerAPI api, SortConfig cfg)
    {
        var data = new Dictionary<string, ChestData>();
        var posLookup = new Dictionary<string, BlockPos>();
        var keys = new List<string>(positions.Count);

        foreach (var pos in positions)
        {
            var inv = GetInventory(pos, api, cfg);
            if (inv == null) continue; // chest removed since the scan — skip

            var key = PosKey(pos);
            posLookup[key] = pos;
            keys.Add(key);

            var items = new List<StackEntry>();
            foreach (var slot in inv)
            {
                if (slot.Itemstack == null) continue;
                items.Add(new StackEntry(new StackIdentity(slot.Itemstack), slot.Itemstack.StackSize, items.Count));
            }
            data[key] = new ChestData(items, inv.Count);
        }

        // Flat adjacency: every chest is a depth-1 neighbour of every other.
        var neighbours = new Dictionary<string, List<string>>(keys.Count);
        foreach (var key in keys)
            neighbours[key] = keys.Where(k => k != key).ToList();

        return new ChestGraphData(data, neighbours, posLookup);
    }

    private static IInventory? GetInventory(BlockPos pos, ICoreServerAPI api, SortConfig cfg)
    {
        var be = api.World.BlockAccessor.GetBlockEntity(pos);
        if (be == null) return null;

        // VS block entities that hold items implement IBlockEntityContainer
        if (be is not IBlockEntityContainer container) return null;

        var inv = container.Inventory;
        if (inv == null) return null;

        if (cfg.SupportedInventoryClasses.Any(cls =>
            inv.ClassName.Contains(cls, StringComparison.OrdinalIgnoreCase)))
            return inv;

        return null;
    }

    private static int ApplyTransfer(TransferPlan plan, IInventory src, IInventory tgt)
    {
        int moved = 0;
        var srcSlots = src.ToList();
        var tgtSlots = tgt.ToList();

        foreach (var srcSlot in srcSlots)
        {
            if (!plan.ItemIdentity.Matches(srcSlot.Itemstack)) continue;

            // Find a target slot: same item (merge) or empty
            foreach (var tgtSlot in tgtSlots)
            {
                if (srcSlot.Itemstack == null || srcSlot.Itemstack.StackSize <= 0) break;

                bool tgtEmpty = tgtSlot.Itemstack == null;
                bool tgtSameItem = plan.ItemIdentity.Matches(tgtSlot.Itemstack);

                if (!tgtEmpty && !tgtSameItem) continue;

                int maxStack = srcSlot.Itemstack.Collectible.MaxStackSize;
                int tgtCurrent = tgtSlot.Itemstack?.StackSize ?? 0;
                int canFit = maxStack - tgtCurrent;
                if (canFit <= 0) continue;

                int take = Math.Min(srcSlot.Itemstack.StackSize, canFit);

                if (tgtEmpty)
                {
                    tgtSlot.Itemstack = srcSlot.Itemstack.Clone();
                    tgtSlot.Itemstack.StackSize = take;
                }
                else
                {
                    tgtSlot.Itemstack!.StackSize += take;
                }

                srcSlot.Itemstack.StackSize -= take;
                if (srcSlot.Itemstack.StackSize <= 0)
                    srcSlot.Itemstack = null;

                srcSlot.MarkDirty();
                tgtSlot.MarkDirty();
                moved++;
            }
        }
        return moved;
    }

    /// <summary>
    /// For items that couldn't be sent to their specialist (specialist full),
    /// try the 6 physically adjacent block positions as overflow targets.
    /// Only uses containers of the same group as the origin.
    /// </summary>
    private static int TryOverflowToAdjacent(
        BlockPos sourcePos,
        ICoreServerAPI api,
        SortConfig cfg,
        IReadOnlyList<string> containerGroup)
    {
        var srcInv = GetInventory(sourcePos, api, cfg);
        if (srcInv == null) return 0;

        // 6-face adjacency
        var offsets = new[]
        {
            new Vec3i( 1, 0, 0), new Vec3i(-1, 0, 0),
            new Vec3i( 0, 1, 0), new Vec3i( 0,-1, 0),
            new Vec3i( 0, 0, 1), new Vec3i( 0, 0,-1),
        };

        int totalMoved = 0;

        foreach (var slot in srcInv.ToList())
        {
            if (slot.Itemstack == null || slot.Itemstack.StackSize <= 0) continue;

            var identity = new StackIdentity(slot.Itemstack);
            var maxStack = identity.MaxStackSize;

            foreach (var offset in offsets)
            {
                if (slot.Itemstack == null || slot.Itemstack.StackSize <= 0) break;

                var adjPos = sourcePos.AddCopy(offset.X, offset.Y, offset.Z);
                var adjInv = GetInventory(adjPos, api, cfg);
                if (adjInv == null) continue;

                // Stay within same container group
                if (!containerGroup.Any(cls =>
                    adjInv.ClassName.Contains(cls, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Try to merge into an existing stack of the same item, or find an empty slot
                foreach (var tgtSlot in adjInv.ToList())
                {
                    if (slot.Itemstack == null || slot.Itemstack.StackSize <= 0) break;

                    bool empty    = tgtSlot.Itemstack == null;
                    bool sameItem = identity.Matches(tgtSlot.Itemstack);
                    if (!empty && !sameItem) continue;

                    int current = tgtSlot.Itemstack?.StackSize ?? 0;
                    int canFit  = maxStack - current;
                    if (canFit <= 0) continue;

                    int take = Math.Min(slot.Itemstack.StackSize, canFit);

                    if (empty)
                    {
                        tgtSlot.Itemstack = slot.Itemstack.Clone();
                        tgtSlot.Itemstack.StackSize = take;
                    }
                    else
                    {
                        tgtSlot.Itemstack!.StackSize += take;
                    }

                    slot.Itemstack.StackSize -= take;
                    if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;

                    slot.MarkDirty();
                    tgtSlot.MarkDirty();
                    totalMoved += take;
                    break; // moved from this source slot, check next
                }
            }
        }

        return totalMoved;
    }

    private static int AvailableCapacity(ChestData chest, StackIdentity itemIdentity)
    {
        int usedSlots = chest.Items.Count(i => i.Count > 0);
        int emptySlots = Math.Max(0, chest.SlotCount - usedSlots);
        int partialSpace = chest.Items
            .Where(i => i.Identity.Equals(itemIdentity) && i.Count > 0)
            .Sum(i => i.MaxStack - i.Count);
        return partialSpace + emptySlots * itemIdentity.MaxStackSize;
    }

    private static string PosKey(BlockPos p) => $"{p.X},{p.Y},{p.Z}";

    private static BlockPos ParsePos(string key)
    {
        var parts = key.Split(',');
        return new BlockPos(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    /// <summary>
    /// Pure cascade simulation over stack-aware chest snapshots.
    /// Processes chests wave by wave: the origin first, then any chest that received
    /// items, until stable (no moves) or <paramref name="maxIterations"/> is reached.
    /// Mutates <paramref name="chests"/> in-place to reflect item movements.
    /// </summary>
    public static CascadeStats ComputeCascadePure(
        string origin,
        IDictionary<string, ChestData> chests,
        System.Func<string, IEnumerable<string>> neighboursOf,
        double threshold,
        int maxIterations)
    {
        var processed = new HashSet<string>();
        var pending = new Queue<string>();
        pending.Enqueue(origin);

        int iterations = 0;
        int totalMoved = 0;

        while (pending.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var currentId = pending.Dequeue();
            if (!processed.Add(currentId)) continue;
            if (!chests.ContainsKey(currentId)) continue;

            // Wrap mutable dict as read-only for ComputeDistribution
            var readonlyChests = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ChestData>(
                chests is Dictionary<string, ChestData> d
                    ? d
                    : chests.ToDictionary(k => k.Key, v => v.Value));

            var result = ComputeDistribution(
                origin: currentId,
                chests: readonlyChests,
                neighboursOf: neighboursOf,
                maxDepth: 1,
                threshold: threshold);

            foreach (var plan in result.Transfers)
            {
                if (!chests.TryGetValue(plan.SourceId, out var srcData)) continue;
                if (!chests.TryGetValue(plan.TargetId, out var tgtData)) continue;

                var srcItems = srcData.Items.ToList();
                int srcIdx = srcItems.FindIndex(i => i.Identity.Equals(plan.ItemIdentity));
                if (srcIdx < 0) continue;

                var srcItem = srcItems[srcIdx];
                int srcCount = srcItem.Count;
                int take = Math.Min(srcCount, plan.Count);
                if (take <= 0) continue;

                // Deduct from source
                if (srcCount - take <= 0)
                    srcItems.RemoveAt(srcIdx);
                else
                    srcItems[srcIdx] = srcItem with { Count = srcCount - take };
                chests[plan.SourceId] = srcData with { Items = srcItems };

                // Add to target
                var tgtItems = tgtData.Items.ToList();
                int tgtIdx = tgtItems.FindIndex(i => i.Identity.Equals(plan.ItemIdentity));
                if (tgtIdx >= 0)
                {
                    var tgtItem = tgtItems[tgtIdx];
                    tgtItems[tgtIdx] = tgtItem with { Count = tgtItem.Count + take };
                }
                else
                {
                    tgtItems.Add(new StackEntry(plan.ItemIdentity, take, tgtItems.Count));
                }
                chests[plan.TargetId] = tgtData with { Items = tgtItems };

                totalMoved += take;

                if (!processed.Contains(plan.TargetId))
                    pending.Enqueue(plan.TargetId);
            }
        }

        return new CascadeStats(iterations, totalMoved, processed);
    }
}
