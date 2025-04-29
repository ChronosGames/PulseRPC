using Microsoft.Extensions.Logging;
using PulseRPC.Internal;
using PulseRPC.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server;

/// <summary>
/// Main server class for PulseRPC.
/// Listens for incoming connections and manages client sessions.
/// </summary>
public class PulseServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ServiceRegistry _serviceRegistry;
    private readonly HubRegistry _hubRegistry;
    private readonly ILogger _logger;
    private bool _isRunning;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, ClientSession> _activeSessions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseServer"/> class.
    /// </summary>
    /// <param name="endpoint">The IP endpoint to listen on.</param>
    /// <param name="logger">Logger instance.</param>
    public PulseServer(IPEndPoint endpoint, ILogger logger)
    {
        _listener = new TcpListener(endpoint);
        _serviceRegistry = new ServiceRegistry();
        _hubRegistry = new HubRegistry(this); // Pass server reference for broadcasting
        _logger = logger;
    }

    /// <summary>
    /// Registers a service implementation.
    /// </summary>
    /// <typeparam name="TService">The service interface type.</typeparam>
    /// <param name="implementation">The service implementation instance.</param>
    public void RegisterService<TService>(TService implementation)
        where TService : class, IPulseService<TService> // Added class constraint
    {
        _serviceRegistry.Register<TService>(implementation);
        _logger.LogInformation("Registered service: {ServiceName}", typeof(TService).FullName);
    }

    /// <summary>
    /// Registers a Hub implementation.
    /// </summary>
    /// <typeparam name="THub">The Hub interface type.</typeparam>
    /// <typeparam name="TReceiver">The Hub receiver interface type.</typeparam>
    /// <param name="implementationType">The Hub implementation type.</param>
    public void RegisterHub<THub, TReceiver>(Type implementationType)
        where THub : class, IPulseHub<THub, TReceiver> // Added class constraint
    {
        _hubRegistry.Register<THub, TReceiver>(implementationType);
        _logger.LogInformation("Registered hub: {HubName}", typeof(THub).FullName);
    }

    /// <summary>
    /// Starts the server and begins listening for connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop the server.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _isRunning = true;
        _logger.LogInformation("PulseRPC server started on {Endpoint}", _listener.LocalEndpoint);

        try
        {
            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                // Don't wait for HandleClientAsync to complete
                _ = HandleClientAsync(client, _cts.Token);
            }
        }
        catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
        {
            _logger.LogInformation("Server shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting client connections");
        }
        finally
        {
            _listener.Stop();
            _isRunning = false;
            _logger.LogInformation("PulseRPC server stopped.");
            // Clean up remaining sessions
            await CleanupSessionsAsync();
        }
    }

    /// <summary>
    /// Stops the server.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString();
        var clientSession = new ClientSession(client, clientId, _serviceRegistry, _hubRegistry, _logger, OnSessionDisconnected);
        if (!_activeSessions.TryAdd(clientId, clientSession))
        {
            _logger.LogWarning("Failed to add client session {ClientId}", clientId);
            await clientSession.DisposeAsync();
            return;
        }

        _logger.LogInformation("Client {ClientId} connected from {RemoteEndPoint}", clientId, client.Client.RemoteEndPoint);

        try
        {
            await clientSession.ProcessAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             _logger.LogDebug("Processing canceled for client {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing client {ClientId}", clientId);
        }
        finally
        {
             // Session cleanup is handled by OnSessionDisconnected callback
        }
    }

    private async void OnSessionDisconnected(string clientId)
    {
        if (_activeSessions.TryRemove(clientId, out var session))
        {
            _logger.LogInformation("Client {ClientId} disconnected.", clientId);
            // Perform any additional cleanup related to the session if needed
            await session.DisposeAsync();
        }
    }

     private async Task CleanupSessionsAsync()
    {
        var cleanupTasks = new List<Task>();
        foreach (var session in _activeSessions.Values)
        {
            cleanupTasks.Add(session.DisposeAsync().AsTask());
        }
        _activeSessions.Clear();
        await Task.WhenAll(cleanupTasks);
        _logger.LogInformation("All active client sessions cleaned up.");
    }

    /// <summary>
    /// Broadcasts an event to all clients connected to a specific Hub group.
    /// </summary>
    internal async Task BroadcastToGroupAsync(string groupName, PulseEvent eventPacket, CancellationToken cancellationToken = default)
    {
        var sessionsInGroup = _activeSessions.Values.Where(s => s.IsInGroup(groupName));
        var broadcastTasks = sessionsInGroup.Select(session => session.SendEventAsync(eventPacket, cancellationToken));
        await Task.WhenAll(broadcastTasks);
    }

    /// <summary>
    /// Sends an event to a specific client.
    /// </summary>
    internal async Task SendToClientAsync(string clientId, PulseEvent eventPacket, CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(clientId, out var session))
        {
            await session.SendEventAsync(eventPacket, cancellationToken);
        }
    }

    /// <summary>
    /// Sends an event to all connected clients (excluding those in specified groups, if any).
    /// </summary>
    internal async Task BroadcastToAllAsync(PulseEvent eventPacket, IEnumerable<string>? excludedGroups = null, CancellationToken cancellationToken = default)
    {
        var excludedClientIds = new HashSet<string>();
        if (excludedGroups != null)
        {
            foreach (var groupName in excludedGroups)
            {
                 foreach (var session in _activeSessions.Values.Where(s => s.IsInGroup(groupName)))
                 {
                     excludedClientIds.Add(session.ClientId);
                 }
            }
        }

        var targetSessions = _activeSessions.Values.Where(s => !excludedClientIds.Contains(s.ClientId));
        var broadcastTasks = targetSessions.Select(session => session.SendEventAsync(eventPacket, cancellationToken));
        await Task.WhenAll(broadcastTasks);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Stop();
        await CleanupSessionsAsync();
        _cts?.Dispose();
    }
}
