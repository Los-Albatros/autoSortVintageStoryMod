using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace autoSortVintageStoryMod.Sorting;

/// <summary>
/// Discovers the container "kinds" available on the server by inspecting every
/// registered block: blocks whose block-entity class is an <see cref="IBlockEntityContainer"/>
/// are containers, and their base code (segment before the first '-') is the kind
/// (e.g. "chest", "storagevessel", "crate", plus any modded ones). Cached after first use.
/// </summary>
public static class ContainerDiscovery
{
    private static string[]? _cache;

    public static string[] Discover(ICoreServerAPI api)
    {
        if (_cache != null) return _cache;

        var kinds = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        // Test each block-entity class only once.
        var containerClass = new Dictionary<string, bool>(System.StringComparer.Ordinal);

        foreach (var block in api.World.Blocks)
        {
            if (block?.Code == null || string.IsNullOrEmpty(block.EntityClass)) continue;

            if (!containerClass.TryGetValue(block.EntityClass, out var isContainer))
            {
                isContainer = false;
                try
                {
                    var be = api.ClassRegistry.CreateBlockEntity(block.EntityClass);
                    isContainer = be is IBlockEntityContainer;
                }
                catch { /* some BE classes can't be created bare — treat as non-container */ }
                containerClass[block.EntityClass] = isContainer;
            }

            if (isContainer) kinds.Add(BaseKind(block.Code.Path));
        }

        _cache = kinds.ToArray();
        return _cache;
    }

    /// <summary>Base name of a block code path: the segment before the first '-'.</summary>
    public static string BaseKind(string codePath)
    {
        if (string.IsNullOrEmpty(codePath)) return "";
        int dash = codePath.IndexOf('-');
        return dash < 0 ? codePath : codePath[..dash];
    }
}
