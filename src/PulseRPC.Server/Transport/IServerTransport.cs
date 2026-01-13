using PulseRPC.Transport;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Transport;

/// <summary>
/// Server transport manager that extends IServerListener with additional management capabilities.
/// Provides enhanced functionality for connection tracking, statistics, and event handling.
/// </summary>
/// <remarks>
/// This interface extends the existing IServerListener from PulseRPC.Transport with server-specific features
/// needed for the message dispatch-process-response pipeline.
/// </remarks>
public interface IPulseServerTransport : IServerListener
{
    /// <summary>
    /// Event raised when a client connection is closed or lost.
    /// </summary>
    event EventHandler<ConnectionClosedEventArgs>? ConnectionClosed;

    /// <summary>
    /// Gets the current number of active client connections.
    /// </summary>
    int ActiveConnectionCount { get; }

    /// <summary>
    /// Gets the total number of connections accepted since the server started.
    /// </summary>
    long TotalConnectionsAccepted { get; }

    /// <summary>
    /// Gets the total number of connections closed since the server started.
    /// </summary>
    long TotalConnectionsClosed { get; }

    /// <summary>
    /// Gets a connection by its identifier.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <returns>The server transport for the connection, or null if not found.</returns>
    IServerTransport? GetConnection(string connectionId);
}

/// <summary>
/// Event arguments for connection closure.
/// </summary>
public class ConnectionClosedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the identifier of the closed connection.
    /// </summary>
    public required string ConnectionId { get; init; }

    /// <summary>
    /// Gets the reason for connection closure.
    /// </summary>
    public ConnectionCloseReason Reason { get; init; }

    /// <summary>
    /// Gets additional details about the closure (e.g., error message).
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Reasons for connection closure.
/// </summary>
public enum ConnectionCloseReason
{
    /// <summary>
    /// Client disconnected normally.
    /// </summary>
    ClientDisconnect,

    /// <summary>
    /// Server initiated graceful shutdown.
    /// </summary>
    ServerShutdown,

    /// <summary>
    /// Network error or timeout.
    /// </summary>
    NetworkError,

    /// <summary>
    /// Protocol violation or invalid message.
    /// </summary>
    ProtocolError,

    /// <summary>
    /// Authentication or authorization failure.
    /// </summary>
    AuthenticationFailed
}
