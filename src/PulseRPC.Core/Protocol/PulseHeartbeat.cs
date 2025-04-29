using MemoryPack;

namespace PulseRPC.Protocol;

/// <summary>
/// Represents a heartbeat message used for keep-alive and RTT calculation.
/// </summary>
[MemoryPackable]
public partial struct PulseHeartbeat
{
    /// <summary>
    /// Timestamp (e.g., UTC Ticks) when the heartbeat was sent.
    /// </summary>
    [MemoryPackOrder(0)]
    public long Timestamp { get; set; }
}
