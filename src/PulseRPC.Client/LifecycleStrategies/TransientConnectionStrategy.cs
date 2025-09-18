using Microsoft.Extensions.Logging;

namespace PulseRPC.Client.Core.LifecycleStrategies;

/// <summary>
/// 瞬态连接策略 - 短期连接，用完即断开，不重连
/// </summary>
public sealed class TransientConnectionStrategy : ConnectionLifecycleStrategyBase
{
    private readonly Dictionary<string, TransientInfo> _transientInfos = new();
    private readonly SemaphoreSlim _transientLock = new(1, 1);

    /// <summary>
    /// 策略名称
    /// </summary>
    public override string Name => "Transient";

    /// <summary>
    /// 连接策略类型
    /// </summary>
    public override ConnectionStrategy Strategy => ConnectionStrategy.Transient;

    /// <summary>
    /// 是否支持自动重连
    /// </summary>
    public override bool SupportsAutoReconnect => false;

    /// <summary>
    /// 是否支持连接池
    /// </summary>
    public override bool SupportsPooling => false;

    /// <summary>
    /// 构造函数
    /// </summary>
    public TransientConnectionStrategy(
        IConnectionManager connectionManager,
        LifecycleStrategyOptions? options = null,
        ILogger<TransientConnectionStrategy>? logger = null)
        : base(connectionManager, options, logger)
    {
        // 瞬态连接的默认配置
        if (options == null)
        {
            _options.MaxReconnectAttempts = 0; // 不重连
            _options.DisconnectOnIdle = true; // 空闲立即断开
            _options.IdleTimeout = TimeSpan.FromSeconds(30); // 很短的空闲超时
            _options.EnableHeartbeat = false; // 不需要心跳
            _options.MaxConnectionLifetime = TimeSpan.FromMinutes(5); // 最大存活5分钟
        }

        _logger.LogDebug("瞬态连接策略已创建");
    }

    /// <summary>
    /// 初始化策略
    /// </summary>
    protected override async Task OnInitializeAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("初始化瞬态连接策略: {ConnectionId}", descriptor.Id);

        // 验证配置是否适合瞬态连接
        if (descriptor.Strategy != ConnectionStrategy.Transient)
        {
            _logger.LogWarning("连接描述符策略不是 Transient，但使用了瞬态连接策略: {ConnectionId}", descriptor.Id);
        }

        if (descriptor.AutoReconnect)
        {
            _logger.LogWarning("瞬态连接不支持自动重连，已忽略 AutoReconnect 设置: {ConnectionId}", descriptor.Id);
        }

