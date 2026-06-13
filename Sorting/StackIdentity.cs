using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace autoSortVintageStoryMod.Sorting;

public sealed class StackIdentity : IEquatable<StackIdentity>
{
    private readonly ItemStack representative;
    private readonly int hashCode;

    public StackIdentity(ItemStack stack)
    {
        representative = stack.Clone();
        hashCode = HashCode.Combine(
            (int)representative.Class,
            representative.Id,
            representative.GetHashCode(GlobalConstants.IgnoredStackAttributes));
    }

    public string Code => representative.Collectible.Code.Path;
    public int MaxStackSize => representative.Collectible.MaxStackSize;

    public bool Matches(ItemStack? stack) => AreEquivalent(representative, stack);

    public ItemStack CloneWithSize(int stackSize)
    {
        var clone = representative.Clone();
        clone.StackSize = stackSize;
        return clone;
    }

    public bool Equals(StackIdentity? other)
        => other != null && AreEquivalent(representative, other.representative);

    public override bool Equals(object? obj) => obj is StackIdentity other && Equals(other);
    public override int GetHashCode() => hashCode;

    public static bool AreEquivalent(ItemStack? left, ItemStack? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return false;
        if (left.Collectible == null || right.Collectible == null) return false;

        return left.Collectible.Equals(left, right, GlobalConstants.IgnoredStackAttributes);
    }
}

public sealed record StackEntry(StackIdentity Identity, int Count, int Order)
{
    public string Code => Identity.Code;
    public int MaxStack => Identity.MaxStackSize;
}
