using ProtoBuf;

namespace autoSortVintageStoryMod.Network;

/// <summary>
/// Server → client snapshot of the AutoSort configuration, the player's own overlay
/// state, whether they may edit (admin), and the set of container kinds discovered on
/// the server (for the editable container list).
/// </summary>
[ProtoContract]
public class ConfigSyncPacket
{
    [ProtoMember(1)] public bool IsAdmin { get; set; }
    [ProtoMember(2)] public bool OverlayEnabled { get; set; }

    [ProtoMember(3)] public bool Enabled { get; set; }
    [ProtoMember(4)] public bool CompactRoom { get; set; }
    [ProtoMember(5)] public bool SeparateFloors { get; set; }
    [ProtoMember(6)] public bool RestrictToSameRoom { get; set; }
    [ProtoMember(7)] public bool SortPlayerBackpack { get; set; }

    [ProtoMember(8)] public int SearchRadiusBlocks { get; set; }
    [ProtoMember(9)] public int MaxNetworkChests { get; set; }
    [ProtoMember(10)] public int MaxVerticalSpan { get; set; }
    [ProtoMember(11)] public double SpecialisationThreshold { get; set; }

    /// <summary>Container kinds AutoSort currently sorts (SupportedInventoryClasses).</summary>
    [ProtoMember(12)] public string[] EnabledKinds { get; set; } = System.Array.Empty<string>();

    /// <summary>All container kinds discovered on the server (vanilla + mods).</summary>
    [ProtoMember(13)] public string[] DiscoveredKinds { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// Client → server change request. The overlay toggle applies to the sending player;
/// the configuration fields are applied only if the player has the controlserver
/// privilege.
/// </summary>
[ProtoContract]
public class ConfigChangePacket
{
    [ProtoMember(1)] public bool OverlayEnabled { get; set; }
    [ProtoMember(2)] public bool ApplyConfig { get; set; } // true only when an admin saves config

    [ProtoMember(3)] public bool Enabled { get; set; }
    [ProtoMember(4)] public bool CompactRoom { get; set; }
    [ProtoMember(5)] public bool SeparateFloors { get; set; }
    [ProtoMember(6)] public bool RestrictToSameRoom { get; set; }
    [ProtoMember(7)] public bool SortPlayerBackpack { get; set; }

    [ProtoMember(8)] public int SearchRadiusBlocks { get; set; }
    [ProtoMember(9)] public int MaxNetworkChests { get; set; }
    [ProtoMember(10)] public int MaxVerticalSpan { get; set; }
    [ProtoMember(11)] public double SpecialisationThreshold { get; set; }

    [ProtoMember(12)] public string[] EnabledKinds { get; set; } = System.Array.Empty<string>();
}
