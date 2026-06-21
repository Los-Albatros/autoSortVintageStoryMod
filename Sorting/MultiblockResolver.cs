namespace autoSortVintageStoryMod.Sorting;

/// <summary>
/// Resolves a Vintage Story "multiblock" filler cell to the principal cell that actually
/// holds the block entity. Large containers span more than one cell: only the principal cell
/// carries the inventory, the others are <c>multiblock-monolithic-{dx}-{dy}-{dz}</c> filler
/// blocks whose code encodes the offset back to the principal.
///
/// This matters for the wooden trunk — the only default container that is a 2-cell multiblock.
/// When a player opens the trunk via its filler half, a naive <c>GetBlockEntity(pos)</c> finds
/// no entity there, so the trunk is never hooked and never sorts. Chests, baskets, vessels and
/// crates are single-cell and unaffected.
/// </summary>
public static class MultiblockResolver
{
    /// <summary>
    /// Parses a filler block code path of the form "multiblock-monolithic-{dx}-{dy}-{dz}"
    /// (offset states n2/n1/0/p1/p2/p3) into the signed offset that, added to the filler's own
    /// position, gives the principal cell. Returns false for any non-filler or degenerate path.
    /// </summary>
    public static bool TryParseFillerOffset(string? codePath, out int dx, out int dy, out int dz)
    {
        dx = dy = dz = 0;
        if (string.IsNullOrEmpty(codePath)) return false;

        var parts = codePath.Split('-');
        // multiblock - monolithic - dx - dy - dz
        if (parts.Length < 5 ||
            !parts[0].Equals("multiblock", System.StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryParseOffset(parts[2], out dx)) return false;
        if (!TryParseOffset(parts[3], out dy)) return false;
        if (!TryParseOffset(parts[4], out dz)) return false;

        // A filler pointing at itself is degenerate — treat it as "not a filler".
        return dx != 0 || dy != 0 || dz != 0;
    }

    /// <summary>"n2"/"n1"/"0"/"p1"/"p2"/"p3" → signed integer (n = minus, p = plus).</summary>
    private static bool TryParseOffset(string s, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        if (s == "0") return true;

        int sign = s[0] switch { 'n' => -1, 'p' => 1, _ => 0 };
        if (sign == 0) return false;
        if (!int.TryParse(s.AsSpan(1), out int magnitude)) return false;

        value = sign * magnitude;
        return true;
    }
}
