using Microsoft.Extensions.Logging;

namespace PulseRPC.Client.Core.LifecycleStrategies;

/// <summary>
/// 会话连接策略 - 维持会话期间的连接，支持有限重连
/// </summary>
public sealed class SessionConnectionStrategy : ConnectionLifecycleStrategyBase
{
    private readonly Dictionary<string, SessionInfo> _sessionInfos = new();
    private readonly object _sessionLock = new();

    /// <summary>
    /// 策略名称
    /// </summary>
    public override string Name => "Session";

    /// <summary>
    /// 连接策略类型
    /// </summary>
    public override ConnectionStrategy Strategy => ConnectionStrategy.Session;

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
    public SessionConnectionStrategy(
        IConnectionManager connectionManager,
        LifecycleStrategyOptions? options = null,
        ILogger<SessionConnectionStrategy>? logger = null)
        : base(connectionManager, options, logger)
    {
        // 会话连接的默认配置更保守
        if (options == null)
        {
            _options.MaxReconnectAttempts = 3; // 会话连接重连次数较少
            _options.DisconnectOnIdle = true; // 会话连接空闲时断开
            _options.IdleTimeout = TimeSpan.FromMinutes(5); // 较短的空闲超时
        }

        _logger.LogDebug("会话连接策略已创建");
    }

    /// <summary>
    /// 初始化策略
    /// </summary>
    protected override async Task OnInitializeAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("初始化会话连接策略: {ConnectionId}", descriptor.Id);

        // 验证配置是否适合会话连接
        if (descriptor.Strategy != ConnectionStrategy.Session)
        {
            _logger.LogWarning("连接描述符策略不是 Session，但使用了会话连接策略: {ConnectionId}", descriptor.Id);
        }

