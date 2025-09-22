using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;

namespace PulseRPC.Client.LifecycleStrategies;

/// <summary>
/// 持久化连接策略 - 维持长期连接，自动重连
/// </summary>
public sealed class PersistentConnectionStrategy : ConnectionLifecycleStrategyBase
{
    private readonly Dictionary<string, ReconnectInfo> _reconnectInfos = new();
    private readonly object _reconnectLock = new();

    /// <summary>
    /// 策略名称
    /// </summary>
    public override string Name => "Persistent";

    /// <summary>
    /// 连接策略类型
    /// </summary>
    public override ConnectionStrategy Strategy => ConnectionStrategy.Persistent;

    /// <summary>
    /// 是否支持自动重连
    /// </summary>
    public override bool SupportsAutoReconnect => true;

    /// <summary>
    /// 是否支持连接池
    /// </summary>
    public override bool SupportsPooling => false;

    /// <summary>
    /// 构造函数
    /// </summary>
    public PersistentConnectionStrategy(
        IConnectionManager connectionManager,
        LifecycleStrategyOptions? options = null,
        ILogger<PersistentConnectionStrategy>? logger = null)
        : base(connectionManager, options, logger)
    {
        _logger.LogDebug("持久化连接策略已创建");
    }

    /// <summary>
    /// 初始化策略
    /// </summary>
    protected override async Task OnInitializeAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("初始化持久化连接策略: {ConnectionId}", descriptor.Id);

        // 验证配置是否适合持久化连接
        if (descriptor.Strategy != ConnectionStrategy.Persistent)
        {
            _logger.LogWarning("连接描述符策略不是 Persistent，但使用了持久化连接策略: {ConnectionId}", descriptor.Id);
        }

