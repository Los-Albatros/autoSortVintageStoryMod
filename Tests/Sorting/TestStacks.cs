using System.Collections.Generic;
using System.Linq;
using autoSortVintageStoryMod.Sorting;

namespace autoSortVintageStoryMod.Tests.Sorting;

/// <summary>
/// Helpers for the pure sorting tests, which run without a Vintage Story runtime and so
/// build <see cref="StackEntry"/> values from a synthetic (code-only) <see cref="StackIdentity"/>.
/// </summary>
internal static class TestStacks
{
    /// <summary>A synthetic stack entry identified purely by its item code.</summary>
    public static StackEntry E(string code, int count, int max = 64)
        => new(new StackIdentity(code, max), count, 0);

    /// <summary>
    /// Stable signature of a layout as "code:count" per chest — used by idempotency tests.
    /// (StackEntry's record ToString isn't value-stable because StackIdentity is a class and
    /// the internal Order field is re-derived on each pass.)
    /// </summary>
    public static string Sig(IEnumerable<List<StackEntry>> layout)
        => string.Join("|", layout.Select(c => string.Join(",", c.Select(e => $"{e.Code}:{e.Count}"))));
}
