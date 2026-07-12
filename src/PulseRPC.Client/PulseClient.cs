using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Authentication;
using PulseRPC.Serialization;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using PulseRPC.Client.Health;
using PulseRPC.Messaging;
using PulseRPC.Shared;

namespace PulseRPC.Client;

/// <summary>
/// PulseRPC 客户端实现
/// </summary>
internal sealed class PulseClient : IPulseClient
{
    private readonly ILogger<PulseClient> _logger;
    private readonly List<ConnectionDescriptor> _initialConnections;
    private readonly ClientOptions _clientOptions;

    // 核心组件（简化后只保留 ConnectionManager 和 LoadBalancer）
    private readonly ConnectionManager _connectionManager;
    private readonly ILoadBalancer _loadBalancer;

    // 状态管理
    private ClientState _state = ClientState.Uninitialized;
    private readonly object _stateLock = new();
    private volatile bool _disposed;

    // 统计信息
    private readonly ClientStatistics _statistics = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    /// <summary>
    /// 连接管理器（统一入口，包含路由、查询和生命周期管理功能）
    /// </summary>
    public IConnectionManager Connections => _connectionManager;

    /// <summary>
    /// 负载均衡器
    /// </summary>
    public ILoadBalancer LoadBalancer => _loadBalancer;

    /// <summary>
    /// 客户端状态
    /// </summary>
    public ClientState State => _state;

    /// <summary>
    /// 客户端状态变化事件
    /// </summary>
    public event EventHandler<ClientStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 构造函数
    /// </summary>
#pragma warning disable CS0618 // Compatibility constructor rejects these unsupported DTOs below.
    public PulseClient(
        IReadOnlyList<ConnectionDescriptor> connections,
        ILoggerFactory? loggerFactory = null,
        ISerializerProvider? serializerProvider = null,
        IAuthenticationProvider? authenticationProvider = null,
        LoadBalancingStrategy loadBalancingStrategy = LoadBalancingStrategy.RoundRobin,
        ConnectionPoolOptions? connectionPoolOptions = null,
        RetryPolicy? retryPolicy = null,
        ClientOptions? clientOptions = null)
    {
#pragma warning restore CS0618
        if (authenticationProvider is not null)
        {
            throw new NotSupportedException(
                "Client authentication is not connected to the transport handshake.");
        }

        if (connectionPoolOptions is not null)
        {
            throw new NotSupportedException(
                "Connection pooling is not connected to ConnectionManager.");
        }

        if (retryPolicy is not null)
        {
            throw new NotSupportedException(
                "RetryPolicy is not connected to client requests or connection attempts.");
        }

        _initialConnections = connections?.ToList() ?? new List<ConnectionDescriptor>();
        _clientOptions = clientOptions ?? new ClientOptions();

        var logger = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = logger.CreateLogger<PulseClient>();

        // 初始化统计信息
        _statistics.ClientName = _clientOptions.Name;
        _statistics.StartTime = _startTime;

        // 创建核心组件（简化后只需要 ConnectionManager 和 LoadBalancer）
        _loadBalancer = CreateLoadBalancer(loadBalancingStrategy, _clientOptions.LoadBalancing, logger);
        _connectionManager = new ConnectionManager(serializerProvider, logger, _loadBalancer);

        _logger.LogInformation("PulseRPC 客户端已创建，初始连接数: {Count}", _initialConnections.Count);
    }

