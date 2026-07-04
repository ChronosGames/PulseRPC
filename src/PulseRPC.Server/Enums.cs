namespace PulseRPC.Server;

/// <summary>
/// Server-side connection state enumeration for application-level connection tracking.
/// Note: PulseRPC.Shared.ConnectionState is used for transport layer state.
/// </summary>
public enum ServerConnectionState : byte
{
    /// <summary>
    /// Handshake in progress.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// Fully established.
    /// </summary>
    Active = 2,

    /// <summary>
    /// Graceful shutdown initiated.
    /// </summary>
    Closing = 3,

    /// <summary>
    /// Resources released.
    /// </summary>
    Closed = 4
}

/// <summary>
/// Service state enumeration.
/// </summary>
public enum ServiceState : byte
{
    /// <summary>
    /// Registered but not active.
    /// </summary>
    Registered = 1,

    /// <summary>
    /// Accepting requests.
    /// </summary>
    Active = 2,

    /// <summary>
    /// Temporarily disabled.
    /// </summary>
    Paused = 3,

    /// <summary>
    /// Removed from registry.
    /// </summary>
    Unregistered = 4
}
