using autoSortVintageStoryMod.Config;
using Xunit;

namespace autoSortVintageStoryMod.Tests.Config;

/// <summary>
/// The wire encoding used to ship ContainerGroups between server and the ConfigLib editor
/// (sync packet, change packet, UI save/sync). One round-trip must preserve groups, and
/// both directions must trim, drop blanks/empties, and dedupe.
/// </summary>
public class ContainerGroupCodecTests
{
    [Fact]
    public void Encode_JoinsEachGroupWithCommas()
    {
        var encoded = ContainerGroupCodec.Encode(new[]
        {
            new[] { "chest", "basket", "crate" },
            new[] { "storagevessel" },
        });

        Assert.Equal(new[] { "chest,basket,crate", "storagevessel" }, encoded);
    }

    [Fact]
    public void Decode_SplitsEachStringOnCommas()
    {
        var groups = ContainerGroupCodec.Decode(new[] { "chest,trunk", "storagevessel" });

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "chest", "trunk" }, groups[0]);
        Assert.Equal(new[] { "storagevessel" }, groups[1]);
    }

    [Fact]
    public void RoundTrip_PreservesGroups()
    {
        var original = new List<List<string>>
        {
            new() { "chest", "basket", "crate" },
            new() { "storagevessel" },
            new() { "trunk" },
        };

        var roundTripped = ContainerGroupCodec.Decode(ContainerGroupCodec.Encode(original));

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Decode_TrimsWhitespaceAndDropsBlankMembers()
    {
        var groups = ContainerGroupCodec.Decode(new[] { " chest , trunk ,, " });

        Assert.Single(groups);
        Assert.Equal(new[] { "chest", "trunk" }, groups[0]);
    }

    [Fact]
    public void Decode_DropsEmptyGroups()
    {
        var groups = ContainerGroupCodec.Decode(new[] { "chest", "", " , ", "trunk" });

        Assert.Equal(new[] { "chest" }, groups[0]);
        Assert.Equal(new[] { "trunk" }, groups[1]);
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void Decode_DedupesMembersCaseInsensitively()
    {
        var groups = ContainerGroupCodec.Decode(new[] { "chest,Chest,CHEST,trunk" });

        Assert.Single(groups);
        Assert.Equal(new[] { "chest", "trunk" }, groups[0]);
    }

    [Fact]
    public void Encode_DropsBlankMembersAndEmptyGroups()
    {
        var encoded = ContainerGroupCodec.Encode(new[]
        {
            new[] { "chest", "", "  " },
            new[] { "", " " },        // becomes empty → dropped entirely
            new[] { "trunk" },
        });

        Assert.Equal(new[] { "chest", "trunk" }, encoded);
    }

    [Fact]
    public void Encode_DedupesMembersCaseInsensitively()
    {
        var encoded = ContainerGroupCodec.Encode(new[] { new[] { "chest", "CHEST", "chest" } });

        Assert.Equal(new[] { "chest" }, encoded);
    }
}
