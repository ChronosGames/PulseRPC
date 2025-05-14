using System.Buffers;
using System.Reflection;

namespace PulseRPC;

/// <summary>
/// Provides a serializer for request/response of MagicOnion services and hub methods.
/// </summary>
public interface IPulseRPCSerializerProvider
{
    /// <summary>
    /// Create a serializer for the service method.
    /// </summary>
    /// <param name="methodInfo">A method info for an implementation of the service method. It is a hint that handling request parameters on the server, which may be passed null on the client.</param>
    /// <returns></returns>
    IPulseRPCSerializer Create(MethodInfo? methodInfo);
}

/// <summary>
/// Provides a processing for message serialization.
/// </summary>
public interface IPulseRPCSerializer
{
    void Serialize<T>(IBufferWriter<byte> writer, in T value);
    T Deserialize<T>(in ReadOnlySequence<byte> bytes);

    byte[] Serialize2<T>(in T message) where T : IPacket;
    IPacket Deserialize2(in ReadOnlySpan<byte> bytes);
}
