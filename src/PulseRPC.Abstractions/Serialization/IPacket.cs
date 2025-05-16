using MemoryPack;

namespace PulseRPC;

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IPacket
{
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class PacketAttribute : Attribute
{
    public PacketAttribute(ushort id = 0x00)
    {
        Id = id;
    }

    public ushort Id { get; }
}
