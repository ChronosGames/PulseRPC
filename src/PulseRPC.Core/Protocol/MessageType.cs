namespace PulseRPC.Protocol;

/// <summary>
/// Defines the type of a message in the PulseRPC protocol.
/// </summary>
public enum MessageType : byte // Use byte for smaller size
{
    Request = 1,
    Response = 2,
    Event = 3,
    Heartbeat = 4
}
