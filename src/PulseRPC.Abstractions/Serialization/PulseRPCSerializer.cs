using System.Buffers;

namespace PulseRPC;

/// <summary>
/// Provides a processing for message serialization.
/// </summary>
public interface IPulseRPCSerializer
{
    void Serialize<T>(IBufferWriter<byte> writer, in T value) where T : IPacket;

    IPacket Deserialize(in ReadOnlySpan<byte> bytes);
}
