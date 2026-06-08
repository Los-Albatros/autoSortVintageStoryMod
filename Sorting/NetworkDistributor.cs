using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using autoSortVintageStoryMod.Config;

namespace autoSortVintageStoryMod.Sorting;

// ── Data types used by the pure BFS layer ─────────────────────────────────────

/// <summary>Represents a chest's contents as a list of (code, count, maxStack) tuples.</summary>
public record ChestData(List<(string Code, int Count, int MaxStack)> Items, int SlotCount = 27);

/// <summary>A planned item transfer between two chest IDs.</summary>
public record TransferPlan(string SourceId, string TargetId, string ItemCode, int Count);

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
    /// Pure BFS function — no VS API dependency.
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
            foreach (var (code, count, _) in data.Items)
            {
                if (count <= 0) continue;
                var t = ItemClassifier.Classify(code);
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
                foreach (var (code, count, _) in data.Items)
                {
                    if (count <= 0) continue;
                    var t = ItemClassifier.Classify(code);
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
            foreach (var (code, count, maxStack) in data.Items)
            {
                if (count <= 0) continue;
                var itemType = ItemClassifier.Classify(code);

                if (!specialists.TryGetValue(itemType, out var targets)) continue;

                // Consolidation target: the specialist already holding the most items
                // of the same BASE NAME (all gears land together), exact code as the
                // tiebreak. Then spill to the next.
                var baseName = ItemClassifier.BaseName(code);
                var ordered = targets
                    .Where(t => t != chestId)
                    .OrderByDescending(t =>
                        chests.TryGetValue(t, out var td)
                            ? td.Items.Where(i => ItemClassifier.BaseName(i.Code) == baseName).Sum(i => i.Count)
                            : 0)
                    .ThenByDescending(t =>
                        chests.TryGetValue(t, out var td)
                            ? td.Items.Where(i => i.Code == code).Sum(i => i.Count)
                            : 0)
                    .ToList();

                // If this chest is a specialist for this type, only move if another specialist
                // has more of this exact item (consolidation) or this chest is the origin
                // and a better-fitting specialist exists.
                if (ownSpeciality.HasValue && ownSpeciality.Value == itemType)
                {
                    // Check if any other specialist has more of this exact code
                    int ownCount = data.Items.Where(i => i.Code == code).Sum(i => i.Count);
                    bool betterExists = ordered.Any(t =>
                        chests.TryGetValue(t, out var td) &&
                        td.Items.Where(i => i.Code == code).Sum(i => i.Count) > ownCount);
                    if (!betterExists) continue;
                }

                int remaining = count;
                foreach (var target in ordered)
                {
                    if (remaining <= 0) break;
                    if (!chests.TryGetValue(target, out var tData)) continue;

                    int canAccept = AvailableCapacity(tData, code, maxStack);
                    if (canAccept <= 0) continue;

                    int send = Math.Min(remaining, canAccept);
                    transfers.Add(new TransferPlan(chestId, target, code, send));
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
            var containerGroup = cfg.GetContainerGroup(originKind);
            var originKey = PosKey(origin);

            // Sort the triggering chest first
            try { InventorySorter.Sort(originInv); }
            catch (Exception ex) { api.Logger.Warning($"[AutoSort] Sort error at {origin}: {ex.Message}"); }

            int totalMoved = 0;

            // Expensive O(radius^3) world scan — done ONCE. Chests don't move between
            // passes, so we cache their positions and only re-read inventories each pass.
            var room = GetEnclosedRoom(origin, api, cfg);
            var roomFilter = BuildRoomFilter(room);
            var containerPositions = ScanContainerPositions(origin, api, cfg, containerGroup, roomFilter);
            if (containerPositions.Count == 0) return;

            // If any container in the network is still open (this player, another player,
            // or another chest left open), defer: the last one to close triggers the sort.
            // This avoids reshuffling items out from under someone who is still editing.
            if (AnyContainerOpen(containerPositions, api, cfg)) return;

            // Compaction layout: pool the whole group and pack it into the chests in
            // door-order, leaving trailing chests empty. Replaces the specialist passes.
            if (cfg.CompactRoom)
            {
                try
                {
                    var anchor = ResolveLayoutAnchor(origin, api, room);
                    ApplyCompactLayout(containerPositions, anchor, api, cfg);
                }
                catch (Exception ex)
                {
                    api.Logger.Warning($"[AutoSort] Compaction error at {origin}: {ex.Message}");
                }
                return;
            }

            // Positions whose contents changed — their block entity is pushed to nearby
            // clients afterwards so the /sort overlay follows the new layout in real time
            // even for chests the player never opened. Origin is always (re)sorted.
            var changed = new HashSet<string> { originKey };

            for (int pass = 0; pass < cfg.MaxCascadeIterations; pass++)
            {
                // Cheap snapshot: re-read only the known container inventories
                var global = BuildGraphFromPositions(containerPositions, api, cfg);
                if (global.Data.Count == 0) break;

                // Single ComputeDistribution call — visits all chests in the flat-adjacency
                // graph (depth-1 = entire network) and plans all transfers at once.
                var result = ComputeDistribution(
                    origin: originKey,
                    chests: global.Data,
                    neighboursOf: id => global.Neighbours.GetValueOrDefault(id, []),
                    maxDepth: 1,
                    threshold: cfg.SpecialisationThreshold);

                int movedThisPass = 0;
                var dirtied = new HashSet<string>();

                foreach (var plan in result.Transfers)
                {
                    if (!global.PosLookup.TryGetValue(plan.SourceId, out var srcPos)) continue;
                    if (!global.PosLookup.TryGetValue(plan.TargetId, out var tgtPos)) continue;

                    var srcInv = GetInventory(srcPos, api, cfg);
                    var tgtInv = GetInventory(tgtPos, api, cfg);
                    if (srcInv == null || tgtInv == null) continue;

                    var moved = ApplyTransfer(plan, srcInv, tgtInv);
                    if (moved > 0)
                    {
                        movedThisPass += moved;
                        dirtied.Add(plan.SourceId);
                        dirtied.Add(plan.TargetId);
                    }
                }

                // Sort every chest that changed
                foreach (var id in dirtied)
                {
                    if (!global.PosLookup.TryGetValue(id, out var pos)) continue;
                    var inv = GetInventory(pos, api, cfg);
                    if (inv == null) continue;
                    try { InventorySorter.Sort(inv); }
                    catch (Exception ex) { api.Logger.Warning($"[AutoSort] Sort error at {pos}: {ex.Message}"); }
                }

                // Overflow: items that couldn't reach a specialist go to adjacent chests
                foreach (var id in global.Data.Keys)
                {
                    if (!global.PosLookup.TryGetValue(id, out var pos)) continue;
                    TryOverflowToAdjacent(pos, api, cfg, containerGroup ?? cfg.SupportedInventoryClasses);
                }

                changed.UnionWith(dirtied);
                totalMoved += movedThisPass;
                if (movedThisPass == 0) break; // stable — nothing moved this pass
            }

            // Push updated contents to nearby clients (real-time overlay).
            foreach (var id in changed)
            {
                var pos = ParsePos(id);
                api.World.BlockAccessor.GetBlockEntity(pos)?.MarkDirty(true);
            }

            api.Logger.VerboseDebug($"[AutoSort] Distribution complete from {origin}: {totalMoved} item(s) moved.");
        }
        catch (Exception ex)
        {
            api.Logger.Warning($"[AutoSort] Distribution error at {origin}: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the enclosed room containing <paramref name="origin"/>, or null when room
    /// restriction is off, the room system is unavailable, or the origin is not inside a
    /// fully enclosed room (open base / oversized space).
    /// </summary>
    private static Vintagestory.GameContent.Room? GetEnclosedRoom(
        BlockPos origin, ICoreServerAPI api, SortConfig cfg)
    {
        if (!cfg.RestrictToSameRoom) return null;
        var registry = api.ModLoader.GetModSystem<Vintagestory.GameContent.RoomRegistry>();
        var room = registry?.GetRoomForPosition(origin);
        if (room == null || room.ExitCount > 0) return null; // not enclosed
        return room;
    }

    /// <summary>
    /// Restricts the chest network to <paramref name="room"/>'s volume (so a closed door
    /// splits two storage rooms). Returns null (no restriction) when room is null. Tests
    /// containment in the bounding cuboid — one cheap check per chest — with a 1-block
    /// tolerance so chests sitting against the room's walls still count.
    /// </summary>
    private static System.Func<BlockPos, bool>? BuildRoomFilter(Vintagestory.GameContent.Room? room)
    {
        if (room == null) return null;
        var loc = room.Location;
        return pos =>
            pos.X >= loc.X1 - 1 && pos.X <= loc.X2 + 1 &&
            pos.Y >= loc.Y1 - 1 && pos.Y <= loc.Y2 + 1 &&
            pos.Z >= loc.Z1 - 1 && pos.Z <= loc.Z2 + 1;
    }

    /// <summary>
    /// Reference point the compaction packs from: the room's door, else a deterministic
    /// corner when several doors exist, else the triggering chest (open / no room).
    /// </summary>
    private static BlockPos ResolveLayoutAnchor(BlockPos origin, ICoreServerAPI api, Vintagestory.GameContent.Room? room)
    {
        if (room == null) return origin;

        var loc = room.Location;
        var doors = new List<BlockPos>();
        var ba = api.World.BlockAccessor;
        for (int x = loc.X1; x <= loc.X2 && doors.Count <= 2; x++)
        for (int y = loc.Y1; y <= loc.Y2 && doors.Count <= 2; y++)
        for (int z = loc.Z1; z <= loc.Z2 && doors.Count <= 2; z++)
        {
            var code = ba.GetBlock(new BlockPos(x, y, z))?.Code?.Path;
            if (code != null && code.Contains("door", StringComparison.OrdinalIgnoreCase))
                doors.Add(new BlockPos(x, y, z));
        }

        if (doors.Count == 1) return doors[0];                       // single door
        if (doors.Count >= 2) return new BlockPos(loc.X1, loc.Y1, loc.Z1); // fixed corner
        return origin;                                              // no door
    }

    /// <summary>
    /// Pure compaction layout. Pools all items, sorts and compacts them (same keys as
    /// the in-chest sort), then packs the result into the given chests in order, filling
    /// each up to its slot count and leaving trailing chests empty. Duplicate stacks
    /// scattered across chests merge naturally. Deterministic, hence idempotent.
    /// </summary>
    public static List<List<(string Code, int Count, int MaxStack)>> ComputeCompactLayout(
        IReadOnlyList<(string Code, int Count, int MaxStack)> pooled,
        IReadOnlyList<int> chestSlotCounts)
    {
        var sorted = InventorySorter.SortItems(pooled);
        var result = new List<List<(string Code, int Count, int MaxStack)>>(chestSlotCounts.Count);

        int idx = 0;
        foreach (var cap in chestSlotCounts)
        {
            var chest = new List<(string Code, int Count, int MaxStack)>();
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
    /// VS adapter for <see cref="ComputeCompactLayout"/>. Orders the chests from the
    /// anchor (nearest first), pools every stack while clearing the slots, computes the
    /// compact layout, and writes the items back. Pooling real (cloned) stacks prevents
    /// duplication; trailing chests are left empty.
    /// </summary>
    private static void ApplyCompactLayout(List<BlockPos> positions, BlockPos anchor, ICoreServerAPI api, SortConfig cfg)
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

        // Pool every stack (clearing slots), keeping frozen clones keyed by item code.
        var pooled = new List<(string, int, int)>();
        var clonePool = new Dictionary<string, Queue<ItemStack>>(StringComparer.Ordinal);
        foreach (var inv in invs)
        {
            foreach (var slot in inv)
            {
                if (slot.Itemstack == null) continue;
                var code = slot.Itemstack.Collectible.Code.Path;
                pooled.Add((code, slot.Itemstack.StackSize, slot.Itemstack.Collectible.MaxStackSize));
                var clone = slot.Itemstack.Clone();
                if (!clonePool.TryGetValue(code, out var q))
                    clonePool[code] = q = new Queue<ItemStack>();
                q.Enqueue(clone);
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }

        var slotCounts = invs.Select(i => i.Count).ToList();
        var layout = ComputeCompactLayout(pooled, slotCounts);

        for (int c = 0; c < invs.Count; c++)
        {
            var slotList = invs[c].ToList();
            int idx = 0;
            foreach (var (code, count, _) in layout[c])
            {
                if (idx >= slotList.Count) break;
                if (!clonePool.TryGetValue(code, out var q) || q.Count == 0) continue;
                var stack = q.Dequeue();
                stack.StackSize = count;
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
    /// True if any container in the network is currently opened by a player. Used to hold
    /// off sorting until the last open container in the room is closed.
    /// </summary>
    private static bool AnyContainerOpen(List<BlockPos> positions, ICoreServerAPI api, SortConfig cfg)
    {
        foreach (var pos in positions)
        {
            if (GetInventory(pos, api, cfg) is InventoryBase inv && inv.openedByPlayerGUIds.Count > 0)
                return true;
        }
        return false;
    }

    private record ChestGraphData(
        Dictionary<string, ChestData> Data,
        Dictionary<string, List<string>> Neighbours,
        Dictionary<string, BlockPos> PosLookup);

    /// <summary>
    /// Discovers the connected chest network by cascade flood-fill: starting from
    /// <paramref name="origin"/>, every discovered chest scans its own
    /// <see cref="SortConfig.SearchRadiusBlocks"/> sphere, pulling in further chests
    /// until the network stops growing or <see cref="SortConfig.MaxNetworkChests"/>
    /// is reached. Each chest is scanned exactly once. Result is cached across passes.
    /// </summary>
    private static List<BlockPos> ScanContainerPositions(
        BlockPos origin, ICoreServerAPI api, SortConfig cfg,
        IReadOnlyList<string>? groupFilter = null,
        System.Func<BlockPos, bool>? roomFilter = null)
    {
        var radius = cfg.SearchRadiusBlocks;
        var r2 = radius * radius;
        var cap = cfg.MaxNetworkChests;

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

                var inv = GetInventory(pos, api, cfg);
                if (inv == null) continue;

                // Match the group against the block code (chest vs storagevessel),
                // since both share the inventory class "chest".
                if (groupFilter != null &&
                    !groupFilter.Any(cls => BlockKind(pos, api).Contains(cls, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Keep the network within the origin's enclosed room (e.g. don't sort
                // across a closed door) when that restriction is enabled.
                if (roomFilter != null && !roomFilter(pos)) continue;

                found[key] = pos;
                queue.Enqueue(pos); // cascade: this chest scans its own neighbourhood
                if (found.Count >= cap) break;
            }
        }

        return found.Values.ToList();
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

            var items = new List<(string, int, int)>();
            foreach (var slot in inv)
            {
                if (slot.Itemstack == null) continue;
                items.Add((
                    slot.Itemstack.Collectible.Code.Path,
                    slot.Itemstack.StackSize,
                    slot.Itemstack.Collectible.MaxStackSize));
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
            if (srcSlot.Itemstack?.Collectible.Code.Path != plan.ItemCode) continue;

            // Find a target slot: same item (merge) or empty
            foreach (var tgtSlot in tgtSlots)
            {
                if (srcSlot.Itemstack == null || srcSlot.Itemstack.StackSize <= 0) break;

                bool tgtEmpty = tgtSlot.Itemstack == null;
                bool tgtSameItem = tgtSlot.Itemstack?.Collectible.Code.Path == plan.ItemCode;

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

            var code     = slot.Itemstack.Collectible.Code.Path;
            var maxStack = slot.Itemstack.Collectible.MaxStackSize;

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
                    bool sameItem = tgtSlot.Itemstack?.Collectible.Code.Path == code;
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

    private static int AvailableCapacity(ChestData chest, string itemCode, int itemMaxStack)
    {
        int usedSlots = chest.Items.Count(i => i.Count > 0);
        int emptySlots = Math.Max(0, chest.SlotCount - usedSlots);
        int partialSpace = chest.Items
            .Where(i => i.Code == itemCode && i.Count > 0)
            .Sum(i => i.MaxStack - i.Count);
        return partialSpace + emptySlots * itemMaxStack;
    }

    private static string PosKey(BlockPos p) => $"{p.X},{p.Y},{p.Z}";

    private static BlockPos ParsePos(string key)
    {
        var parts = key.Split(',');
        return new BlockPos(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    /// <summary>
    /// Pure cascade simulation — no VS API dependency.
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
                int srcIdx = srcItems.FindIndex(i => i.Code == plan.ItemCode);
                if (srcIdx < 0) continue;

                var (code, srcCount, maxStack) = srcItems[srcIdx];
                int take = Math.Min(srcCount, plan.Count);
                if (take <= 0) continue;

                // Deduct from source
                if (srcCount - take <= 0)
                    srcItems.RemoveAt(srcIdx);
                else
                    srcItems[srcIdx] = (code, srcCount - take, maxStack);
                chests[plan.SourceId] = srcData with { Items = srcItems };

                // Add to target
                var tgtItems = tgtData.Items.ToList();
                int tgtIdx = tgtItems.FindIndex(i => i.Code == plan.ItemCode);
                if (tgtIdx >= 0)
                {
                    var (tc, tCount, tMax) = tgtItems[tgtIdx];
                    tgtItems[tgtIdx] = (tc, tCount + take, tMax);
                }
                else
                {
                    tgtItems.Add((plan.ItemCode, take, maxStack));
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
