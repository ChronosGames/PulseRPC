namespace PulseRPC.Server.Models;

/// <summary>
/// Message type enumeration.
/// </summary>
public enum MessageType : byte
{
    /// <summary>
    /// RPC request from client.
    /// </summary>
    Request = 1,

    /// <summary>
    /// Successful response to client.
    /// </summary>
    Response = 2,

    /// <summary>
    /// Error response to client.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Keep-alive ping.
    /// </summary>
    Ping = 4,

    /// <summary>
    /// Keep-alive response.
    /// </summary>
    Pong = 5
}

/// <summary>
/// Connection state enumeration.
/// </summary>
public enum ConnectionState : byte
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

/// <summary>
/// Message priority enumeration.
/// </summary>
public enum MessagePriority : byte
{
    /// <summary>
    /// Process immediately (health checks, admin).
    /// </summary>
    Critical = 0,

    /// <summary>
    /// High priority (latency-sensitive operations).
    /// </summary>
    High = 1,

    /// <summary>
    /// Default priority.
    /// </summary>
    Normal = 2,

    /// <summary>
    /// Low priority (background tasks, analytics).
    /// </summary>
    Low = 3
}

/// <summary>
/// Transport type enumeration.
/// </summary>
public enum TransportType : byte
{
    /// <summary>
    /// Reliable stream-based transport.
    /// </summary>
    TCP = 1,

    /// <summary>
    /// Low-latency UDP-based transport.
    /// </summary>
    KCP = 2
}
