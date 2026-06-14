using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace autoSortVintageStoryMod.Sorting;

/// <summary>
/// Trait-aware identity of an item stack. Two stacks share an identity when the game's own
/// <see cref="CollectibleObject.Equals(ItemStack, ItemStack, string[])"/> considers them
/// equal (ignoring volatile attributes), so distinct-trait items — different-genetics
/// cuttings, partially-used tools, filled crocks — never merge into one stack.
/// </summary>
/// <remarks>
/// Two construction modes:
/// <list type="bullet">
/// <item><b>Real</b> (<see cref="StackIdentity(ItemStack)"/>): a frozen clone compared with
/// the game's trait-aware equality. Used by the live in-game sorting.</item>
/// <item><b>Synthetic</b> (<see cref="StackIdentity(string, int)"/>): a code-only key for the
/// pure sorting logic and its unit tests, which have no Vintage Story runtime to build real
/// <see cref="ItemStack"/>s from.</item>
/// </list>
/// Within a single run every identity is of the same kind (all real in-game, all synthetic in
/// tests), so the equality/hash contract stays self-consistent in each context.
/// </remarks>
public sealed class StackIdentity : IEquatable<StackIdentity>
{
    private readonly ItemStack? representative;
    private readonly string code;
    private readonly int maxStack;
    private readonly int hashCode;

    public StackIdentity(ItemStack stack)
    {
        representative = stack.Clone();
        code = representative.Collectible.Code.Path;
        maxStack = representative.Collectible.MaxStackSize;
        hashCode = HashCode.Combine(
            (int)representative.Class,
            representative.Id,
            representative.GetHashCode(GlobalConstants.IgnoredStackAttributes));
    }

    /// <summary>
    /// Code-only identity for the pure sorting logic (and its unit tests): two stacks are the
    /// same when their item code matches. Used where no real <see cref="ItemStack"/> exists.
    /// In-game sorting always uses the <see cref="StackIdentity(ItemStack)"/> constructor.
    /// </summary>
    public StackIdentity(string code, int maxStackSize)
    {
        representative = null;
        this.code = code;
        maxStack = maxStackSize;
        hashCode = StringComparer.Ordinal.GetHashCode(code);
    }

    public string Code => code;
    public int MaxStackSize => maxStack;

    public bool Matches(ItemStack? stack)
        => representative != null
            ? AreEquivalent(representative, stack)
            : stack?.Collectible?.Code?.Path == code;

    public ItemStack CloneWithSize(int stackSize)
    {
        if (representative == null)
            throw new InvalidOperationException("CloneWithSize requires a real (ItemStack-backed) identity.");
        var clone = representative.Clone();
        clone.StackSize = stackSize;
        return clone;
    }

    public bool Equals(StackIdentity? other)
    {
        if (other == null) return false;
        if (representative != null && other.representative != null)
            return AreEquivalent(representative, other.representative);
        // At least one synthetic identity → compare by item code.
        return string.Equals(code, other.code, StringComparison.Ordinal);
    }

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
