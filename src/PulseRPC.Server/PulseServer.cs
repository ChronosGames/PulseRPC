using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Builder;
using PulseRPC.Server.Integration;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// 增强的服务器管理器 - 集成传输层管理，高性能线程安全设计
/// </summary>
internal sealed class PulseServer : IPulseServer
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServerOptions _serverOptions;
    private readonly IServerChannelManager _channelManager;
    private readonly ITransportIntegrationManager _transportIntegrationManager;
    private readonly ILogger<PulseServer> _logger;

    private readonly ConcurrentDictionary<string, IServerListener> _listeners = new();
    private readonly ConcurrentDictionary<string, TransportChannelConfiguration> _transports = new();

    private volatile ServerState _state = ServerState.Stopped;
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    // 性能统计
    private long _totalConnectionsAccepted;
    private DateTime _lastResetTime = DateTime.UtcNow;

    public ServerState State => _state;
    public bool IsRunning => _state == ServerState.Running;
    public int ActiveConnectionCount => _channelManager.ConnectionCount;

    // 事件
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public PulseServer(
        ILoggerFactory? loggerFactory = null,
        IOptions<ServerOptions>? serverOptions = null,
        IServerChannelManager? channelManager = null,
        ITransportIntegrationManager? transportIntegrationManager = null)
    {
        _loggerFactory = loggerFactory ?? new NullLoggerFactory();
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _transportIntegrationManager = transportIntegrationManager ?? throw new ArgumentNullException(nameof(transportIntegrationManager));
        _serverOptions = serverOptions?.Value ?? new ServerOptions();
        _logger = _loggerFactory.CreateLogger<PulseServer>();

        _logger.LogInformation("增强服务器管理器已初始化，会话管理器类型：{ManagerType}",
            _channelManager.GetType().Name);
    }

    /// <summary>
    /// 添加传输配置
    /// </summary>
    public void AddTransport(TransportChannelConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (_state == ServerState.Running)
        {
            throw new InvalidOperationException("服务器运行中，无法添加传输");
        }

        if (_transports.ContainsKey(config.Name))
        {
            throw new ArgumentException($"传输通道已存在: {config.Name}");
        }

        // 验证传输是否支持
        if (!_transportIntegrationManager.IsSupported(config.Type.ToString()))
        {
            throw new NotSupportedException($"不支持的传输类型: {config.Type}");
        }

        if (_transports.TryAdd(config.Name, config))
        {
            _logger.LogInformation("已添加传输配置: {Name} ({Type}:{Port})", config.Name, config.Type, config.Port);
        }
        else
        {
            throw new InvalidOperationException($"添加传输配置失败: {config.Name}");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

        lock (_stateLock)
        {
            if (_state == ServerState.Running || _state == ServerState.Starting)
            {
                return;
            }

            ChangeState(ServerState.Starting);
        }

        try
        {
            _logger.LogInformation("正在启动服务器，传输数量: {TransportCount}", _transports.Count);

            if (_transports.Count == 0)
            {
                throw new InvalidOperationException("没有配置任何传输通道");
            }

            // 并行启动所有传输 - 提升启动性能
            var startTasks = _transports.Values.Select(config =>
                StartTransportAsync(config, combinedCts.Token)).ToArray();

            await Task.WhenAll(startTasks);

            ChangeState(ServerState.Running);
            _logger.LogInformation("服务器启动完成，活动监听器: {ListenerCount}", _listeners.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动服务器失败");
            ChangeState(ServerState.Stopped);

            // 清理已启动的监听器
            await StopAllListenersAsync();
            throw;
        }
    }

    /// <summary>
    /// 启动单个传输 - 异步高性能实现
    /// </summary>
    private async Task StartTransportAsync(TransportChannelConfiguration config,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("正在启动传输: {Name} ({Type}:{Port})",
                config.Name, config.Type, config.Port);

            // 创建监听器
            var listener = _transportIntegrationManager.CreateListener(config, _loggerFactory);

            // 注册连接接受事件
            listener.ConnectionAccepted += OnConnectionAccepted;

            // 启动监听器
            await listener.StartAsync(cancellationToken);

            // 原子操作添加到监听器集合
            if (_listeners.TryAdd(config.Name, listener))
            {
                _logger.LogInformation("传输监听器已启动: {Name} ({Type}:{Port})",
                    config.Name, config.Type, config.Port);
            }
            else
            {
                // 如果添加失败，清理监听器
                await SafeStopListenerAsync(listener);
                throw new InvalidOperationException($"监听器已存在: {config.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动传输监听器失败: {Name} ({Type}:{Port})",
                config.Name, config.Type, config.Port);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state == ServerState.Stopped || _state == ServerState.Stopping)
            {
                return;
            }

            ChangeState(ServerState.Stopping);
        }

        try
        {
            _logger.LogInformation("正在停止服务器...");

            // 触发内部取消令牌
            _shutdownCts.Cancel();

            // 并行停止所有监听器
            await StopAllListenersAsync();

            ChangeState(ServerState.Stopped);
            _logger.LogInformation("服务器已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止服务器时发生异常");
            ChangeState(ServerState.Stopped);
            throw;
        }
    }

    /// <summary>
    /// 高性能并行停止所有监听器
    /// </summary>
    private async Task StopAllListenersAsync()
    {
        var listeners = _listeners.ToArray(); // 快照，避免并发修改
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
            _logger.LogError(ex, "停止部分监听器时发生异常");
        }
    }

    /// <summary>
    /// 停止单个监听器
    /// </summary>
    private async Task StopListenerAsync(string name, IServerListener listener)
    {
        try
        {
            listener.ConnectionAccepted -= OnConnectionAccepted;
            await SafeStopListenerAsync(listener);
            _logger.LogInformation("监听器已停止: {Name}", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止监听器时发生异常: {Name}", name);
        }
    }

    /// <summary>
    /// 安全停止监听器
    /// </summary>
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
                // 忽略释放异常
            }
        }
    }

    /// <summary>
    /// 处理新连接接受 - 高性能异步处理
    /// </summary>
    private void OnConnectionAccepted(object? sender, ServerConnectionEventArgs e)
    {
        // 使用后台任务避免阻塞监听线程
        _ = Task.Run(async () => await ProcessNewConnectionAsync(e));
    }

    private async Task ProcessNewConnectionAsync(ServerConnectionEventArgs e)
    {
        try
        {
            _logger.LogDebug("接受新连接: {ConnectionId} from {RemoteEndPoint} via {TransportType}", e.Transport.ConnectionId, e.Transport.RemoteEndPoint, e.Transport.Type);

            // TODO: 创建客户端会话并添加到会话管理器
            // 这里需要根据具体的会话实现来创建会话对象
            // var session = CreateClientSession(e.Transport);
            // _sessionManager.AddSession(session);

            _logger.LogInformation("新连接已接受: {ConnectionId}", e.Transport.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理新连接时发生异常: {ConnectionId}", e.Transport.Name);

            // 异步关闭有问题的连接
            _ = Task.Run(async () =>
            {
                try
                {
                    await e.Transport.CloseAsync();
                }
                catch (Exception closeEx)
                {
                    _logger.LogDebug(closeEx, "关闭异常连接时发生异常: {ConnectionId}",
                        e.Transport.Name);
                }
            });
        }
    }

    /// <summary>
    /// 更改服务器状态 - 线程安全
    /// </summary>
    private void ChangeState(ServerState newState)
    {
        var oldState = _state;
        if (oldState == newState) return;

        _state = newState;
        _logger.LogInformation("服务器状态变更: {OldState} -> {NewState}", oldState, newState);

        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(oldState, newState));
    }

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
            LocalEndPoint = listener?.LocalEndPoint
        };
    }

    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
    {
        return _channelManager.GetAllChannels()
            .Select(session => new ConnectionInfo
            {
                ConnectionId = session.ConnectionId,
                RemoteEndPoint = session.RemoteEndPoint,
                TransportType = session.Type,
                IsAuthenticated = session.IsAuthenticated,
                ConnectedTime = session.ConnectedAt,
                LastActiveTime = session.LastActiveTime
            }).ToList();
    }

    public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<System.Net.TransportContext, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<ServiceInfo> GetRegisteredServices()
    {
        // TODO: 从 ServiceRegistry 或其他服务注册表获取
        return new List<ServiceInfo>();
    }

    public ServerPerformanceMetrics GetPerformanceMetrics()
    {
        return new ServerPerformanceMetrics
        {
            ActiveConnections = ActiveConnectionCount,
            TotalConnectionsAccepted = Interlocked.Read(ref _totalConnectionsAccepted),
            TotalMessagesProcessed = 0, // TODO: 实现消息处理统计
            TotalMessagesDropped = 0, // TODO: 实现消息丢弃统计
            AverageLatencyMs = 0, // TODO: 实现延迟统计
            ThroughputMsgsPerSec = 0, // TODO: 实现吞吐量统计
            MemoryUsageMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
            CpuUsagePercent = 0, // TODO: 实现CPU使用率统计
            LastResetTime = _lastResetTime
        };
    }

    public void ResetPerformanceMetrics()
    {
        Interlocked.Exchange(ref _totalConnectionsAccepted, 0);
        _lastResetTime = DateTime.UtcNow;
        _logger.LogInformation("性能统计已重置");
    }

    /// <summary>
    /// 广播消息到所有连接
    /// </summary>
    public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<TransportContext, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        // 使用会话管理器进行广播
        return _channelManager.BroadcastAsync(data, cancellationToken);
    }

    /// <summary>
    /// 向指定连接发送数据
    /// </summary>
    public async Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var session = _channelManager.GetChannel(connectionId);
        if (session != null)
        {
            return await session.SendAsync(data, cancellationToken);
        }
        return false;
    }

    // === 资源释放 ===

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
                _logger.LogError(ex, "释放资源时停止服务器失败");
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
