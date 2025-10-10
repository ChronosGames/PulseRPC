using System.Net;

namespace PulseRPC.Server.Models;

/// <summary>
/// Represents an active network session with a client.
/// </summary>
public sealed class ServerConnection
{
    private long _messagesSent;
    private long _messagesReceived;
    private long _errorCount;
    private long _bytesSent;
    private long _bytesReceived;

    /// <summary>
    /// Unique connection identifier.
    /// </summary>
    public string ConnectionId { get; init; } = string.Empty;

    /// <summary>
    /// Client IP and port.
    /// </summary>
    public IPEndPoint? ClientAddress { get; init; }

    /// <summary>
    /// Transport protocol (TCP or KCP).
    /// </summary>
    public TransportType TransportProtocol { get; init; }

    /// <summary>
    /// Connection state.
    /// </summary>
    public ConnectionState State { get; private set; }

    /// <summary>
    /// Connection establishment time (UTC).
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Most recent send/receive time (UTC).
    /// </summary>
    public DateTime LastActivityAt { get; private set; }

    /// <summary>
    /// Cumulative responses sent.
    /// </summary>
    public long MessagesSent => Interlocked.Read(ref _messagesSent);

    /// <summary>
    /// Cumulative requests received.
    /// </summary>
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);

    /// <summary>
    /// Cumulative error count.
    /// </summary>
    public long ErrorCount => Interlocked.Read(ref _errorCount);

    /// <summary>
    /// Cumulative bytes sent.
    /// </summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>
    /// Cumulative bytes received.
    /// </summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>
    /// Initializes a new ServerConnection.
    /// </summary>
    public ServerConnection(string connectionId, IPEndPoint? clientAddress, TransportType transportProtocol)
    {
        ConnectionId = connectionId;
        ClientAddress = clientAddress;
        TransportProtocol = transportProtocol;
        CreatedAt = DateTime.UtcNow;
        LastActivityAt = DateTime.UtcNow;
        State = ConnectionState.Connecting;
    }

    /// <summary>
    /// Transitions the connection to a new state.
    /// </summary>
    /// <returns>True if transition was valid, false otherwise.</returns>
    public bool TryTransitionState(ConnectionState newState)
    {
        // Validate state transition (one-way only)
        var isValidTransition = (State, newState) switch
        {
            (ConnectionState.Connecting, ConnectionState.Active) => true,
            (ConnectionState.Active, ConnectionState.Closing) => true,
            (ConnectionState.Closing, ConnectionState.Closed) => true,
            (ConnectionState.Connecting, ConnectionState.Closed) => true, // Failed connection
            _ => false
        };

        if (isValidTransition)
        {
            State = newState;
            LastActivityAt = DateTime.UtcNow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records a sent message.
    /// </summary>
    public void RecordMessageSent(int bytesCount)
    {
        Interlocked.Increment(ref _messagesSent);
        Interlocked.Add(ref _bytesSent, bytesCount);
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a received message.
    /// </summary>
    public void RecordMessageReceived(int bytesCount)
    {
        Interlocked.Increment(ref _messagesReceived);
        Interlocked.Add(ref _bytesReceived, bytesCount);
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records an error.
    /// </summary>
    public void RecordError()
    {
        Interlocked.Increment(ref _errorCount);
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if the connection is active.
    /// </summary>
    public bool IsActive => State == ConnectionState.Active;

    /// <summary>
    /// Gets the connection duration.
    /// </summary>
    public TimeSpan GetDuration() => DateTime.UtcNow - CreatedAt;
}
