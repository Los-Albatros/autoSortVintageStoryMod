namespace autoSortVintageStoryMod.Config;

/// <summary>
/// Wire encoding for <see cref="SortConfig.ContainerGroups"/>: each group is sent as a
/// single string of its member kinds joined by ','. Both the config sync/change packets
/// and the ConfigLib group editor go through here so the encode/decode rules (trim, drop
/// blanks, dedupe case-insensitively, drop empty groups) live in exactly one place.
/// </summary>
public static class ContainerGroupCodec
{
    /// <summary>Groups → one ','-joined string per group. Blank members are dropped, members
    /// deduped case-insensitively, and empty groups omitted.</summary>
    public static string[] Encode(IEnumerable<IEnumerable<string>> groups)
        => groups
            .Select(g => string.Join(",", g
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)))
            .Where(s => s.Length > 0)
            .ToArray();

    /// <summary>','-joined group strings → groups. Whitespace trimmed, blank members dropped,
    /// members deduped case-insensitively, and empty groups omitted.</summary>
    public static List<List<string>> Decode(IEnumerable<string> encoded)
        => encoded
            .Select(s => s
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList())
            .Where(g => g.Count > 0)
            .ToList();
}