    /// <summary>
    /// 初始化客户端
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_state != ClientState.Uninitialized)
            {
                throw new InvalidOperationException($"客户端已初始化，当前状态: {_state}");
            }

            _state = ClientState.Initializing;
        }

        OnStateChanged(ClientState.Uninitialized, ClientState.Initializing);

        try
        {
            _logger.LogInformation("开始初始化 PulseRPC 客户端");

            // 连接到所有初始连接
            var connectionTasks = _initialConnections.Select(async descriptor =>
            {
                try
                {
                    await _connectionManager.ConnectAsync(descriptor, cancellationToken);
                    _logger.LogInformation("连接成功: {ConnectionId}", descriptor.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "连接失败: {ConnectionId}", descriptor.Id);
                    throw;
                }
            });

            await Task.WhenAll(connectionTasks);

            lock (_stateLock)
            {
                _state = ClientState.Running;
            }

            OnStateChanged(ClientState.Initializing, ClientState.Running);
            _logger.LogInformation("PulseRPC 客户端初始化完成，活跃连接数: {Count}", _connectionManager.Count);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _state = ClientState.Error;
            }

            OnStateChanged(ClientState.Initializing, ClientState.Error, ex);
            _logger.LogError(ex, "PulseRPC 客户端初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 停止客户端
    /// </summary>
    public async Task StopAsync(bool graceful = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ClientState previousState;
        lock (_stateLock)
        {
            if (_state == ClientState.Stopped || _state == ClientState.Stopping)
            {
                return;
            }

            previousState = _state;
            _state = ClientState.Stopping;
        }

        OnStateChanged(previousState, ClientState.Stopping);

        try
        {
            _logger.LogInformation("开始停止 PulseRPC 客户端，优雅停止: {Graceful}", graceful);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (graceful)
            {
                cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            }

            // 断开所有连接
            var connections = _connectionManager.GetAllConnections();
            var disconnectTasks = connections.Select(async connection =>
            {
                try
                {
                    await _connectionManager.DisconnectAsync(connection.Id, graceful, cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "断开连接失败: {ConnectionId}", connection.Id);
                }
            });

            await Task.WhenAll(disconnectTasks);

            lock (_stateLock)
            {
                _state = ClientState.Stopped;
            }

            OnStateChanged(ClientState.Stopping, ClientState.Stopped);
            _logger.LogInformation("PulseRPC 客户端已停止");
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _state = ClientState.Error;
            }

            OnStateChanged(ClientState.Stopping, ClientState.Error, ex);
            _logger.LogError(ex, "停止 PulseRPC 客户端失败");
            throw;
        }
    }

    /// <summary>
    /// 连接到服务
    /// </summary>
    public async Task<IClientChannel> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureRunning();

        return await _connectionManager.ConnectAsync(descriptor, cancellationToken);
    }

    /// <summary>
    /// 运行时动态连接到服务器
    /// </summary>
    public async Task<IClientChannel> ConnectToServerAsync(
        string host,
        int port,
        string? serverId = null,
        string? name = null,
        TransportType transport = TransportType.TCP,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureRunning();

        // 生成 ID 和 Name
        var id = serverId ?? Guid.NewGuid().ToString("N");
        var connectionName = name ?? $"{transport}-{host}:{port}";

        // 创建连接描述符
        var descriptor = transport switch
        {
            TransportType.TCP => ConnectionDescriptor.CreateTcp(id, connectionName, host, port, strategy),
            TransportType.KCP => ConnectionDescriptor.CreateKcp(id, connectionName, host, port, strategy),
            _ => throw new NotSupportedException($"不支持的传输类型: {transport}")
        };

        // 使用现有的 ConnectAsync 方法
        return await ConnectAsync(descriptor, cancellationToken);
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync(string connectionId, bool graceful = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _connectionManager.DisconnectAsync(connectionId, graceful, cancellationToken);
    }

    /// <summary>
    /// 批量断开连接
    /// </summary>
    public async Task<int> DisconnectAsync(Func<IClientChannel, bool> predicate, bool graceful = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var connectionsToDisconnect = _connectionManager.GetAllConnections().Where(predicate).ToList();

        var disconnectTasks = connectionsToDisconnect.Select(async connection =>
        {
            try
            {
                await DisconnectAsync(connection.Id, graceful, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开连接失败: {ConnectionId}", connection.Id);
            }
        });

        await Task.WhenAll(disconnectTasks);
        return connectionsToDisconnect.Count;
    }

    /// <summary>
    /// 获取客户端统计信息
    /// </summary>
    public ClientStatistics GetStatistics()
    {
        ThrowIfDisposed();

        _statistics.Uptime = DateTime.UtcNow - _startTime;
        _statistics.TotalConnections = _connectionManager.Count;
        _statistics.ActiveConnections = _connectionManager.GetAllConnections().Count(c => c.State == ExtendedConnectionState.Connected);
        _statistics.Timestamp = DateTime.UtcNow;

        return _statistics;
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public async Task<ClientHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var checkStart = DateTime.UtcNow;
        var connectionResults = await _connectionManager.PerformHealthChecksAsync(cancellationToken);

        var overallHealth = connectionResults.All(r => r.Health == HealthStatus.Healthy)
            ? HealthStatus.Healthy
            : connectionResults.Any(r => r.Health == HealthStatus.Healthy)
                ? HealthStatus.Degraded
                : HealthStatus.Unhealthy;

        return new ClientHealthCheckResult
        {
            OverallHealth = overallHealth,
            ConnectionResults = connectionResults,
            ServiceDiscoveryHealth = HealthStatus.Unknown,
            CheckedAt = checkStart,
            TotalCheckTime = DateTime.UtcNow - checkStart
        };
    }

    /// <summary>
    /// 创建负载均衡器
    /// </summary>
    private static ILoadBalancer CreateLoadBalancer(
        LoadBalancingStrategy strategy,
        ConnectionLoadBalancingOptions options,
        ILoggerFactory loggerFactory)
    {
        return new ConnectionLoadBalancer(
            strategy,
            options,
            loggerFactory.CreateLogger<ConnectionLoadBalancer>());
    }

    /// <summary>
    /// 确保客户端处于运行状态
    /// </summary>
    private void EnsureRunning()
    {
        if (_state != ClientState.Running)
        {
            throw new InvalidOperationException($"客户端未处于运行状态，当前状态: {_state}");
        }
    }

    /// <summary>
    /// 触发状态变化事件
    /// </summary>
    private void OnStateChanged(ClientState previousState, ClientState currentState, Exception? exception = null)
    {
        var eventArgs = new ClientStateChangedEventArgs
        {
            PreviousState = previousState,
            CurrentState = currentState,
            Exception = exception,
            Timestamp = DateTime.UtcNow
        };

        StateChanged?.Invoke(this, eventArgs);
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PulseClient));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // 停止客户端
            if (_state == ClientState.Running)
            {
                StopAsync().Wait(TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止客户端时发生错误");
        }

        // 释放组件
        _connectionManager?.Dispose();

        _logger.LogInformation("PulseRPC 客户端已释放");
    }
}
