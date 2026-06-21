using autoSortVintageStoryMod.Sorting;
using Xunit;

namespace autoSortVintageStoryMod.Tests.Sorting;

/// <summary>
/// Covers resolving a multiblock filler cell to the container's principal cell — the
/// wooden-trunk case, where opening the trunk via its filler half must still hook the
/// trunk's block entity (which lives in the principal cell) or the trunk never sorts.
/// </summary>
public class MultiblockResolverTests
{
    [Theory]
    // Trunk-east filler sits one cell +Z from the principal, so its offset back is -Z.
    [InlineData("multiblock-monolithic-0-0-n1", 0, 0, -1)]
    [InlineData("multiblock-monolithic-n1-0-0", -1, 0, 0)]
    [InlineData("multiblock-monolithic-p1-0-0", 1, 0, 0)]
    [InlineData("multiblock-monolithic-p2-n2-p3", 2, -2, 3)]
    public void TryParseFillerOffset_DecodesSignedOffsets(string code, int ex, int ey, int ez)
    {
        Assert.True(MultiblockResolver.TryParseFillerOffset(code, out int dx, out int dy, out int dz));
        Assert.Equal((ex, ey, ez), (dx, dy, dz));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("trunk-east")]            // a real container, not a filler
    [InlineData("chest")]
    [InlineData("multiblock-monolithic-0-0-0")] // degenerate self-pointer
    [InlineData("multiblock-monolithic-x-0-0")] // malformed offset
    public void TryParseFillerOffset_RejectsNonFillers(string? code)
    {
        Assert.False(MultiblockResolver.TryParseFillerOffset(code, out _, out _, out _));
    }
}
