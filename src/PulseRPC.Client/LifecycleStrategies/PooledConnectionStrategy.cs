using Microsoft.Extensions.Logging;
using PulseRPC.Client.Core.ConnectionPool;

namespace PulseRPC.Client.Core.LifecycleStrategies;

/// <summary>
/// 连接池连接策略 - 与连接池集成，管理池化连接的生命周期
/// </summary>
public sealed class PooledConnectionStrategy : ConnectionLifecycleStrategyBase
{
    private readonly IConnectionPoolFactory _poolFactory;
    private readonly Dictionary<string, PooledConnectionInfo> _pooledInfos = new();
    private readonly Dictionary<string, IConnectionPool> _associatedPools = new();
    private readonly object _poolLock = new();

    /// <summary>
    /// 策略名称
    /// </summary>
    public override string Name => "Pooled";

    /// <summary>
    /// 连接策略类型
    /// </summary>
    public override ConnectionStrategy Strategy => ConnectionStrategy.Pooled;

    /// <summary>
    /// 是否支持自动重连
    /// </summary>
    public override bool SupportsAutoReconnect => true;

    /// <summary>
    /// 是否支持连接池
    /// </summary>
    public override bool SupportsPooling => true;

    /// <summary>
    /// 构造函数
    /// </summary>
    public PooledConnectionStrategy(
        IConnectionManager connectionManager,
        IConnectionPoolFactory poolFactory,
        LifecycleStrategyOptions? options = null,
        ILogger<PooledConnectionStrategy>? logger = null)
        : base(connectionManager, options, logger)
    {
        _poolFactory = poolFactory ?? throw new ArgumentNullException(nameof(poolFactory));

        // 连接池连接的默认配置
        if (options == null)
        {
            _options.MaxReconnectAttempts = 3;
            _options.DisconnectOnIdle = false; // 池化连接不因空闲断开
            _options.EnableHeartbeat = true; // 启用心跳检测
            _options.HeartbeatInterval = TimeSpan.FromMinutes(1);
            _options.MaxConnectionLifetime = TimeSpan.FromHours(4); // 较长的存活时间
        }

        _logger.LogDebug("连接池连接策略已创建");
    }

    /// <summary>
    /// 初始化策略
    /// </summary>
    protected override async Task OnInitializeAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("初始化连接池连接策略: {ConnectionId}", descriptor.Id);

        // 验证配置是否适合连接池连接
        if (descriptor.Strategy != ConnectionStrategy.Pooled)
        {
            _logger.LogWarning("连接描述符策略不是 Pooled，但使用了连接池连接策略: {ConnectionId}", descriptor.Id);
        }

