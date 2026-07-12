using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Health;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Channels;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Server.Extensions;

namespace PulseRPC.Server.Channels;

/// <summary>
/// Manages connection state tracking and lifecycle.
/// Thread-safe connection registry with automatic cleanup.
/// </summary>
[Obsolete("This state store is not connected to IPulseServer. Use IServerChannelManager or IPulseServer connection queries.", false)]
public sealed class ServerConnectionStateManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ServerConnection> _connections = new();
    private readonly ILogger<ServerConnectionStateManager> _logger;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _idleTimeout;
    private bool _disposed;

    public ServerConnectionStateManager(
        TimeSpan? idleTimeout = null,
        ILogger<ServerConnectionStateManager>? logger = null)
    {
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ServerConnectionStateManager>.Instance;

        // Start cleanup timer (runs every 30 seconds)
        _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Registers a new connection.
    /// </summary>
    public bool RegisterConnection(ServerConnection connection)
    {
        if (_connections.TryAdd(connection.ConnectionId, connection))
        {
            _logger.LogConnectionAccepted(connection);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a connection by ID.
    /// </summary>
    public ServerConnection? GetConnection(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    /// <summary>
    /// Updates connection state.
    /// </summary>
    public bool UpdateServerConnectionState(string connectionId, ServerConnectionState newState)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            return connection.TryTransitionState(newState);
        }

        return false;
    }

    /// <summary>
    /// Removes a connection.
    /// </summary>
    public bool RemoveConnection(string connectionId, string reason = "Normal closure")
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            connection.TryTransitionState(ServerConnectionState.Closed);
            _logger.LogConnectionClosed(connection, reason);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all active connections.
    /// </summary>
    public IReadOnlyCollection<ServerConnection> GetActiveConnections()
    {
        return _connections.Values
            .Where(c => c.IsActive)
            .ToList();
    }

    /// <summary>
    /// Gets connection statistics.
    /// </summary>
    public ConnectionStatistics GetStatistics()
    {
        var connections = _connections.Values.ToList();

        return new ConnectionStatistics
        {
            TotalConnections = connections.Count,
            ActiveConnections = connections.Count(c => c.IsActive),
            ConnectingConnections = connections.Count(c => c.State == ServerConnectionState.Connecting),
            ClosingConnections = connections.Count(c => c.State == ServerConnectionState.Closing),
            TotalMessagesSent = connections.Sum(c => c.MessagesSent),
            TotalMessagesReceived = connections.Sum(c => c.MessagesReceived),
            TotalErrors = connections.Sum(c => c.ErrorCount),
            AverageConnectionDuration = connections.Any()
                ? TimeSpan.FromSeconds(connections.Average(c => c.GetDuration().TotalSeconds))
                : TimeSpan.Zero
        };
    }

    /// <summary>
    /// Cleans up idle connections.
    /// </summary>
    private void CleanupIdleConnections(object? state)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        var toRemove = new List<string>();

        foreach (var kvp in _connections)
        {
            var connection = kvp.Value;

            // Check if connection is idle
            if (connection.State == ServerConnectionState.Active &&
                (now - connection.LastActivityAt) > _idleTimeout)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} idle for {IdleDuration}s, marking for cleanup",
                    connection.ConnectionId,
                    (now - connection.LastActivityAt).TotalSeconds);

                toRemove.Add(kvp.Key);
            }

            // Clean up closed connections
            if (connection.State == ServerConnectionState.Closed &&
                (now - connection.LastActivityAt) > TimeSpan.FromMinutes(1))
            {
                toRemove.Add(kvp.Key);
            }
        }

        // Remove idle connections
        foreach (var connectionId in toRemove)
        {
            RemoveConnection(connectionId, "Idle timeout or cleanup");
        }

        if (toRemove.Any())
        {
            _logger.LogDebug("Cleaned up {Count} idle/closed connections", toRemove.Count);
        }
    }

    /// <summary>
    /// Closes all connections gracefully.
    /// </summary>
    public async Task CloseAllConnectionsAsync(TimeSpan timeout)
    {
        _logger.LogInformation("Closing all connections gracefully (Timeout: {Timeout}s)", timeout.TotalSeconds);

        var connections = _connections.Values.ToList();
        var closeTasks = connections.Select(async c =>
        {
            try
            {
                c.TryTransitionState(ServerConnectionState.Closing);
                await Task.Delay(100); // Give time for pending messages
                c.TryTransitionState(ServerConnectionState.Closed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing connection {ConnectionId}", c.ConnectionId);
            }
        });

        var allClosedTask = Task.WhenAll(closeTasks);
        var timeoutTask = Task.Delay(timeout);

        await Task.WhenAny(allClosedTask, timeoutTask);

        // Force close any remaining
        foreach (var connection in _connections.Values)
        {
            connection.TryTransitionState(ServerConnectionState.Closed);
        }

        _connections.Clear();
        _logger.LogInformation("All connections closed");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _cleanupTimer?.Dispose();
        _connections.Clear();
    }
}

/// <summary>
/// Connection statistics snapshot.
/// </summary>
public sealed class ConnectionStatistics
{
    public int TotalConnections { get; init; }
    public int ActiveConnections { get; init; }
    public int ConnectingConnections { get; init; }
    public int ClosingConnections { get; init; }
    public long TotalMessagesSent { get; init; }
    public long TotalMessagesReceived { get; init; }
    public long TotalErrors { get; init; }
    public TimeSpan AverageConnectionDuration { get; init; }
}
