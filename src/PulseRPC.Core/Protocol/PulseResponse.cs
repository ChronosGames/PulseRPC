using MemoryPack;

namespace PulseRPC.Protocol;

/// <summary>
/// Represents a response message sent from server to client.
/// </summary>
[MemoryPackable]
public partial struct PulseResponse
{
    /// <summary>
    /// Identifier correlating this response to the original request.
    /// </summary>
    [MemoryPackOrder(0)]
    public Guid RequestId { get; set; }

    /// <summary>
    /// Indicates whether the request was processed successfully.
    /// </summary>
    [MemoryPackOrder(1)]
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Serialized result of the method call if successful.
    /// </summary>
    [MemoryPackOrder(2)]
    public byte[]? Result { get; set; }

    /// <summary>
    /// Error message if the request processing failed.
    /// </summary>
    [MemoryPackOrder(3)]
    public string? ErrorMessage { get; set; }
}
