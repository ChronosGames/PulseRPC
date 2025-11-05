using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Core;
using PulseRPC.Server.MessageEngine;
using PulseRPC.Server.Integration;
using PulseRPC.Server.Models;
using PulseRPC.Server.Pipeline;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;
using BackpressurePolicyCore = PulseRPC.Server.Core.BackpressurePolicy;

namespace PulseRPC.Server;

/// <summary>
/// Unified server implementation consolidating PulseServer and ServerHost functionality.
/// Provides a single, clear API entry point for RPC server management.
/// </summary>
public sealed class UnifiedPulseServer : IPulseServer
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly UnifiedServerOptions _options;
    private readonly IServerChannelManager _channelManager;
    private readonly ITransportIntegrationManager _transportIntegrationManager;
    private readonly ILogger<UnifiedPulseServer> _logger;

    // Pipeline components
    private readonly ITieredMessageEngine _messageEngine;
    private readonly MessageDispatcher _messageDispatcher;
    private readonly ServiceRegistry? _serviceRegistry;
    private readonly BackpressurePolicyCore? _backpressurePolicy;

    private readonly ConcurrentDictionary<string, IServerListener> _listeners = new();
    private readonly ConcurrentDictionary<string, TransportChannelConfiguration> _transports = new();

    private volatile ServerState _state = ServerState.Stopped;
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    // Performance tracking
    private long _totalConnectionsAccepted;
    private DateTime _lastResetTime = DateTime.UtcNow;

    public ServerState State => _state;
    public bool IsRunning => _state == ServerState.Running;
    public int ActiveConnectionCount => _channelManager.ConnectionCount;

    // Events
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public UnifiedPulseServer(
        ITieredMessageEngine? messageEngine = null,
        IServerChannelManager? channelManager = null,
        ITransportIntegrationManager? transportIntegrationManager = null,
        ILoggerFactory? loggerFactory = null,
        IOptions<UnifiedServerOptions>? options = null)
    {
        _loggerFactory = loggerFactory ?? new NullLoggerFactory();
        _logger = _loggerFactory.CreateLogger<UnifiedPulseServer>();
        _options = options?.Value ?? new UnifiedServerOptions();

        // Validate configuration
        _options.Validate();

        _messageEngine = messageEngine ?? throw new ArgumentNullException(nameof(messageEngine));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _transportIntegrationManager = transportIntegrationManager ?? throw new ArgumentNullException(nameof(transportIntegrationManager));

        // Initialize pipeline components (if options configured)
        _messageDispatcher = new MessageDispatcher(_options.MessageDispatcher);
        _serviceRegistry = new ServiceRegistry(_options.ServiceRegistry);
        _backpressurePolicy = new BackpressurePolicyCore(_options.BackpressurePolicy);

        // Add configured transports
        foreach (var transport in _options.Transports)
        {
            _transports.TryAdd(transport.Name, transport);
        }

        _logger.LogInformation("UnifiedPulseServer initialized with {TransportCount} transports", _transports.Count);
    }

    // === Lifecycle Management ===

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

        lock (_stateLock)
        {
            if (_state is ServerState.Running or ServerState.Starting)
            {
                _logger.LogWarning("Server is already running or starting");
                return;
            }

            ChangeState(ServerState.Starting);
        }

        try
        {
            _logger.LogInformation("Starting server with {TransportCount} transports", _transports.Count);

            if (_transports.Count == 0)
            {
                throw new InvalidOperationException("No transports configured");
            }

            // Start pipeline components
            await _messageEngine.StartAsync(combinedCts.Token);

            await _messageDispatcher.StartAsync(combinedCts.Token);

            // Start all transports in parallel
            var startTasks = _transports.Values.Select(config =>
                StartTransportAsync(config, combinedCts.Token)).ToArray();

            await Task.WhenAll(startTasks);

            ChangeState(ServerState.Running);
            _logger.LogInformation("Server started successfully with {ListenerCount} active listeners", _listeners.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start server");
            ChangeState(ServerState.Stopped);

            // Cleanup started listeners
            await StopAllListenersAsync();
            throw;
        }
    }

    private async Task StartTransportAsync(TransportChannelConfiguration config, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting transport: {Name} ({Type}:{Port})", config.Name, config.Type, config.Port);

            // Create listener
            var listener = _transportIntegrationManager.CreateListener(config, _loggerFactory);

            // Subscribe to events
            listener.ConnectionAccepted += OnConnectionAccepted;

            // Start listener
            await listener.StartAsync(cancellationToken);

            // Add to collection
            if (_listeners.TryAdd(config.Name, listener))
            {
                _logger.LogInformation("Transport listener started: {Name} ({Type}:{Port})",
                    config.Name, config.Type, config.Port);
            }
            else
            {
                await SafeStopListenerAsync(listener);
                throw new InvalidOperationException($"Listener already exists: {config.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start transport: {Name} ({Type}:{Port})",
                config.Name, config.Type, config.Port);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state is ServerState.Stopped or ServerState.Stopping)
            {
                return;
            }

            ChangeState(ServerState.Stopping);
        }

        try
        {
            _logger.LogInformation("Stopping server...");

            // Trigger shutdown
            await _shutdownCts.CancelAsync();

            // Stop all listeners
            await StopAllListenersAsync();

            // Stop pipeline components
            await _messageEngine.StopAsync();
            await _messageDispatcher.StopAsync(cancellationToken);

            ChangeState(ServerState.Stopped);
            _logger.LogInformation("Server stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during server shutdown");
            ChangeState(ServerState.Stopped);
            throw;
        }
    }

    private async Task StopAllListenersAsync()
    {
        var listeners = _listeners.ToArray();
        _listeners.Clear();

        if (listeners.Length == 0) return;

        var stopTasks = listeners.Select(kvp =>
            StopListenerAsync(kvp.Key, kvp.Value)).ToArray();

        try
        {
            await Task.WhenAll(stopTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping some listeners");
        }
    }

    private async Task StopListenerAsync(string name, IServerListener listener)
    {
        try
        {
            listener.ConnectionAccepted -= OnConnectionAccepted;
            await SafeStopListenerAsync(listener);
            _logger.LogInformation("Listener stopped: {Name}", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping listener: {Name}", name);
        }
    }

    private static async Task SafeStopListenerAsync(IServerListener listener)
    {
        try
        {
            await listener.StopAsync();
        }
        finally
        {
            try
            {
                listener.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    // === Connection Management ===

    private void OnConnectionAccepted(object? sender, ServerConnectionEventArgs e)
    {
        // Non-blocking connection processing
        _ = Task.Run(async () => await ProcessNewConnectionAsync(e));
    }

    private async Task ProcessNewConnectionAsync(ServerConnectionEventArgs e)
    {
        try
        {
            Interlocked.Increment(ref _totalConnectionsAccepted);

            _logger.LogDebug("Accepting connection: {ConnectionId} from {RemoteEndPoint}",
                e.Transport.Id, e.Transport.RemoteEndPoint);

            _channelManager.AddChannel(e.Transport);

            ClientConnected?.Invoke(this, new ClientConnectedEventArgs(e.Transport as IServerChannel
                ?? throw new InvalidOperationException("Transport is not IServerChannel")));

            _logger.LogInformation("Connection accepted: {ConnectionId}", e.Transport.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing new connection: {ConnectionId}", e.Transport.Id);

            // Close problematic connection
            _ = Task.Run(async () =>
            {
                try
                {
                    await e.Transport.CloseAsync();
                }
                catch (Exception closeEx)
                {
                    _logger.LogDebug(closeEx, "Error closing failed connection: {ConnectionId}", e.Transport.Id);
                }
            });
        }
    }

    // === Service Registration ===

    public void RegisterService<TService>(string serviceName, TService serviceInstance, ServiceOptions? options = null)
        where TService : class
    {
        if (_serviceRegistry == null)
            throw new InvalidOperationException("Service registry not initialized");

        _serviceRegistry.RegisterService(serviceName, serviceInstance, options);

        var handler = _serviceRegistry.GetServiceHandler(serviceName);
        if (handler != null && _messageDispatcher != null)
        {
            _messageDispatcher.RegisterServiceHandler(serviceName, handler);
        }

        _logger.LogInformation("Service registered: {ServiceName}", serviceName);
    }

    public bool UnregisterService(string serviceName)
    {
        if (_serviceRegistry == null)
            return false;

        _messageDispatcher?.UnregisterServiceHandler(serviceName);
        var result = _serviceRegistry.UnregisterService(serviceName);

        if (result)
            _logger.LogInformation("Service unregistered: {ServiceName}", serviceName);

        return result;
    }

    // === Query Methods ===

    public IReadOnlyDictionary<string, TransportInfo> GetTransports()
    {
        return _transports.ToDictionary(
            kvp => kvp.Key,
            kvp => new TransportInfo
            {
                Name = kvp.Value.Name,
                Type = kvp.Value.Type,
                Port = kvp.Value.Port,
                IsDefault = kvp.Value.IsDefault,
                IsListening = _listeners.TryGetValue(kvp.Key, out var listener) && listener.IsListening,
                LocalEndPoint = _listeners.TryGetValue(kvp.Key, out var l) ? l.LocalEndPoint : null
            });
    }

    public TransportInfo? GetDefaultTransport()
    {
        var defaultTransport = _transports.Values.FirstOrDefault(t => t.IsDefault);
        if (defaultTransport == null) return null;

        return new TransportInfo
        {
            Name = defaultTransport.Name,
            Type = defaultTransport.Type,
            Port = defaultTransport.Port,
            IsDefault = true,
            IsListening = _listeners.TryGetValue(defaultTransport.Name, out var listener) && listener.IsListening,
            LocalEndPoint = _listeners.TryGetValue(defaultTransport.Name, out var l) ? l.LocalEndPoint : null
        };
    }

    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
    {
        return _channelManager.GetAllChannels()
            .Select(channel => new ConnectionInfo
            {
                ConnectionId = channel.Id,
                RemoteEndPoint = channel.RemoteEndPoint,
                TransportType = channel.Type,
                IsAuthenticated = channel.IsAuthenticated,
                ConnectedTime = channel.ConnectedAt,
                LastActiveTime = channel.LastActiveTime
            }).ToList();
    }

    public IReadOnlyList<ServiceInfo> GetRegisteredServices()
    {
        if (_serviceRegistry == null)
            return Array.Empty<ServiceInfo>();

        // TODO: Implement proper service info retrieval from registry
        return Array.Empty<ServiceInfo>();
    }

    // === Performance Metrics ===

    public ServerPerformanceMetrics GetPerformanceMetrics()
    {
        return new ServerPerformanceMetrics
        {
            ActiveConnections = ActiveConnectionCount,
            TotalConnectionsAccepted = Interlocked.Read(ref _totalConnectionsAccepted),
            TotalMessagesProcessed = _messageDispatcher?.TotalMessagesDispatched ?? 0,
            TotalMessagesDropped = 0, // TODO: Track dropped messages
            AverageLatencyMs = 0, // TODO: Implement latency tracking
            ThroughputMsgsPerSec = 0, // TODO: Implement throughput calculation
            MemoryUsageMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
            CpuUsagePercent = 0, // TODO: Implement CPU usage tracking
            LastResetTime = _lastResetTime
        };
    }

    public void ResetPerformanceMetrics()
    {
        Interlocked.Exchange(ref _totalConnectionsAccepted, 0);
        _lastResetTime = DateTime.UtcNow;
        _logger.LogInformation("Performance metrics reset");
    }

    // === Broadcasting ===

    public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<System.Net.TransportContext, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        return _channelManager.BroadcastAsync(data, cancellationToken);
    }

    public async Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var channel = _channelManager.GetChannel(connectionId);
        if (channel != null)
        {
            return await channel.SendAsync(data, cancellationToken);
        }
        return false;
    }

    // === State Management ===

    private void ChangeState(ServerState newState)
    {
        var oldState = _state;
        if (oldState == newState) return;

        lock (_stateLock)
        {
            _state = newState;
        }

        _logger.LogInformation("Server state changed: {OldState} -> {NewState}", oldState, newState);
        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(oldState, newState));
    }

    // === Disposal ===

    public void Dispose()
    {
        if (_state == ServerState.Running)
        {
            try
            {
                StopAsync().Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping server during disposal");
            }
        }

        _shutdownCts.Dispose();
        _messageDispatcher?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_state == ServerState.Running)
        {
            await StopAsync();
        }

        _shutdownCts.Dispose();
        _messageDispatcher?.Dispose();
        GC.SuppressFinalize(this);
    }
}