        await base.OnInitializeAsync(descriptor, cancellationToken);
    }

    /// <summary>
    /// 创建连接
    /// </summary>
    public override async Task<IConnectionContext> CreateConnectionAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("创建瞬态连接: {ConnectionId}", descriptor.Id);

        var connection = await base.CreateConnectionAsync(descriptor, cancellationToken);

        // 为瞬态连接创建信息记录
        lock (_transientLock)
        {
            _transientInfos[connection.Id] = new TransientInfo(connection);
        }

        _logger.LogDebug("瞬态连接已创建: {ConnectionId}", connection.Id);
        return connection;
    }

    /// <summary>
    /// 管理连接生命周期
    /// </summary>
    protected override async Task OnManageConnectionAsync(IConnectionContext connection, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("管理瞬态连接: {ConnectionId}", connection.Id);

        // 瞬态连接不进行重连，断开就清理
        if (connection.State == ExtendedConnectionState.Disconnected ||
            connection.State == ExtendedConnectionState.Failed)
        {
            _logger.LogDebug("瞬态连接已断开，准备清理: {ConnectionId}", connection.Id);
            await CleanupConnectionAsync(connection, "瞬态连接断开");
            return;
        }

        // 检查连接是否超过最大存活时间
        await _transientLock.WaitAsync();
        try
        {
            if (_transientInfos.TryGetValue(connection.Id, out var transientInfo))
            {
                var lifetime = DateTime.UtcNow - transientInfo.CreatedAt;
                if (_options.MaxConnectionLifetime.HasValue && lifetime > _options.MaxConnectionLifetime.Value)
                {
                    _logger.LogDebug("瞬态连接超过最大存活时间，准备清理: {ConnectionId}, 存活时间: {Lifetime}",
                        connection.Id, lifetime);
                    await CleanupConnectionAsync(connection, "超过最大存活时间");
                    return;
                }
            }
        }
        finally
        {
            _transientLock.Release();
        }

        await base.OnManageConnectionAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    protected override async Task OnConnectionDisconnectedInternalAsync(IConnectionContext connection, string reason, Exception? exception = null)
    {
        _logger.LogInformation("瞬态连接断开: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        lock (_transientLock)
        {
            if (_transientInfos.TryGetValue(connection.Id, out var transientInfo))
            {
                transientInfo.DisconnectedAt = DateTime.UtcNow;
                transientInfo.DisconnectReason = reason;
                transientInfo.IsCompleted = true;
            }
        }

        // 瞬态连接断开后不重连，直接清理
        _logger.LogDebug("瞬态连接断开，不进行重连: {ConnectionId}", connection.Id);

        await base.OnConnectionDisconnectedInternalAsync(connection, reason, exception);

        // 安排清理任务
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1)); // 短暂延迟确保状态稳定
                await CleanupConnectionAsync(connection, "瞬态连接断开清理");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "瞬态连接清理失败: {ConnectionId}", connection.Id);
            }
        });
    }

    /// <summary>
    /// 处理连接失败
    /// </summary>
    protected override async Task OnConnectionFailedInternalAsync(IConnectionContext connection, Exception exception)
    {
        _logger.LogWarning(exception, "瞬态连接失败: {ConnectionId}", connection.Id);

        lock (_transientLock)
        {
            if (_transientInfos.TryGetValue(connection.Id, out var transientInfo))
            {
                transientInfo.FailedAt = DateTime.UtcNow;
                transientInfo.FailureException = exception;
                transientInfo.IsCompleted = true;
            }
        }

        // 瞬态连接失败后不重连，直接清理
        _logger.LogDebug("瞬态连接失败，不进行重连: {ConnectionId}", connection.Id);

        await base.OnConnectionFailedInternalAsync(connection, exception);

        // 安排清理任务
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1)); // 短暂延迟确保状态稳定
                await CleanupConnectionAsync(connection, "瞬态连接失败清理");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "瞬态连接清理失败: {ConnectionId}", connection.Id);
            }
        });
    }

    /// <summary>
    /// 检查连接是否应该保持活跃
    /// </summary>
    protected override bool ShouldKeepAliveInternal(IConnectionContext connection, TimeSpan idleDuration)
    {
        // 瞬态连接空闲后立即断开
        if (idleDuration > _options.IdleTimeout)
        {
            _logger.LogDebug("瞬态连接空闲超时，将断开: {ConnectionId}, 空闲时间: {IdleDuration}",
                connection.Id, idleDuration);
            return false;
        }

        // 检查是否超过最大存活时间
        lock (_transientLock)
        {
            if (_transientInfos.TryGetValue(connection.Id, out var transientInfo))
            {
                var lifetime = DateTime.UtcNow - transientInfo.CreatedAt;
                if (_options.MaxConnectionLifetime.HasValue && lifetime > _options.MaxConnectionLifetime.Value)
                {
                    _logger.LogDebug("瞬态连接超过最大存活时间，将断开: {ConnectionId}, 存活时间: {Lifetime}",
                        connection.Id, lifetime);
                    return false;
                }

                // 检查连接是否已标记为完成
                if (transientInfo.IsCompleted)
                {
                    _logger.LogDebug("瞬态连接已完成，将断开: {ConnectionId}", connection.Id);
                    return false;
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
        _logger.LogInformation("清理瞬态连接: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        // 清理瞬态信息
        lock (_transientLock)
        {
            if (_transientInfos.TryGetValue(connection.Id, out var transientInfo))
            {
                _logger.LogDebug("瞬态连接统计 - ID: {ConnectionId}, 存活时长: {Lifetime}, 状态: {Status}",
                    connection.Id,
                    DateTime.UtcNow - transientInfo.CreatedAt,
                    transientInfo.IsCompleted ? "已完成" : "提前清理");

                _transientInfos.Remove(connection.Id);
            }
        }

        await base.OnCleanupConnectionAsync(connection, reason);
    }

    /// <summary>
    /// 标记连接为完成状态
    /// </summary>
    public void MarkConnectionCompleted(string connectionId, string reason = "任务完成")
    {
        lock (_transientLock)
        {
            if (_transientInfos.TryGetValue(connectionId, out var transientInfo))
            {
                transientInfo.IsCompleted = true;
                transientInfo.CompletionReason = reason;
                transientInfo.CompletedAt = DateTime.UtcNow;

                _logger.LogDebug("瞬态连接已标记为完成: {ConnectionId}, 原因: {Reason}", connectionId, reason);

                // 安排清理任务
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5)); // 给一些时间完成最后的操作
                        if (_managedConnections.TryGetValue(connectionId, out var lifecycleInfo))
                        {
                            await CleanupConnectionAsync(lifecycleInfo.Connection, reason);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "完成后清理瞬态连接失败: {ConnectionId}", connectionId);
                    }
                });
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

        // 清理已完成或过期的瞬态连接
        var connectionsToCleanup = new List<IConnectionContext>();
        var now = DateTime.UtcNow;

        lock (_transientLock)
        {
            foreach (var kvp in _transientInfos)
            {
                var transientInfo = kvp.Value;

                // 检查是否已完成
                if (transientInfo.IsCompleted)
                {
                    connectionsToCleanup.Add(transientInfo.Connection);
                    continue;
                }

                // 检查是否超过最大存活时间
                if (_options.MaxConnectionLifetime.HasValue)
                {
                    var lifetime = now - transientInfo.CreatedAt;
                    if (lifetime > _options.MaxConnectionLifetime.Value)
                    {
                        connectionsToCleanup.Add(transientInfo.Connection);
                    }
                }
            }
        }

        // 清理连接
        foreach (var connection in connectionsToCleanup)
        {
            try
            {
                await CleanupConnectionAsync(connection, "维护清理");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "维护清理瞬态连接失败: {ConnectionId}", connection.Id);
            }
        }
    }

    /// <summary>
    /// 获取瞬态连接策略统计信息
    /// </summary>
    public TransientConnectionStatistics GetTransientStatistics()
    {
        var baseStats = GetStatistics();
        var transientStats = new Dictionary<string, TransientStatistics>();

        lock (_transientLock)
        {
            foreach (var kvp in _transientInfos)
            {
                var transientInfo = kvp.Value;
                transientStats[kvp.Key] = new TransientStatistics
                {
                    ConnectionId = kvp.Key,
                    CreatedAt = transientInfo.CreatedAt,
                    Lifetime = DateTime.UtcNow - transientInfo.CreatedAt,
                    IsCompleted = transientInfo.IsCompleted,
                    CompletionReason = transientInfo.CompletionReason,
                    CompletedAt = transientInfo.CompletedAt,
                    DisconnectedAt = transientInfo.DisconnectedAt,
                    FailedAt = transientInfo.FailedAt,
                    DisconnectReason = transientInfo.DisconnectReason
                };
            }
        }

        return new TransientConnectionStatistics
        {
            BaseStatistics = baseStats,
            TransientStatistics = transientStats,
            ActiveConnections = transientStats.Count,
            CompletedConnections = transientStats.Values.Count(s => s.IsCompleted),
            AverageLifetime = transientStats.Values.Any()
                ? TimeSpan.FromMilliseconds(transientStats.Values.Average(s => s.Lifetime.TotalMilliseconds))
                : TimeSpan.Zero
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        _logger.LogInformation("关闭瞬态连接策略");

        lock (_transientLock)
        {
            _transientInfos.Clear();
        }

        base.Dispose();
    }
}

