using MemoryPack;

namespace PulseRPC.Protocol;

/// <summary>
/// The outer wrapper for all messages sent over the wire.
/// Contains the message type and the serialized payload.
/// </summary>
[MemoryPackable]
public partial struct MessageEnvelope
{
    [MemoryPackOrder(0)]
    public MessageType Type { get; set; }

    [MemoryPackOrder(1)]
    public byte[] Payload { get; set; }
}
