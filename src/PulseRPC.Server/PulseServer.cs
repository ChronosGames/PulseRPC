using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Channels;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Health; using PulseRPC.Server.Processing; using PulseRPC.Server.Channels; using PulseRPC.Server.Services; using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Shared;
using BackpressurePolicyCore = PulseRPC.Server.Services.BackpressurePolicy;
using EndPoint = System.Net.EndPoint;

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
    private readonly Lock _connectionTaskLock = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentDictionary<long, Task> _connectionTasks = new();
    private bool _acceptingConnections;
    private long _connectionTaskId;

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

        _messageEngine = messageEngine ?? throw new ArgumentNullException(nameof(messageEngine));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _transportIntegrationManager = transportIntegrationManager ?? throw new ArgumentNullException(nameof(transportIntegrationManager));
        _channelManager.ChannelDisconnected += OnChannelDisconnected;

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

        lock (_connectionTaskLock)
        {
            _acceptingConnections = true;
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

            lock (_connectionTaskLock)
            {
                _acceptingConnections = false;
            }

            // Cleanup started listeners
            await StopAllListenersAsync();
            await WaitForConnectionTasksAsync();
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

            lock (_connectionTaskLock)
            {
                _acceptingConnections = false;
            }

            // Trigger shutdown
            await _shutdownCts.CancelAsync();

            // Stop all listeners
            await StopAllListenersAsync();
            await WaitForConnectionTasksAsync();

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
        // ProcessNewConnectionAsync 在成功路径的首个 await 之前完成 channel 与消息处理器注册。
        // 这里不能切到线程池，否则传输开始收包后首帧可能先于 RegisterConnection 到达而被丢弃。
        Task task;
        var taskId = Interlocked.Increment(ref _connectionTaskId);
        lock (_connectionTaskLock)
        {
            task = _acceptingConnections
                ? ProcessNewConnectionAsync(e)
                : CloseRejectedConnectionAsync(e.Transport);
            _connectionTasks.TryAdd(taskId, task);
        }

        _ = task.ContinueWith(
            (completedTask, state) =>
            {
                var (tasks, id) = ((ConcurrentDictionary<long, Task>, long))state!;
                tasks.TryRemove(id, out _);
            },
            (_connectionTasks, taskId),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task WaitForConnectionTasksAsync()
    {
        Task[] tasks;
        lock (_connectionTaskLock)
        {
            tasks = _connectionTasks.Values.ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task CloseRejectedConnectionAsync(IServerTransport transport)
    {
        try
        {
            await transport.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error closing connection accepted during shutdown: {ConnectionId}", transport.Id);
        }
    }

    private async Task ProcessNewConnectionAsync(ServerConnectionEventArgs e)
    {
        IServerChannel? channel = null;
        try
        {
            Interlocked.Increment(ref _totalConnectionsAccepted);

            _logger.LogDebug("Accepting connection: {ConnectionId} from {RemoteEndPoint}",
                e.Transport.Id, e.Transport.RemoteEndPoint);

            channel = _channelManager.AddChannel(e.Transport);

            ClientConnected?.Invoke(this, new ClientConnectedEventArgs(channel));

            _logger.LogInformation("Connection accepted: {ConnectionId}", e.Transport.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing new connection: {ConnectionId}", e.Transport.Id);

            if (channel != null)
            {
                _channelManager.RemoveChannel(channel.Id);
            }

            try
            {
                await e.Transport.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception closeEx)
            {
                _logger.LogDebug(closeEx, "Error closing failed connection: {ConnectionId}", e.Transport.Id);
            }
        }
    }

    private void OnChannelDisconnected(object? sender, ChannelEventArgs e)
    {
        try
        {
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(e.Channel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClientDisconnected handler failed for connection {ConnectionId}", e.Channel.Id);
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
        return ServiceManifestRegistry.Instance?.Services ?? Array.Empty<ServiceInfo>();
    }

    // === Performance Metrics ===

    public ServerPerformanceMetrics GetPerformanceMetrics()
    {
        var engineStats = _messageEngine.GetStatistics();
        return new ServerPerformanceMetrics
        {
            ActiveConnections = ActiveConnectionCount,
            TotalConnectionsAccepted = Interlocked.Read(ref _totalConnectionsAccepted),
            TotalMessagesProcessed = engineStats.TotalMessagesProcessed,
            TotalMessagesDropped = engineStats.TotalMessagesDropped,
            AverageLatencyMs = engineStats.AverageLatencyMs,
            ThroughputMsgsPerSec = engineStats.CurrentThroughput,
            MemoryUsageMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
            CpuUsagePercent = double.NaN,
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
        if (filter is null)
        {
            return _channelManager.BroadcastAsync(data, cancellationToken);
        }

        return BroadcastFilteredAsync(data, filter, cancellationToken);
    }

    private async Task<int> BroadcastFilteredAsync(
        ReadOnlyMemory<byte> data,
        Func<TransportContext, bool> filter,
        CancellationToken cancellationToken)
    {
        var sent = 0;
        foreach (var channel in _channelManager.GetAuthenticatedChannels())
        {
            using var context = CreateTransportContext(channel);
            if (!filter(context))
            {
                continue;
            }

            try
            {
                if (await channel.SendAsync(data, cancellationToken).ConfigureAwait(false))
                {
                    sent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向通道 {ConnectionId} 发送过滤广播消息失败", channel.Id);
            }
        }

        return sent;
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
        return _channelManager.GetChannel(connectionId) as ITransportChannel;
    }

    /// <inheritdoc />
    public IReadOnlyList<ITransportChannel> GetAllChannels()
    {
        return _channelManager.GetAllChannels().OfType<ITransportChannel>().ToList();
    }

    /// <inheritdoc />
    public ITransportChannelPool ChannelPool => _channelPool;

    private static TransportContext CreateTransportContext(IServerChannel channel)
    {
        ITransport transport = channel is ServerTransportChannel serverChannel
            ? serverChannel.Transport
            : new ServerChannelTransportAdapter(channel);

        return new TransportContext(channel.Id, transport)
        {
            AuthenticationContext = channel.AuthenticationContext,
            LastActiveTime = channel.LastActiveTime
        };
    }

    private sealed class ServerChannelTransportAdapter(IServerChannel channel) : ITransport
    {
        public string Id => channel.Id;
        public TransportType Type => channel.Type;
        public bool IsConnected => State == ConnectionState.Connected;
        public ConnectionState State => ConnectionState.Connected;
        public EndPoint LocalEndPoint => channel.LocalEndPoint;
        public EndPoint RemoteEndPoint => channel.RemoteEndPoint;

        public event EventHandler<TransportStateEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<TransportDataEventArgs>? DataReceived
        {
            add { }
            remove { }
        }

        public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            => channel.SendAsync(data, cancellationToken);

        public void Dispose()
        {
        }
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

        _channelManager.ChannelDisconnected -= OnChannelDisconnected;
        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_state == ServerState.Running)
        {
            await StopAsync();
        }

        _channelManager.ChannelDisconnected -= OnChannelDisconnected;
        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