        await base.OnInitializeAsync(descriptor, cancellationToken);
    }

    /// <summary>
    /// 管理连接生命周期
    /// </summary>
    protected override async Task OnManageConnectionAsync(IClientChannel connection, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("开始管理持久化连接: {ConnectionId}", connection.Id);

        // 持久化连接需要确保始终保持连接状态
        if (connection.State == ExtendedConnectionState.Disconnected ||
            connection.State == ExtendedConnectionState.Failed)
        {
            await TryReconnectAsync(connection);
        }

        await base.OnManageConnectionAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    protected override async Task OnConnectionDisconnectedInternalAsync(IClientChannel connection, string reason, Exception? exception = null)
    {
        _logger.LogWarning("持久化连接断开，准备重连: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        // 持久化连接断开后立即尝试重连
        await TryReconnectAsync(connection);

        await base.OnConnectionDisconnectedInternalAsync(connection, reason, exception);
    }

    /// <summary>
    /// 处理连接失败
    /// </summary>
    protected override async Task OnConnectionFailedInternalAsync(IClientChannel connection, Exception exception)
    {
        _logger.LogError(exception, "持久化连接失败，准备重连: {ConnectionId}", connection.Id);

        // 持久化连接失败后尝试重连
        await TryReconnectAsync(connection);

        await base.OnConnectionFailedInternalAsync(connection, exception);
    }

    /// <summary>
    /// 检查连接是否应该保持活跃
    /// </summary>
    protected override bool ShouldKeepAliveInternal(IClientChannel connection, TimeSpan idleDuration)
    {
        // 持久化连接总是保持活跃，不因为空闲而断开
        return true;
    }

    /// <summary>
    /// 清理连接
    /// </summary>
    protected override async Task OnCleanupConnectionAsync(IClientChannel connection, string reason)
    {
        _logger.LogInformation("清理持久化连接: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        // 清理重连信息
        lock (_reconnectLock)
        {
            _reconnectInfos.Remove(connection.Id);
        }

        await base.OnCleanupConnectionAsync(connection, reason);
    }

    /// <summary>
    /// 尝试重连
    /// </summary>
    private async Task TryReconnectAsync(IClientChannel connection)
    {
        ReconnectInfo reconnectInfo;

        lock (_reconnectLock)
        {
            if (!_reconnectInfos.TryGetValue(connection.Id, out reconnectInfo!))
            {
                reconnectInfo = new ReconnectInfo(connection);
                _reconnectInfos[connection.Id] = reconnectInfo;
            }

            // 检查是否已经在重连中
            if (reconnectInfo.IsReconnecting)
            {
                _logger.LogDebug("连接已在重连中，跳过: {ConnectionId}", connection.Id);
                return;
            }

            // 检查重连次数限制
            if (reconnectInfo.Attempts >= _options.MaxReconnectAttempts)
            {
                _logger.LogError("持久化连接重连次数已达上限: {ConnectionId}, 尝试次数: {Attempts}",
                    connection.Id, reconnectInfo.Attempts);
                return;
            }

            reconnectInfo.IsReconnecting = true;
        }

        try
        {
            // 计算重连延迟
            var delay = GetReconnectDelay(reconnectInfo.Attempts + 1);
            _logger.LogInformation("持久化连接准备重连: {ConnectionId}, 尝试次数: {Attempts}, 延迟: {Delay}ms",
                connection.Id, reconnectInfo.Attempts + 1, delay.TotalMilliseconds);

            // 等待重连延迟
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            // 执行重连
            reconnectInfo.Attempts++;
            _statistics.TotalReconnections++;

            try
            {
                // 通过连接管理器重新连接
                await _connectionManager.DisconnectAsync(connection.Id);
                var newConnection = await _connectionManager.ConnectAsync(connection.Descriptor);

                // 更新连接上下文（这里假设连接管理器会更新现有的上下文）
                _statistics.SuccessfulReconnections++;
                reconnectInfo.LastReconnectAt = DateTime.UtcNow;
                reconnectInfo.Attempts = 0; // 重连成功，重置计数器

                _logger.LogInformation("持久化连接重连成功: {ConnectionId}", connection.Id);
            }
            catch (Exception ex)
            {
                _statistics.FailedReconnections++;
                reconnectInfo.LastFailureAt = DateTime.UtcNow;
                reconnectInfo.LastException = ex;

                _logger.LogError(ex, "持久化连接重连失败: {ConnectionId}, 尝试次数: {Attempts}",
                    connection.Id, reconnectInfo.Attempts);

                // 如果未达到最大重连次数，安排下次重连
                if (reconnectInfo.Attempts < _options.MaxReconnectAttempts)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5)); // 短暂延迟后再次尝试
                        lock (_reconnectLock)
                        {
                            reconnectInfo.IsReconnecting = false;
                        }
                        await TryReconnectAsync(connection);
                    });
                }
                else
                {
                    _logger.LogError("持久化连接重连彻底失败: {ConnectionId}, 已达最大重连次数", connection.Id);
                }
            }
        }
        finally
        {
            lock (_reconnectLock)
            {
                reconnectInfo.IsReconnecting = false;
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

        // 检查需要重连的连接
        var connectionsToReconnect = new List<IClientChannel>();

        foreach (var kvp in _managedConnections)
        {
            var connection = kvp.Value.Connection;

            // 检查连接状态，如果不是正常状态且不在重连中，则尝试重连
            if ((connection.State == ExtendedConnectionState.Disconnected ||
                 connection.State == ExtendedConnectionState.Failed) &&
                !IsReconnecting(connection.Id))
            {
                connectionsToReconnect.Add(connection);
            }
        }

        // 尝试重连
        foreach (var connection in connectionsToReconnect)
        {
            try
            {
                await TryReconnectAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "维护任务中重连失败: {ConnectionId}", connection.Id);
            }
        }
    }

    /// <summary>
    /// 检查连接是否正在重连
    /// </summary>
    private bool IsReconnecting(string connectionId)
    {
        lock (_reconnectLock)
        {
            return _reconnectInfos.TryGetValue(connectionId, out var info) && info.IsReconnecting;
        }
    }

    /// <summary>
    /// 获取持久化连接策略统计信息
    /// </summary>
    public PersistentConnectionStatistics GetPersistentStatistics()
    {
        var baseStats = GetStatistics();
        var reconnectStats = new Dictionary<string, ReconnectStatistics>();

        lock (_reconnectLock)
        {
            foreach (var kvp in _reconnectInfos)
            {
                reconnectStats[kvp.Key] = new ReconnectStatistics
                {
                    ConnectionId = kvp.Key,
                    TotalAttempts = kvp.Value.Attempts,
                    IsReconnecting = kvp.Value.IsReconnecting,
                    LastReconnectAt = kvp.Value.LastReconnectAt,
                    LastFailureAt = kvp.Value.LastFailureAt,
                    LastException = kvp.Value.LastException?.Message
                };
            }
        }

        return new PersistentConnectionStatistics
        {
            BaseStatistics = baseStats,
            ReconnectStatistics = reconnectStats
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        _logger.LogInformation("关闭持久化连接策略");

        lock (_reconnectLock)
        {
            _reconnectInfos.Clear();
        }

        base.Dispose();
    }
}

/// <summary>
/// 重连信息
/// </summary>
internal sealed class ReconnectInfo
{
    public IClientChannel Connection { get; }
    public int Attempts { get; set; }
    public bool IsReconnecting { get; set; }
    public DateTime? LastReconnectAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public Exception? LastException { get; set; }

    public ReconnectInfo(IClientChannel connection)
    {
        Connection = connection;
        Attempts = 0;
        IsReconnecting = false;
    }
}

/// <summary>
/// 持久化连接策略统计信息
/// </summary>
public sealed class PersistentConnectionStatistics
{
    /// <summary>
    /// 基础统计信息
    /// </summary>
    public LifecycleStrategyStatistics BaseStatistics { get; set; } = new();

    /// <summary>
    /// 重连统计信息
    /// </summary>
    public Dictionary<string, ReconnectStatistics> ReconnectStatistics { get; set; } = new();
}

/// <summary>
/// 重连统计信息
/// </summary>
public sealed class ReconnectStatistics
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 总尝试次数
    /// </summary>
    public int TotalAttempts { get; set; }

    /// <summary>
    /// 是否正在重连
    /// </summary>
    public bool IsReconnecting { get; set; }

    /// <summary>
    /// 最后重连时间
    /// </summary>
    public DateTime? LastReconnectAt { get; set; }

    /// <summary>
    /// 最后失败时间
    /// </summary>
    public DateTime? LastFailureAt { get; set; }

    /// <summary>
    /// 最后异常信息
    /// </summary>
    public string? LastException { get; set; }
}
