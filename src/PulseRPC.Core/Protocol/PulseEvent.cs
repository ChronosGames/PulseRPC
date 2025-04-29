using MemoryPack;

namespace PulseRPC.Protocol;

/// <summary>
/// Represents an event message pushed from the server to the client (Hub related).
/// </summary>
[MemoryPackable]
public partial struct PulseEvent
{
    /// <summary>
    /// The name of the Hub that raised the event.
    /// </summary>
    [MemoryPackOrder(0)]
    public string HubName { get; set; }

    /// <summary>
    /// The name of the event (usually the method name on the receiver interface).
    /// </summary>
    [MemoryPackOrder(1)]
    public string EventName { get; set; }

    /// <summary>
    /// Serialized event data/parameters.
    /// </summary>
    [MemoryPackOrder(2)]
    public byte[] EventData { get; set; }
}
