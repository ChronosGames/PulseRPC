using MemoryPack;

namespace PulseRPC.Protocol;

/// <summary>
/// Represents a request message sent from client to server.
/// </summary>
[MemoryPackable]
public partial struct PulseRequest
{
    /// <summary>
    /// Unique identifier for the request, used to correlate with the response.
    /// </summary>
    [MemoryPackOrder(0)]
    public Guid RequestId { get; set; }

    /// <summary>
    /// The name of the target service or hub.
    /// </summary>
    [MemoryPackOrder(1)]
    public string ServiceName { get; set; }

    /// <summary>
    /// The name of the target method.
    /// </summary>
    [MemoryPackOrder(2)]
    public string MethodName { get; set; }

    /// <summary>
    /// Serialized parameters for the method call.
    /// </summary>
    [MemoryPackOrder(3)]
    public byte[] Parameters { get; set; }
}
