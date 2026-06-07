using ProtoBuf;

namespace autoSortVintageStoryMod.Network;

[ProtoContract]
public class OverlayPacket
{
    [ProtoMember(1)]
    public bool Enabled { get; set; }
}
