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
using EndPoint = System.Net.EndPoint;

namespace PulseRPC.Server;

/// <summary>
/// Default RPC server facade. All runtime behavior is owned by one internal
/// <see cref="ServerRuntime"/> shared by the standard, named, and factory entry points.
/// </summary>
public sealed class PulseServer : IPulseServer
{
    private readonly ServerRuntime _runtime;
    private int _eventsDetached;

    public PulseServer(
        ITieredMessageEngine? messageEngine = null,
        IServerChannelManager? channelManager = null,
        ITransportIntegrationManager? transportIntegrationManager = null,
        ILoggerFactory? loggerFactory = null,
        IOptions<PulseServerOptions>? options = null)
        : this(ServerRuntimeComponentFactory.CreateRuntime(
            messageEngine,
            channelManager,
            transportIntegrationManager,
            loggerFactory,
            options))
    {
    }

    internal PulseServer(ServerRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.StateChanged += OnRuntimeStateChanged;
        _runtime.ClientConnected += OnRuntimeClientConnected;
        _runtime.ClientDisconnected += OnRuntimeClientDisconnected;
    }

    internal ServerRuntime Runtime => _runtime;

    public ServerState State => _runtime.State;
    public bool IsRunning => _runtime.IsRunning;
    public int ActiveConnectionCount => _runtime.ActiveConnectionCount;

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _runtime.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _runtime.StopAsync(cancellationToken);

    public IReadOnlyDictionary<string, TransportInfo> GetTransports()
        => _runtime.GetTransports();

    public TransportInfo? GetDefaultTransport()
        => _runtime.GetDefaultTransport();

    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
        => _runtime.GetActiveConnections();

    public Task<int> BroadcastAsync(
        ReadOnlyMemory<byte> data,
        Func<TransportContext, bool>? filter = null,
        CancellationToken cancellationToken = default)
        => _runtime.BroadcastAsync(data, filter, cancellationToken);

    public Task<bool> SendAsync(
        string connectionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => _runtime.SendAsync(connectionId, data, cancellationToken);

    public ITransportChannel? GetChannel(string connectionId)
        => _runtime.GetChannel(connectionId);

    public IReadOnlyList<ITransportChannel> GetAllChannels()
        => _runtime.GetAllChannels();

    [Obsolete("Use GetChannel/GetAllChannels. Runtime channels are owned by PulseServer; pool mutation is not supported.", false)]
    public ITransportChannelPool ChannelPool => _runtime.ChannelPool;

    public IReadOnlyList<ServiceInfo> GetRegisteredServices()
        => _runtime.GetRegisteredServices();

    public ServerPerformanceMetrics GetPerformanceMetrics()
        => _runtime.GetPerformanceMetrics();

    public void ResetPerformanceMetrics()
        => _runtime.ResetPerformanceMetrics();

    public void Dispose()
    {
        try
        {
            _runtime.Dispose();
        }
        finally
        {
            DetachRuntimeEvents();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            DetachRuntimeEvents();
        }
    }

    private void OnRuntimeStateChanged(object? sender, ServerStateChangedEventArgs args)
        => StateChanged?.Invoke(this, args);

    private void OnRuntimeClientConnected(object? sender, ClientConnectedEventArgs args)
        => ClientConnected?.Invoke(this, args);

    private void OnRuntimeClientDisconnected(object? sender, ClientDisconnectedEventArgs args)
        => ClientDisconnected?.Invoke(this, args);

    private void DetachRuntimeEvents()
    {
        if (Interlocked.Exchange(ref _eventsDetached, 1) != 0)
        {
            return;
        }

        _runtime.StateChanged -= OnRuntimeStateChanged;
        _runtime.ClientConnected -= OnRuntimeClientConnected;
        _runtime.ClientDisconnected -= OnRuntimeClientDisconnected;
    }
}

/// <summary>
/// Coordinates one server runtime: listener lifecycle, message engine stop order,
/// and its single authoritative channel registry. DI owns final component disposal;
/// public server facades delegate lifecycle operations to this type.
/// </summary>
internal sealed class ServerRuntime : IPulseServer
{
    [ThreadStatic]
    private static ServerRuntime? s_userCallbackPublisher;

    private readonly ILoggerFactory _loggerFactory;
    private readonly PulseServerOptions _options;
    private readonly IServerChannelManager _channelManager;
    private readonly ITransportIntegrationManager _transportIntegrationManager;
    private readonly ILogger<PulseServer> _logger;

    // Pipeline components
    private readonly ITieredMessageEngine _messageEngine;

    private readonly ConcurrentDictionary<string, IServerListener> _listeners = new();
    private readonly ConcurrentDictionary<string, TransportChannelConfiguration> _transports = new();
    private readonly ITransportChannelPool _channelPool;

