using PulseRPC.Server.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Core;

/// <summary>
/// Manages the lifecycle of client connections, tracking statistics and handling cleanup.
/// Thread-safe connection tracking with state transition validation.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ServerConnection> _connections = new();
    private readonly Timer _cleanupTimer;
    private readonly ConnectionManagerOptions _options;
    private bool _disposed;

    private long _totalConnectionsAccepted;
    private long _totalConnectionsClosed;
    private long _totalConnectionsFailed;

    /// <summary>
    /// Gets the number of currently active connections.
    /// </summary>
    public int ActiveConnectionCount => _connections.Count(c => c.Value.IsActive);

    /// <summary>
    /// Gets the total number of connections ever accepted.
    /// </summary>
    public long TotalConnectionsAccepted => Interlocked.Read(ref _totalConnectionsAccepted);

    /// <summary>
    /// Gets the total number of connections closed.
    /// </summary>
    public long TotalConnectionsClosed => Interlocked.Read(ref _totalConnectionsClosed);

    /// <summary>
    /// Gets the total number of connections that failed.
    /// </summary>
    public long TotalConnectionsFailed => Interlocked.Read(ref _totalConnectionsFailed);

    /// <summary>
    /// Gets all current connections.
    /// </summary>
    public IReadOnlyCollection<ServerConnection> GetAllConnections() => _connections.Values.ToArray();

    public ConnectionManager(ConnectionManagerOptions? options = null)
    {
        _options = options ?? new ConnectionManagerOptions();

        // Start cleanup timer
        _cleanupTimer = new Timer(
            CleanupStaleConnections,
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Registers a new connection.
    /// </summary>
    public bool TryAddConnection(ServerConnection connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        // Check max connections limit
        if (_connections.Count >= _options.MaxConnections)
        {
            Interlocked.Increment(ref _totalConnectionsFailed);
            return false;
        }

        if (_connections.TryAdd(connection.ConnectionId, connection))
        {
            Interlocked.Increment(ref _totalConnectionsAccepted);

            // Transition to Active state
            connection.TryTransitionState(ConnectionState.Active);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a connection by ID.
    /// </summary>
    public ServerConnection? GetConnection(string connectionId)
    {
        _connections.TryGetValue(connectionId, out var connection);
        return connection;
    }

    /// <summary>
    /// Removes a connection.
    /// </summary>
    public bool TryRemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            // Transition to Closed state
            connection.TryTransitionState(ConnectionState.Closing);
            connection.TryTransitionState(ConnectionState.Closed);

            Interlocked.Increment(ref _totalConnectionsClosed);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Closes a specific connection.
    /// </summary>
    public async Task<bool> CloseConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            return false;
        }

        // Transition to Closing state
        connection.TryTransitionState(ConnectionState.Closing);

        // Allow some time for graceful shutdown
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        // Remove from dictionary
        return TryRemoveConnection(connectionId);
    }

    /// <summary>
    /// Closes all connections.
    /// </summary>
    public async Task CloseAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var connectionIds = _connections.Keys.ToArray();

        var tasks = connectionIds.Select(id => CloseConnectionAsync(id, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets statistics for all connections.
    /// </summary>
    public ConnectionStatistics GetStatistics()
    {
        var connections = _connections.Values;

        var totalMessagesSent = connections.Sum(c => c.MessagesSent);
        var totalMessagesReceived = connections.Sum(c => c.MessagesReceived);
        var totalBytesSent = connections.Sum(c => c.BytesSent);
        var totalBytesReceived = connections.Sum(c => c.BytesReceived);
        var totalErrors = connections.Sum(c => c.ErrorCount);

        return new ConnectionStatistics
        {
            ActiveConnections = ActiveConnectionCount,
            TotalConnectionsAccepted = TotalConnectionsAccepted,
            TotalConnectionsClosed = TotalConnectionsClosed,
            TotalConnectionsFailed = TotalConnectionsFailed,
            TotalMessagesSent = totalMessagesSent,
            TotalMessagesReceived = totalMessagesReceived,
            TotalBytesSent = totalBytesSent,
            TotalBytesReceived = totalBytesReceived,
            TotalErrors = totalErrors
        };
    }

    /// <summary>
    /// Detects potential resource leaks (connections in non-final state).
    /// </summary>
    public IReadOnlyList<string> DetectLeakedConnections()
    {
        var leakedConnections = new List<string>();

        foreach (var kvp in _connections)
        {
            var connection = kvp.Value;

            // Check if connection has been inactive for too long
            var inactiveDuration = DateTime.UtcNow - connection.LastActivityAt;

            if (inactiveDuration > _options.InactivityTimeout)
            {
                leakedConnections.Add(kvp.Key);
            }
        }

        return leakedConnections;
    }

    private void CleanupStaleConnections(object? state)
    {
        if (_disposed)
            return;

        try
        {
            var leakedConnections = DetectLeakedConnections();

            foreach (var connectionId in leakedConnections)
            {
                // Remove stale connection
                TryRemoveConnection(connectionId);
            }
        }
        catch (Exception)
        {
            // Log error in production
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cleanupTimer.Dispose();

        // Close all connections
        CloseAllConnectionsAsync().GetAwaiter().GetResult();

        _connections.Clear();
    }
}

/// <summary>
/// Configuration options for ConnectionManager.
/// </summary>
public sealed class ConnectionManagerOptions
{
    /// <summary>
    /// Maximum number of concurrent connections (default: 10,000).
    /// </summary>
    public int MaxConnections { get; set; } = 10_000;

    /// <summary>
    /// Inactivity timeout for connection cleanup (default: 5 minutes).
    /// </summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cleanup interval (default: 30 seconds).
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Aggregated connection statistics.
/// </summary>
public sealed class ConnectionStatistics
{
    public int ActiveConnections { get; init; }
    public long TotalConnectionsAccepted { get; init; }
    public long TotalConnectionsClosed { get; init; }
    public long TotalConnectionsFailed { get; init; }
    public long TotalMessagesSent { get; init; }
    public long TotalMessagesReceived { get; init; }
    public long TotalBytesSent { get; init; }
    public long TotalBytesReceived { get; init; }
    public long TotalErrors { get; init; }
}
