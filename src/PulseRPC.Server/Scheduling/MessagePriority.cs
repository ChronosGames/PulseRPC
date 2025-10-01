namespace PulseRPC.Server.Scheduling;

/// <summary>
/// Message priority levels for L3 degradation.
/// When channels are full, low priority messages are dropped first.
/// </summary>
public enum MessagePriority
{
    /// <summary>
    /// Low priority - first to be dropped during degradation.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Normal priority - default for most messages.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// High priority - last to be dropped during degradation.
    /// </summary>
    High = 2
}