    private volatile ServerState _state = ServerState.Stopped;
    private readonly Lock _stateLock = new();
    private readonly Lock _connectionTaskLock = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentDictionary<long, Task> _connectionTasks = new();
    private bool _acceptingConnections;
    private long _connectionTaskId;
    private Task? _startTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private bool _disposed;

    // Performance tracking
    private long _totalConnectionsAccepted;
    private DateTime _lastResetTime = DateTime.UtcNow;

    public ServerState State => _state;
    public bool IsRunning => _state == ServerState.Running;
    public int ActiveConnectionCount => _channelManager.ConnectionCount;
    internal IServerChannelManager ChannelRegistry => _channelManager;
    internal ITieredMessageEngine MessageEngine => _messageEngine;

    // Events
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public ServerRuntime(
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
        _channelPool = new ServerChannelPoolView(_channelManager);
        _transportIntegrationManager = transportIntegrationManager ?? throw new ArgumentNullException(nameof(transportIntegrationManager));
        _channelManager.ChannelDisconnected += OnChannelDisconnected;

        // Initialize pipeline components (if options configured)

        // Add configured transports
        foreach (var transport in _options.Transports)
        {
            _transports.TryAdd(transport.Name, transport);
        }

        SafeLog(() => _logger.LogInformation(
            "PulseServer initialized with {TransportCount} transports",
            _transports.Count));
    }

    // === Lifecycle Management ===

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Task startTask;
        ServerStateChangedEventArgs? stateChange;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(ServerRuntime));

            if (_stopTask is not null)
            {
                return Task.FromException(
                    new InvalidOperationException("A stopped server runtime cannot be restarted."));
            }

            if (_startTask is not null)
            {
                return _startTask;
            }

            stateChange = SetStateLocked(ServerState.Starting);
            lock (_connectionTaskLock)
            {
                _acceptingConnections = true;
            }

