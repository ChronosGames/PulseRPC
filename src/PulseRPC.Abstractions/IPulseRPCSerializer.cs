using System.Buffers;
using MemoryPack;

namespace PulseRPC;

/// <summary>
/// Provides a processing for message serialization.
/// </summary>
public interface IPulseRPCSerializer
{
    void Serialize<T>(IBufferWriter<byte> writer, in T value) where T : IMemoryPackable<T>;

    // object Deserialize(in ReadOnlySpan<byte> bytes);

    int ProcessMessage(ref ReadOnlySequence<byte> buffer);
}