        await base.OnInitializeAsync(descriptor, cancellationToken);
    }

    /// <summary>
    /// 创建连接
    /// </summary>
    public override async Task<IConnectionContext> CreateConnectionAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("创建连接池连接: {ConnectionId}", descriptor.Id);

        // 对于连接池连接，不直接创建，而是从连接池获取
        var poolName = GetPoolName(descriptor);
        var pool = await GetOrCreatePoolAsync(poolName, descriptor);

        var lease = await pool.AcquireAsync(cancellationToken);
        var connection = lease.Connection;

        // 记录连接与连接池的关联
        lock (_poolLock)
        {
            _pooledInfos[connection.Id] = new PooledConnectionInfo(connection, pool, lease);
        }

        // 注册到管理列表
        var lifecycleInfo = new ConnectionLifecycleInfo(connection, DateTime.UtcNow);
        _managedConnections.TryAdd(connection.Id, lifecycleInfo);
        connection.StateChanged += OnConnectionStateChanged;

        _statistics.ManagedConnections = _managedConnections.Count;
        _logger.LogDebug("连接池连接已创建: {ConnectionId}, 连接池: {PoolName}", connection.Id, poolName);

        return connection;
    }

    /// <summary>
    /// 管理连接生命周期
    /// </summary>
    protected override async Task OnManageConnectionAsync(IConnectionContext connection, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("管理连接池连接: {ConnectionId}", connection.Id);

        // 连接池连接的生命周期主要由连接池管理
        // 策略主要负责监控和协调

        lock (_poolLock)
        {
            if (!_pooledInfos.ContainsKey(connection.Id))
            {
                _logger.LogWarning("管理的连接不在连接池记录中: {ConnectionId}", connection.Id);
                return;
            }
        }

        // 检查连接状态，如果连接失败，可能需要从连接池中移除
        if (connection.State == ExtendedConnectionState.Failed ||
            connection.State == ExtendedConnectionState.Disposed)
        {
            await HandleFailedPooledConnectionAsync(connection);
        }

        await base.OnManageConnectionAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    protected override async Task OnConnectionDisconnectedInternalAsync(IConnectionContext connection, string reason, Exception? exception = null)
    {
        _logger.LogInformation("连接池连接断开: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        lock (_poolLock)
        {
            if (_pooledInfos.TryGetValue(connection.Id, out var pooledInfo))
            {
                pooledInfo.LastDisconnectedAt = DateTime.UtcNow;
                pooledInfo.DisconnectReason = reason;
            }
        }

        // 连接池连接断开后，归还给连接池（如果连接仍然有效）
        await ReturnConnectionToPoolAsync(connection, reason);

        await base.OnConnectionDisconnectedInternalAsync(connection, reason, exception);
    }

    /// <summary>
    /// 处理连接失败
    /// </summary>
    protected override async Task OnConnectionFailedInternalAsync(IConnectionContext connection, Exception exception)
    {
        _logger.LogError(exception, "连接池连接失败: {ConnectionId}", connection.Id);

        lock (_poolLock)
        {
            if (_pooledInfos.TryGetValue(connection.Id, out var pooledInfo))
            {
                pooledInfo.FailureCount++;
                pooledInfo.LastFailureAt = DateTime.UtcNow;
                pooledInfo.LastException = exception;
            }
        }

        // 连接失败，需要从连接池中移除
        await HandleFailedPooledConnectionAsync(connection);

        await base.OnConnectionFailedInternalAsync(connection, exception);
    }

    /// <summary>
    /// 检查连接是否应该保持活跃
    /// </summary>
    protected override bool ShouldKeepAliveInternal(IConnectionContext connection, TimeSpan idleDuration)
    {
        // 连接池连接的保活主要由连接池策略决定
        lock (_poolLock)
        {
            if (_pooledInfos.TryGetValue(connection.Id, out var pooledInfo))
            {
                // 检查连接池是否还在运行
                if (pooledInfo.Pool.State != ConnectionPoolState.Running)
                {
                    _logger.LogDebug("连接池已停止，连接将断开: {ConnectionId}", connection.Id);
                    return false;
                }

                // 检查连接是否超过最大存活时间
                if (_options.MaxConnectionLifetime.HasValue)
                {
                    var lifetime = DateTime.UtcNow - pooledInfo.CreatedAt;
                    if (lifetime > _options.MaxConnectionLifetime.Value)
                    {
                        _logger.LogDebug("连接池连接超过最大存活时间，将断开: {ConnectionId}, 存活时间: {Lifetime}",
                            connection.Id, lifetime);
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 清理连接
    /// </summary>
    protected override async Task OnCleanupConnectionAsync(IConnectionContext connection, string reason)
    {
        _logger.LogInformation("清理连接池连接: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        // 确保连接被归还给连接池
        await ReturnConnectionToPoolAsync(connection, reason);

        // 清理连接池信息
        lock (_poolLock)
        {
            if (_pooledInfos.TryGetValue(connection.Id, out var pooledInfo))
            {
                _logger.LogDebug("连接池连接统计 - ID: {ConnectionId}, 存活时长: {Lifetime}, 失败次数: {Failures}",
                    connection.Id,
                    DateTime.UtcNow - pooledInfo.CreatedAt,
                    pooledInfo.FailureCount);

                _pooledInfos.Remove(connection.Id);
            }
        }

        await base.OnCleanupConnectionAsync(connection, reason);
    }

    /// <summary>
    /// 获取或创建连接池
    /// </summary>
    private async Task<IConnectionPool> GetOrCreatePoolAsync(string poolName, ConnectionDescriptor descriptor)
    {
        lock (_poolLock)
        {
            if (_associatedPools.TryGetValue(poolName, out var existingPool))
            {
                return existingPool;
            }
        }

        // 创建连接池选项
        var poolOptions = CreatePoolOptions(descriptor);
        var pool = _poolFactory.CreatePool(poolName, descriptor, poolOptions);

        await pool.InitializeAsync();

        lock (_poolLock)
        {
            _associatedPools[poolName] = pool;
        }

        _logger.LogInformation("为连接池连接策略创建新连接池: {PoolName}", poolName);
        return pool;
    }

    /// <summary>
    /// 创建连接池选项
    /// </summary>
    private ConnectionPoolOptions CreatePoolOptions(ConnectionDescriptor descriptor)
    {
        // 根据连接描述符创建合适的连接池选项
        var strategy = PoolingStrategy.Dynamic; // 默认使用动态策略

        // 可以根据连接的特性调整策略
        if (descriptor.Tags?.ContainsKey("pool_strategy") == true)
        {
            if (Enum.TryParse<PoolingStrategy>(descriptor.Tags["pool_strategy"], out var customStrategy))
            {
                strategy = customStrategy;
            }
        }

        return new ConnectionPoolOptions
        {
            Strategy = strategy,
            MinSize = 2,
            MaxSize = 10,
            IdleTimeout = TimeSpan.FromMinutes(10),
            AcquireTimeout = TimeSpan.FromSeconds(30),
            ValidateOnAcquire = true,
            WarmUp = true,
            MaxConnectionAge = _options.MaxConnectionLifetime ?? TimeSpan.FromHours(4)
        };
    }

    /// <summary>
    /// 获取连接池名称
    /// </summary>
    private static string GetPoolName(ConnectionDescriptor descriptor)
    {
        // 使用服务名称或端点作为连接池名称
        if (!string.IsNullOrEmpty(descriptor.ServiceName))
        {
            return $"pool-{descriptor.ServiceName}";
        }

        if (descriptor.Endpoint != null)
        {
            return $"pool-{descriptor.Endpoint.Host}-{descriptor.Endpoint.Port}";
        }

        return $"pool-{descriptor.Id}";
    }

    /// <summary>
    /// 处理失败的连接池连接
    /// </summary>
    private async Task HandleFailedPooledConnectionAsync(IConnectionContext connection)
    {
        lock (_poolLock)
        {
            if (!_pooledInfos.TryGetValue(connection.Id, out var pooledInfo))
            {
                return;
            }

            // 标记连接租借为无效
            pooledInfo.Lease.MarkInvalid("连接失败");
        }

        // 归还无效连接给连接池
        await ReturnConnectionToPoolAsync(connection, "连接失败");
    }

    /// <summary>
    /// 将连接归还给连接池
    /// </summary>
    private async Task ReturnConnectionToPoolAsync(IConnectionContext connection, string reason)
    {
        lock (_poolLock)
        {
            if (!_pooledInfos.TryGetValue(connection.Id, out var pooledInfo))
            {
                return;
            }

            try
            {
                // 归还连接租借
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await pooledInfo.Pool.ReleaseAsync(pooledInfo.Lease);
                        _logger.LogDebug("连接已归还给连接池: {ConnectionId}, 原因: {Reason}", connection.Id, reason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "归还连接到连接池失败: {ConnectionId}", connection.Id);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "归还连接到连接池时发生错误: {ConnectionId}", connection.Id);
            }
        }
    }

    /// <summary>
    /// 执行维护任务
    /// </summary>
    protected override async Task PerformMaintenanceAsync()
    {
        // 执行基类的维护任务
        await base.PerformMaintenanceAsync();

        // 检查连接池健康状态
        var poolHealthTasks = new List<Task>();

        lock (_poolLock)
        {
            foreach (var pool in _associatedPools.Values)
            {
                poolHealthTasks.Add(CheckPoolHealthAsync(pool));
            }
        }

        if (poolHealthTasks.Count > 0)
        {
            await Task.WhenAll(poolHealthTasks);
        }
    }

    /// <summary>
    /// 检查连接池健康状态
    /// </summary>
    private async Task CheckPoolHealthAsync(IConnectionPool pool)
    {
        try
        {
            var healthResult = await pool.CheckHealthAsync();
            if (healthResult.OverallHealth == ConnectionHealth.Unhealthy)
            {
                _logger.LogWarning("连接池健康状态不佳: {PoolName}, 健康状态: {Health}",
                    pool.Name, healthResult.OverallHealth);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查连接池健康状态失败: {PoolName}", pool.Name);
        }
    }

    /// <summary>
    /// 获取连接池连接策略统计信息
    /// </summary>
    public PooledConnectionStatistics GetPooledStatistics()
    {
        var baseStats = GetStatistics();
        var pooledStats = new Dictionary<string, PooledStatistics>();
        var poolStatistics = new Dictionary<string, ConnectionPoolStatistics>();

        lock (_poolLock)
        {
            foreach (var kvp in _pooledInfos)
            {
                var pooledInfo = kvp.Value;
                pooledStats[kvp.Key] = new PooledStatistics
                {
                    ConnectionId = kvp.Key,
                    PoolName = pooledInfo.Pool.Name,
                    CreatedAt = pooledInfo.CreatedAt,
                    Lifetime = DateTime.UtcNow - pooledInfo.CreatedAt,
                    FailureCount = pooledInfo.FailureCount,
                    LastDisconnectedAt = pooledInfo.LastDisconnectedAt,
                    LastFailureAt = pooledInfo.LastFailureAt,
                    DisconnectReason = pooledInfo.DisconnectReason
                };
            }

            foreach (var kvp in _associatedPools)
            {
                try
                {
                    poolStatistics[kvp.Key] = kvp.Value.GetStatistics();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取连接池统计信息失败: {PoolName}", kvp.Key);
                }
            }
        }

        return new PooledConnectionStatistics
        {
            BaseStatistics = baseStats,
            PooledStatistics = pooledStats,
            PoolStatistics = poolStatistics,
            TotalPools = poolStatistics.Count,
            TotalPooledConnections = pooledStats.Count
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        _logger.LogInformation("关闭连接池连接策略");

        // 清理所有连接池信息
        lock (_poolLock)
        {
            _pooledInfos.Clear();

            // 注意：不在这里关闭连接池，因为连接池由工厂管理
            _associatedPools.Clear();
        }

        base.Dispose();
    }
}

/// <summary>
/// 连接池连接信息
/// </summary>
internal sealed class PooledConnectionInfo
{
    public IConnectionContext Connection { get; }
    public IConnectionPool Pool { get; }
    public IConnectionLease Lease { get; }
    public DateTime CreatedAt { get; }
    public DateTime? LastDisconnectedAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public string? DisconnectReason { get; set; }
    public Exception? LastException { get; set; }
    public int FailureCount { get; set; }

    public PooledConnectionInfo(IConnectionContext connection, IConnectionPool pool, IConnectionLease lease)
    {
        Connection = connection;
        Pool = pool;
        Lease = lease;
        CreatedAt = DateTime.UtcNow;
        FailureCount = 0;
    }
}

/// <summary>
/// 连接池连接策略统计信息
/// </summary>
public sealed class PooledConnectionStatistics
{
    /// <summary>
    /// 基础统计信息
    /// </summary>
    public LifecycleStrategyStatistics BaseStatistics { get; set; } = new();

    /// <summary>
    /// 连接池连接统计信息
    /// </summary>
    public Dictionary<string, PooledStatistics> PooledStatistics { get; set; } = new();

    /// <summary>
    /// 连接池统计信息
    /// </summary>
    public Dictionary<string, ConnectionPoolStatistics> PoolStatistics { get; set; } = new();

    /// <summary>
    /// 总连接池数
    /// </summary>
    public int TotalPools { get; set; }

    /// <summary>
    /// 总连接池连接数
    /// </summary>
    public int TotalPooledConnections { get; set; }
}

/// <summary>
/// 连接池连接统计信息
/// </summary>
public sealed class PooledStatistics
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 连接池名称
    /// </summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 存活时间
    /// </summary>
    public TimeSpan Lifetime { get; set; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 最后断开时间
    /// </summary>
    public DateTime? LastDisconnectedAt { get; set; }

    /// <summary>
    /// 最后失败时间
    /// </summary>
    public DateTime? LastFailureAt { get; set; }

    /// <summary>
    /// 断开原因
    /// </summary>
    public string? DisconnectReason { get; set; }
}