/// <summary>
/// 瞬态连接信息
/// </summary>
internal sealed class TransientInfo
{
    public IConnectionContext Connection { get; }
    public DateTime CreatedAt { get; }
    public bool IsCompleted { get; set; }
    public string? CompletionReason { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? DisconnectReason { get; set; }
    public Exception? FailureException { get; set; }

    public TransientInfo(IConnectionContext connection)
    {
        Connection = connection;
        CreatedAt = DateTime.UtcNow;
        IsCompleted = false;
    }
}

/// <summary>
/// 瞬态连接策略统计信息
/// </summary>
public sealed class TransientConnectionStatistics
{
    /// <summary>
    /// 基础统计信息
    /// </summary>
    public LifecycleStrategyStatistics BaseStatistics { get; set; } = new();

    /// <summary>
    /// 瞬态连接统计信息
    /// </summary>
    public Dictionary<string, TransientStatistics> TransientStatistics { get; set; } = new();

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 已完成连接数
    /// </summary>
    public int CompletedConnections { get; set; }

    /// <summary>
    /// 平均连接存活时间
    /// </summary>
    public TimeSpan AverageLifetime { get; set; }
}

/// <summary>
/// 瞬态连接统计信息
/// </summary>
public sealed class TransientStatistics
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 存活时间
    /// </summary>
    public TimeSpan Lifetime { get; set; }

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 完成原因
    /// </summary>
    public string? CompletionReason { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 断开时间
    /// </summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>
    /// 失败时间
    /// </summary>
    public DateTime? FailedAt { get; set; }

    /// <summary>
    /// 断开原因
    /// </summary>
    public string? DisconnectReason { get; set; }
}