            _startTask = StartCoreAsync(cancellationToken);
            startTask = _startTask;
        }

        PublishStateChange(stateChange);
        return startTask;
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdownCts.Token);

        try
        {
            SafeLog(() => _logger.LogInformation(
                "Starting server with {TransportCount} transports",
                _transports.Count));

            if (_transports.Count == 0)
            {
                throw new InvalidOperationException("No transports configured");
            }

            await _messageEngine.StartAsync(combinedCts.Token).ConfigureAwait(false);

            // Start all transports in parallel
            var startTasks = _transports.Values.Select(config =>
                StartTransportAsync(config, combinedCts.Token)).ToArray();

            await Task.WhenAll(startTasks).ConfigureAwait(false);

            ServerStateChangedEventArgs? stateChange;
            lock (_stateLock)
            {
                combinedCts.Token.ThrowIfCancellationRequested();
                if (_stopTask is not null || _disposed || _state != ServerState.Starting)
                {
                    throw new OperationCanceledException(
                        "Server runtime stopped while starting.",
                        combinedCts.Token);
                }

                stateChange = SetStateLocked(ServerState.Running);
            }

            PublishStateChange(stateChange);
            SafeLog(() => _logger.LogInformation(
                "Server started successfully with {ListenerCount} active listeners",
                _listeners.Count));
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex, "Failed to start server"));
            lock (_connectionTaskLock)
            {
                _acceptingConnections = false;
            }

            bool shutdownOwnsRollback;
            lock (_stateLock)
            {
                shutdownOwnsRollback = _state == ServerState.Stopping || _disposed;
            }

            if (!shutdownOwnsRollback)
            {
                await StopAllListenersAsync().ConfigureAwait(false);
                await WaitForConnectionTasksAsync().ConfigureAwait(false);
                StopAcceptingChannelsAndCloseAll();
                try
                {
                    await _messageEngine.StopAsync().ConfigureAwait(false);
                }
                catch (Exception stopException)
                {
                    _logger.LogError(stopException, "Failed to roll back the message engine after server start failure");
                }

                ChangeState(ServerState.Stopped);
            }

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

            try
            {
                await listener.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                listener.ConnectionAccepted -= OnConnectionAccepted;
                try
                {
                    await SafeStopListenerAsync(listener).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogDebug(cleanupException, "Failed to clean up listener after start failure: {Name}", config.Name);
                }

                throw;
            }

            // Add to collection
            if (_listeners.TryAdd(config.Name, listener))
            {
                _logger.LogInformation("Transport listener started: {Name} ({Type}:{Port})",
                    config.Name, config.Type, config.Port);
            }
            else
            {
                await SafeStopListenerAsync(listener).ConfigureAwait(false);
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

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stopTask;
        ServerStateChangedEventArgs? stateChange;
        lock (_stateLock)
        {
            if (_stopTask is not null)
            {
                return ReferenceEquals(s_userCallbackPublisher, this)
                    ? Task.CompletedTask
                    : _stopTask;
            }

            if (_state == ServerState.Stopped)
            {
                return Task.CompletedTask;
            }

            stateChange = SetStateLocked(ServerState.Stopping);
            lock (_connectionTaskLock)
            {
                _acceptingConnections = false;
            }

            _stopTask = StopCoreAsync(cancellationToken);
            stopTask = _stopTask;
        }

        PublishStateChange(stateChange);
        return ReferenceEquals(s_userCallbackPublisher, this)
            ? Task.CompletedTask
            : stopTask;
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        _ = cancellationToken;

        try
        {
                SafeLog(() => _logger.LogInformation("Stopping server..."));

            try
            {
                await _shutdownCts.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception cancellationException)
            {
                    SafeLog(() => _logger.LogError(
                        cancellationException,
                        "Server shutdown cancellation callback failed"));
            }

            Task? startTask;
            lock (_stateLock)
            {
                startTask = _startTask;
            }

            if (startTask is not null)
            {
                try
                {
                    await startTask.ConfigureAwait(false);
                }
                catch
                {
                    // StartCore performs its own rollback; shutdown still joins the remaining components.
                }
            }

            await StopAllListenersAsync().ConfigureAwait(false);
            await WaitForConnectionTasksAsync().ConfigureAwait(false);
            StopAcceptingChannelsAndCloseAll();
            await _messageEngine.StopAsync().ConfigureAwait(false);

            ChangeState(ServerState.Stopped);
            SafeLog(() => _logger.LogInformation("Server stopped successfully"));
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex, "Error during server shutdown"));
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
            SafeLog(() => _logger.LogError(ex, "Error stopping some listeners"));
        }
    }

    private async Task StopListenerAsync(string name, IServerListener listener)
    {
        try
        {
            listener.ConnectionAccepted -= OnConnectionAccepted;
            await SafeStopListenerAsync(listener);
            SafeLog(() => _logger.LogInformation("Listener stopped: {Name}", name));
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex, "Error stopping listener: {Name}", name));
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
            try
            {
                _logger.LogDebug(ex, "Error closing connection accepted during shutdown: {ConnectionId}", transport.Id);
            }
            catch
            {
                // Cleanup must not depend on a custom logger implementation.
            }
        }
        finally
        {
            DisposeRejectedTransport(transport);
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

            var previousPublisher = s_userCallbackPublisher;
            s_userCallbackPublisher = this;
            try
            {
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(channel));
            }
            finally
            {
                s_userCallbackPublisher = previousPublisher;
            }

            _logger.LogInformation("Connection accepted: {ConnectionId}", e.Transport.Id);
        }
        catch (Exception ex)
        {
            try
            {
                _logger.LogError(ex, "Error processing new connection: {ConnectionId}", e.Transport.Id);
            }
            catch
            {
                // Logging must not prevent cleanup of an unpublished/failed connection.
            }

            if (channel != null)
            {
                RemoveChannelIfCurrent(channel);
                try
                {
                    await e.Transport.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception closeEx)
                {
                    try
                    {
                        _logger.LogDebug(
                            closeEx,
                            "Error closing failed published connection: {ConnectionId}",
                            e.Transport.Id);
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                await CloseRejectedConnectionAsync(e.Transport).ConfigureAwait(false);
            }
        }
    }

    private void OnChannelDisconnected(object? sender, ChannelEventArgs e)
    {
        try
        {
            var previousPublisher = s_userCallbackPublisher;
            s_userCallbackPublisher = this;
            try
            {
                ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(e.Channel));
            }
            finally
            {
                s_userCallbackPublisher = previousPublisher;
            }
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
        ServerStateChangedEventArgs? stateChange;
        lock (_stateLock)
        {
            stateChange = SetStateLocked(newState);
        }

        PublishStateChange(stateChange);
    }

    private ServerStateChangedEventArgs? SetStateLocked(ServerState newState)
    {
        var oldState = _state;
        if (oldState == newState)
        {
            return null;
        }

        _state = newState;
        return new ServerStateChangedEventArgs(oldState, newState);
    }

    private void PublishStateChange(ServerStateChangedEventArgs? args)
    {
        if (args is null)
        {
            return;
        }

        SafeLog(() => _logger.LogInformation(
            "Server state changed: {OldState} -> {NewState}",
            args.OldState,
            args.NewState));
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        var previousPublisher = s_userCallbackPublisher;
        s_userCallbackPublisher = this;
        try
        {
            foreach (EventHandler<ServerStateChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "StateChanged handler failed: {OldState} -> {NewState}",
                        args.OldState,
                        args.NewState);
                }
            }
        }
        finally
        {
            s_userCallbackPublisher = previousPublisher;
        }
    }

    private bool RemoveChannelIfCurrent(IServerChannel channel)
    {
        if (_channelManager is IServerChannelRegistryLifetime registryLifetime)
        {
            return registryLifetime.RemoveChannel(channel);
        }

        return ReferenceEquals(_channelManager.GetChannel(channel.Id), channel) &&
               _channelManager.RemoveChannel(channel.Id);
    }

    private void StopAcceptingChannelsAndCloseAll()
    {
        if (_channelManager is IServerChannelRegistryLifetime registryLifetime)
        {
            registryLifetime.StopAcceptingChannelsAndCloseAll();
            return;
        }

        foreach (var channel in _channelManager.GetAllChannels().ToArray())
        {
            if (ReferenceEquals(_channelManager.GetChannel(channel.Id), channel))
            {
                _channelManager.RemoveChannel(channel.Id);
            }
        }
    }

    // === Disposal ===

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_stateLock)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                _disposeTask = DisposeCoreAsync();
            }

            disposeTask = _disposeTask;
        }

        // A synchronous Dispose call from this runtime's StateChanged callback cannot
        // wait for the disposal task whose progress currently depends on that callback.
        // Disposal has already been started/cached above; let the outer lifecycle call join it.
        return ReferenceEquals(s_userCallbackPublisher, this)
            ? ValueTask.CompletedTask
            : new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        Task? stopTask = null;
        var stopUnstartedEngine = false;
        lock (_stateLock)
        {
            if (_stopTask is not null || _state != ServerState.Stopped)
            {
                stopTask = StopAsync();
            }
            else if (_startTask is null)
            {
                stopUnstartedEngine = true;
            }
        }

        if (stopTask is not null)
        {
            try
            {
                await stopTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SafeLog(() => _logger.LogError(ex, "Error stopping server during disposal"));
            }
        }

        if (stopUnstartedEngine)
        {
            try
            {
                await _messageEngine.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SafeLog(() => _logger.LogError(
                    ex,
                    "Error stopping an unstarted message engine during disposal"));
            }
        }

        StopAcceptingChannelsAndCloseAll();
        _channelManager.ChannelDisconnected -= OnChannelDisconnected;
        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DisposeRejectedTransport(IServerTransport transport)
    {
        try
        {
            transport.Dispose();
        }
        catch (Exception ex)
        {
            try
            {
                _logger.LogDebug(ex, "Error disposing unpublished connection: {ConnectionId}", transport.Id);
            }
            catch
            {
                // Cleanup is best-effort after a transport itself violates IDisposable.
            }
        }
    }

    private static void SafeLog(Action logAction)
    {
        try
        {
            logAction();
        }
        catch
        {
            // Listener, channel and engine cleanup must not depend on a logger provider.
        }
    }
}

