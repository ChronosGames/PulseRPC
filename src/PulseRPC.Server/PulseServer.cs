using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Channels;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Health; using PulseRPC.Server.Processing; using PulseRPC.Server.Channels; using PulseRPC.Server.Services; using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using BackpressurePolicyCore = PulseRPC.Server.Services.BackpressurePolicy;

namespace PulseRPC.Server;

/// <summary>
/// Default RPC server implementation providing a single, clear API entry point
/// for transport, connection, and message pipeline management.
/// </summary>
public sealed class PulseServer : IPulseServer
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly PulseServerOptions _options;
    private readonly IServerChannelManager _channelManager;
    private readonly ITransportIntegrationManager _transportIntegrationManager;
    private readonly ILogger<PulseServer> _logger;

    // Pipeline components
    private readonly ITieredMessageEngine _messageEngine;
    private readonly BackpressurePolicyCore? _backpressurePolicy;

    private readonly ConcurrentDictionary<string, IServerListener> _listeners = new();
    private readonly ConcurrentDictionary<string, TransportChannelConfiguration> _transports = new();
    private readonly ITransportChannelPool _channelPool = new TransportChannelPool();

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

    public PulseServer(
        ITieredMessageEngine? messageEngine = null,
        IServerChannelManager? channelManager = null,
        ITransportIntegrationManager? transportIntegrationManager = null,
        ILoggerFactory? loggerFactory = null,
        IOptions<PulseServerOptions>? options = null)
    {
        _loggerFactory = loggerFactory ?? new NullLoggerFactory();
        _logger = _loggerFactory.CreateLogger<PulseServer>();
        _options = options?.Value ?? new PulseServerOptions();

        // Validate configuration
        _options.Validate();

        // P-6：按配置启用/禁用 client-facing 可见性门闸的强制检查（默认关闭，向后兼容）
        Security.ClientFacingGate.EnforcementEnabled = _options.EnableClientFacingGate;

        _messageEngine = messageEngine ?? throw new ArgumentNullException(nameof(messageEngine));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _transportIntegrationManager = transportIntegrationManager ?? throw new ArgumentNullException(nameof(transportIntegrationManager));

        // Initialize pipeline components (if options configured)
        _backpressurePolicy = new BackpressurePolicyCore(_options.BackpressurePolicy);

        // Add configured transports
        foreach (var transport in _options.Transports)
        {
            _transports.TryAdd(transport.Name, transport);
        }

        _logger.LogInformation("PulseServer initialized with {TransportCount} transports", _transports.Count);
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
    // NOTE: Service registration is now handled by IServiceRoutingTable (generated by SourceGenerator)
    // The old string-based registration methods have been removed.
    // Services are automatically discovered and routed via ProtocolId at compile time.

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
        // Services are now registered via IServiceRoutingTable (generated by SourceGenerator)
        // This method returns an empty list as service discovery is compile-time
        return Array.Empty<ServiceInfo>();
    }

    // === Performance Metrics ===

    public ServerPerformanceMetrics GetPerformanceMetrics()
    {
        return new ServerPerformanceMetrics
        {
            ActiveConnections = ActiveConnectionCount,
            TotalConnectionsAccepted = Interlocked.Read(ref _totalConnectionsAccepted),
            TotalMessagesProcessed = _messageEngine.GetStatistics().TotalMessagesProcessed,
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

    public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<TransportContext, bool>? filter = null, CancellationToken cancellationToken = default)
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

    // === Transport Channel Management (双向RPC支持) ===

    /// <inheritdoc />
    public ITransportChannel? GetChannel(string connectionId)
    {
        // TODO: 在第一阶段实现 IServerChannel 到 ITransportChannel 的适配器
        // 目前返回 null，因为 IServerChannel 还未实现 ITransportChannel
        _logger.LogDebug("GetChannel called for {ConnectionId} - returning null (adapter not yet implemented)", connectionId);
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<ITransportChannel> GetAllChannels()
    {
        // TODO: 在第一阶段实现 IServerChannel 到 ITransportChannel 的适配器
        // 目前返回空列表，因为 IServerChannel 还未实现 ITransportChannel
        _logger.LogDebug("GetAllChannels called - returning empty list (adapter not yet implemented)");
        return Array.Empty<ITransportChannel>();
    }

    /// <inheritdoc />
    public ITransportChannelPool ChannelPool => _channelPool;

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
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_state == ServerState.Running)
        {
            await StopAsync();
        }

        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