        await base.OnInitializeAsync(descriptor, cancellationToken);
    }

    /// <summary>
    /// 管理连接生命周期
    /// </summary>
    protected override async Task OnManageConnectionAsync(IConnectionContext connection, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("开始管理会话连接: {ConnectionId}", connection.Id);

        // 为连接创建会话信息
        lock (_sessionLock)
        {
            if (!_sessionInfos.ContainsKey(connection.Id))
            {
                _sessionInfos[connection.Id] = new SessionInfo(connection);
            }
        }

        // 会话连接在断开时可以尝试有限次数的重连
        if ((connection.State == ExtendedConnectionState.Disconnected ||
             connection.State == ExtendedConnectionState.Failed) &&
            ShouldAttemptReconnect(connection.Id))
        {
            await TryReconnectAsync(connection);
        }

        await base.OnManageConnectionAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    protected override async Task OnConnectionDisconnectedInternalAsync(IConnectionContext connection, string reason, Exception? exception = null)
    {
        _logger.LogInformation("会话连接断开: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        lock (_sessionLock)
        {
            if (_sessionInfos.TryGetValue(connection.Id, out var sessionInfo))
            {
                sessionInfo.LastDisconnectedAt = DateTime.UtcNow;
                sessionInfo.DisconnectReason = reason;
            }
        }

        // 会话连接断开后有选择性地重连
        if (ShouldAttemptReconnect(connection.Id) && IsReconnectWorthwhile(reason))
        {
            await TryReconnectAsync(connection);
        }
        else
        {
            _logger.LogDebug("会话连接不进行重连: {ConnectionId}, 原因: {Reason}", connection.Id, reason);
        }

        await base.OnConnectionDisconnectedInternalAsync(connection, reason, exception);
    }

    /// <summary>
    /// 处理连接失败
    /// </summary>
    protected override async Task OnConnectionFailedInternalAsync(IConnectionContext connection, Exception exception)
    {
        _logger.LogWarning(exception, "会话连接失败: {ConnectionId}", connection.Id);

        lock (_sessionLock)
        {
            if (_sessionInfos.TryGetValue(connection.Id, out var sessionInfo))
            {
                sessionInfo.FailureCount++;
                sessionInfo.LastFailureAt = DateTime.UtcNow;
                sessionInfo.LastException = exception;
            }
        }

        // 会话连接失败后尝试重连（有限次数）
        if (ShouldAttemptReconnect(connection.Id))
        {
            await TryReconnectAsync(connection);
        }
        else
        {
            _logger.LogWarning("会话连接重连次数已达上限: {ConnectionId}", connection.Id);
        }

        await base.OnConnectionFailedInternalAsync(connection, exception);
    }

    /// <summary>
    /// 检查连接是否应该保持活跃
    /// </summary>
    protected override bool ShouldKeepAliveInternal(IConnectionContext connection, TimeSpan idleDuration)
    {
        // 会话连接在空闲超时后会断开
        if (_options.DisconnectOnIdle && idleDuration > _options.IdleTimeout)
        {
            _logger.LogDebug("会话连接空闲超时，将断开: {ConnectionId}, 空闲时间: {IdleDuration}",
                connection.Id, idleDuration);
            return false;
        }

        // 检查会话是否过期
        lock (_sessionLock)
        {
            if (_sessionInfos.TryGetValue(connection.Id, out var sessionInfo))
            {
                var sessionAge = DateTime.UtcNow - sessionInfo.CreatedAt;
                var maxSessionDuration = _options.MaxConnectionLifetime ?? TimeSpan.FromHours(2); // 默认会话最长2小时

                if (sessionAge > maxSessionDuration)
                {
                    _logger.LogDebug("会话连接超过最大会话时间，将断开: {ConnectionId}, 会话时长: {SessionAge}",
                        connection.Id, sessionAge);
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
        _logger.LogInformation("清理会话连接: {ConnectionId}, 原因: {Reason}", connection.Id, reason);

        // 清理会话信息
        lock (_sessionLock)
        {
            if (_sessionInfos.TryGetValue(connection.Id, out var sessionInfo))
            {
                _logger.LogDebug("会话连接统计 - ID: {ConnectionId}, 会话时长: {Duration}, 重连次数: {Reconnects}, 失败次数: {Failures}",
                    connection.Id,
                    DateTime.UtcNow - sessionInfo.CreatedAt,
                    sessionInfo.ReconnectAttempts,
                    sessionInfo.FailureCount);

                _sessionInfos.Remove(connection.Id);
            }
        }

        await base.OnCleanupConnectionAsync(connection, reason);
    }

    /// <summary>
    /// 检查是否应该尝试重连
    /// </summary>
    private bool ShouldAttemptReconnect(string connectionId)
    {
        lock (_sessionLock)
        {
            if (_sessionInfos.TryGetValue(connectionId, out var sessionInfo))
            {
                // 检查重连次数限制
                if (sessionInfo.ReconnectAttempts >= _options.MaxReconnectAttempts)
                {
                    return false;
                }

                // 检查是否已在重连中
                if (sessionInfo.IsReconnecting)
                {
                    return false;
                }

                // 检查会话是否过期
                var sessionAge = DateTime.UtcNow - sessionInfo.CreatedAt;
                var maxSessionDuration = _options.MaxConnectionLifetime ?? TimeSpan.FromHours(2);
                if (sessionAge > maxSessionDuration)
                {
                    return false;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查重连是否值得
    /// </summary>
    private bool IsReconnectWorthwhile(string reason)
    {
        // 某些断开原因不值得重连
        var unworthyReasons = new[]
        {
            "手动断开",
            "会话过期",
            "认证失败",
            "权限不足",
            "策略关闭"
        };

        return !unworthyReasons.Any(unworthyReason =>
            reason.Contains(unworthyReason, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 尝试重连
    /// </summary>
    private async Task TryReconnectAsync(IConnectionContext connection)
    {
        SessionInfo sessionInfo;

        lock (_sessionLock)
        {
            if (!_sessionInfos.TryGetValue(connection.Id, out sessionInfo!))
            {
                _logger.LogWarning("尝试重连时未找到会话信息: {ConnectionId}", connection.Id);
                return;
            }

            if (sessionInfo.IsReconnecting)
            {
                _logger.LogDebug("会话连接已在重连中，跳过: {ConnectionId}", connection.Id);
                return;
            }

            if (sessionInfo.ReconnectAttempts >= _options.MaxReconnectAttempts)
            {
                _logger.LogWarning("会话连接重连次数已达上限: {ConnectionId}, 尝试次数: {Attempts}",
                    connection.Id, sessionInfo.ReconnectAttempts);
                return;
            }

            sessionInfo.IsReconnecting = true;
        }

        try
        {
            // 计算重连延迟
            var delay = GetReconnectDelay(sessionInfo.ReconnectAttempts + 1);
            _logger.LogInformation("会话连接准备重连: {ConnectionId}, 尝试次数: {Attempts}, 延迟: {Delay}ms",
                connection.Id, sessionInfo.ReconnectAttempts + 1, delay.TotalMilliseconds);

            // 等待重连延迟
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            // 执行重连
            sessionInfo.ReconnectAttempts++;
            _statistics.TotalReconnections++;

            try
            {
                await _connectionManager.DisconnectAsync(connection.Id);
                var newConnection = await _connectionManager.ConnectAsync(connection.Descriptor);

                _statistics.SuccessfulReconnections++;
                sessionInfo.LastReconnectAt = DateTime.UtcNow;

                _logger.LogInformation("会话连接重连成功: {ConnectionId}, 尝试次数: {Attempts}",
                    connection.Id, sessionInfo.ReconnectAttempts);
            }
            catch (Exception ex)
            {
                _statistics.FailedReconnections++;
                sessionInfo.LastFailureAt = DateTime.UtcNow;
                sessionInfo.LastException = ex;

                _logger.LogError(ex, "会话连接重连失败: {ConnectionId}, 尝试次数: {Attempts}",
                    connection.Id, sessionInfo.ReconnectAttempts);

                // 如果重连失败且未达到最大次数，标记为可以再次尝试
                if (sessionInfo.ReconnectAttempts < _options.MaxReconnectAttempts)
                {
                    _logger.LogDebug("会话连接将在稍后再次尝试重连: {ConnectionId}", connection.Id);
                }
            }
        }
        finally
        {
            lock (_sessionLock)
            {
                sessionInfo.IsReconnecting = false;
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

        // 检查过期的会话
        var expiredSessions = new List<IConnectionContext>();
        var maxSessionDuration = _options.MaxConnectionLifetime ?? TimeSpan.FromHours(2);
        var now = DateTime.UtcNow;

        lock (_sessionLock)
        {
            foreach (var kvp in _sessionInfos)
            {
                var sessionInfo = kvp.Value;
                var sessionAge = now - sessionInfo.CreatedAt;

                if (sessionAge > maxSessionDuration)
                {
                    expiredSessions.Add(sessionInfo.Connection);
                }
            }
        }

        // 清理过期的会话
        foreach (var connection in expiredSessions)
        {
            try
            {
                _logger.LogInformation("清理过期会话: {ConnectionId}", connection.Id);
                await CleanupConnectionAsync(connection, "会话过期");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期会话失败: {ConnectionId}", connection.Id);
            }
        }
    }

    /// <summary>
    /// 获取会话连接策略统计信息
    /// </summary>
    public SessionConnectionStatistics GetSessionStatistics()
    {
        var baseStats = GetStatistics();
        var sessionStats = new Dictionary<string, SessionStatistics>();

        lock (_sessionLock)
        {
            foreach (var kvp in _sessionInfos)
            {
                var sessionInfo = kvp.Value;
                sessionStats[kvp.Key] = new SessionStatistics
                {
                    ConnectionId = kvp.Key,
                    CreatedAt = sessionInfo.CreatedAt,
                    SessionDuration = DateTime.UtcNow - sessionInfo.CreatedAt,
                    ReconnectAttempts = sessionInfo.ReconnectAttempts,
                    FailureCount = sessionInfo.FailureCount,
                    IsReconnecting = sessionInfo.IsReconnecting,
                    LastReconnectAt = sessionInfo.LastReconnectAt,
                    LastFailureAt = sessionInfo.LastFailureAt,
                    DisconnectReason = sessionInfo.DisconnectReason
                };
            }
        }

        return new SessionConnectionStatistics
        {
            BaseStatistics = baseStats,
            SessionStatistics = sessionStats,
            ActiveSessions = sessionStats.Count,
            AverageSessionDuration = sessionStats.Values.Any()
                ? TimeSpan.FromMilliseconds(sessionStats.Values.Average(s => s.SessionDuration.TotalMilliseconds))
                : TimeSpan.Zero
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        _logger.LogInformation("关闭会话连接策略");

        lock (_sessionLock)
        {
            _sessionInfos.Clear();
        }

        base.Dispose();
    }
}

/// <summary>
/// 会话信息
/// </summary>
internal sealed class SessionInfo
{
    public IConnectionContext Connection { get; }
    public DateTime CreatedAt { get; }
    public DateTime? LastReconnectAt { get; set; }
    public DateTime? LastDisconnectedAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public string? DisconnectReason { get; set; }
    public Exception? LastException { get; set; }
    public int ReconnectAttempts { get; set; }
    public int FailureCount { get; set; }
    public bool IsReconnecting { get; set; }

    public SessionInfo(IConnectionContext connection)
    {
        Connection = connection;
        CreatedAt = DateTime.UtcNow;
        ReconnectAttempts = 0;
        FailureCount = 0;
        IsReconnecting = false;
    }
}

/// <summary>
/// 会话连接策略统计信息
/// </summary>
public sealed class SessionConnectionStatistics
{
    /// <summary>
    /// 基础统计信息
    /// </summary>
    public LifecycleStrategyStatistics BaseStatistics { get; set; } = new();

    /// <summary>
    /// 会话统计信息
    /// </summary>
    public Dictionary<string, SessionStatistics> SessionStatistics { get; set; } = new();

    /// <summary>
    /// 活跃会话数
    /// </summary>
    public int ActiveSessions { get; set; }

    /// <summary>
    /// 平均会话时长
    /// </summary>
    public TimeSpan AverageSessionDuration { get; set; }
}

/// <summary>
/// 会话统计信息
/// </summary>
public sealed class SessionStatistics
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
    /// 会话时长
    /// </summary>
    public TimeSpan SessionDuration { get; set; }

    /// <summary>
    /// 重连尝试次数
    /// </summary>
    public int ReconnectAttempts { get; set; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public int FailureCount { get; set; }

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
    /// 断开原因
    /// </summary>
    public string? DisconnectReason { get; set; }
}