/// <summary>
/// Compatibility view over the runtime channel registry. It intentionally owns
/// no collection, so <see cref="IPulseServer.ChannelPool"/> cannot diverge from
/// <see cref="IServerChannelManager"/>.
/// </summary>
internal sealed class ServerChannelPoolView(IServerChannelManager channelManager) : ITransportChannelPool
{
    private readonly IServerChannelManager _channelManager =
        channelManager ?? throw new ArgumentNullException(nameof(channelManager));

    public void Register(string connectionId, ITransportChannel channel)
    {
        throw new NotSupportedException(
            "IPulseServer owns runtime channels. Register transports through the configured server listener.");
    }

    public bool Unregister(string connectionId)
        => throw new NotSupportedException(
            "IPulseServer owns runtime channels. Closing a connection is not exposed through the legacy pool view.");

    public ITransportChannel? GetChannel(string connectionId)
        => _channelManager.GetChannel(connectionId) as ITransportChannel;

    public IReadOnlyCollection<ITransportChannel> GetAllChannels()
        => _channelManager.GetAllChannels().OfType<ITransportChannel>().ToArray();

    public IReadOnlyCollection<string> GetAllConnectionIds()
        => _channelManager.GetAllChannels()
            .OfType<ITransportChannel>()
            .Select(channel => channel.ConnectionId)
            .ToArray();

    public bool Contains(string connectionId)
        => GetChannel(connectionId) is not null;

    public int Count => _channelManager.GetAllChannels().Count(channel => channel is ITransportChannel);

    public void Clear()
        => throw new NotSupportedException(
            "IPulseServer owns runtime channels. Stop the server to release all connections.");
}
