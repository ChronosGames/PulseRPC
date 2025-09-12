using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Sessions;

/// <summary>
/// 服务端会话管理器实现 - 三层抽象架构增强版
/// </summary>
public class ServerSessionManager : IServerSessionManager, IClientSessionManager
{
    private readonly ConcurrentDictionary<string, IClientSession> _sessions;
    private readonly ConcurrentDictionary<string, string> _connectionToSessionMap; // ConnectionId -> SessionId
    private readonly ILogger<ServerSessionManager> _logger;
    private readonly Timer _healthCheckTimer;
    private readonly Timer _cleanupTimer;
    private volatile bool _disposed;

    // 统计信息
    private long _totalSessionsCreated;
    private long _totalSessionsRemoved;
    private long _totalHubInvocations;

    /// <summary>
    /// 会话超时时间（毫秒）
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 300000; // 5分钟

    /// <summary>
    /// 当前会话数
    /// </summary>
    public int SessionCount => _sessions.Count;

    /// <summary>
    /// 所有会话ID
    /// </summary>
    public IEnumerable<string> SessionIds => _sessions.Keys.ToList();

    /// <summary>
    /// 会话创建事件
    /// </summary>
    public event EventHandler<SessionEventArgs>? SessionCreated;

    /// <summary>
    /// 会话移除事件
    /// </summary>
    public event EventHandler<SessionEventArgs>? SessionRemoved;

    /// <summary>
    /// 会话认证事件
    /// </summary>
    public event EventHandler<SessionAuthenticatedEventArgs>? SessionAuthenticated;

    public ServerSessionManager(ILogger<ServerSessionManager> logger)
    {
        _sessions = new ConcurrentDictionary<string, IClientSession>();
        _connectionToSessionMap = new ConcurrentDictionary<string, string>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 启动健康检查定时器，每30秒检查一次
        _healthCheckTimer = new Timer(PerformHealthChecks, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // 启动清理定时器，每60秒清理一次
        _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _logger.LogInformation("服务端会话管理器已启动");
    }

    /// <summary>
    /// 从服务端通道创建新的客户端会话
    /// </summary>
    /// <param name="serverChannel">服务端通道</param>
    /// <param name="descriptor">会话描述符</param>
    /// <returns>创建的客户端会话</returns>
    public IClientSession CreateSession(IServerChannel serverChannel, ClientSessionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(serverChannel);
        ArgumentNullException.ThrowIfNull(descriptor);
        ObjectDisposedException.ThrowIf(_disposed, nameof(ServerSessionManager));

        // 创建会话适配器
        var sessionLogger = _logger; // 可以创建专用logger，这里简化处理
        var session = new ClientSessionAdapter(serverChannel, descriptor, this, sessionLogger);

        // 注册事件处理
        session.AuthenticationChanged += OnSessionAuthenticated;
        session.HealthChanged += OnSessionHealthChanged;

        // 添加到管理字典
        if (_sessions.TryAdd(descriptor.Id, session))
        {
            _connectionToSessionMap.TryAdd(serverChannel.ConnectionId, descriptor.Id);
            Interlocked.Increment(ref _totalSessionsCreated);

            _logger.LogInformation("已创建客户端会话: SessionId={SessionId}, ConnectionId={ConnectionId}, 总数: {TotalCount}",
                descriptor.Id, serverChannel.ConnectionId, _sessions.Count);

            SessionCreated?.Invoke(this, new SessionEventArgs(session));
            return session;
        }
        else
        {
            // 如果添加失败，说明会话ID冲突，释放新创建的会话
            session.Dispose();
            throw new InvalidOperationException($"会话ID冲突: {descriptor.Id}");
        }
    }

    /// <summary>
    /// 获取指定的客户端会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>客户端会话，如果不存在则返回null</returns>
    public IClientSession? GetSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// 根据连接ID获取客户端会话
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>客户端会话，如果不存在则返回null</returns>
    public IClientSession? GetSessionByConnectionId(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return null;

        if (_connectionToSessionMap.TryGetValue(connectionId, out var sessionId))
        {
            return GetSession(sessionId);
        }

        return null;
    }

    /// <summary>
    /// 移除指定的客户端会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;

        if (!_sessions.TryRemove(sessionId, out var session)) return false;

        Interlocked.Increment(ref _totalSessionsRemoved);

        // 移除连接映射
        _connectionToSessionMap.TryRemove(session.ConnectionId, out _);

        _logger.LogInformation("已移除客户端会话: SessionId={SessionId}, ConnectionId={ConnectionId}, 剩余: {RemainingCount}",
            sessionId, session.ConnectionId, _sessions.Count);

        // 取消订阅事件
        session.AuthenticationChanged -= OnSessionAuthenticated;
        session.HealthChanged -= OnSessionHealthChanged;

        // 触发移除事件
        SessionRemoved?.Invoke(this, new SessionEventArgs(session));

        // 释放会话资源
        session.Dispose();

        return true;
    }

    /// <summary>
    /// 获取所有客户端会话
    /// </summary>
    /// <returns>所有客户端会话的集合</returns>
    public IEnumerable<IClientSession> GetAllSessions()
    {
        return _sessions.Values.ToList();
    }

    /// <summary>
    /// 获取所有已认证的客户端会话
    /// </summary>
    /// <returns>已认证的客户端会话集合</returns>
    public IEnumerable<IClientSession> GetAuthenticatedSessions()
    {
        return _sessions.Values.Where(s => s.IsAuthenticated).ToList();
    }

    /// <summary>
    /// 获取所有可用的客户端会话（已连接且健康）
    /// </summary>
    /// <returns>可用的客户端会话集合</returns>
    public IEnumerable<IClientSession> GetAvailableSessions()
    {
        return _sessions.Values.Where(s => s.IsAvailable).ToList();
    }

    /// <summary>
    /// 根据认证用户名获取客户端会话
    /// </summary>
    /// <param name="username">用户名</param>
    /// <returns>用户的客户端会话集合</returns>
    public IEnumerable<IClientSession> GetSessionsByUser(string username)
    {
        if (string.IsNullOrEmpty(username)) return Enumerable.Empty<IClientSession>();

        return _sessions.Values
            .Where(s => s.IsAuthenticated &&
                        string.Equals(s.AuthenticationContext?.Name, username, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 根据组名获取客户端会话
    /// </summary>
    /// <param name="groupName">组名</param>
    /// <returns>该组的客户端会话集合</returns>
    public IEnumerable<IClientSession> GetSessionsByGroup(string groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return Enumerable.Empty<IClientSession>();

        return _sessions.Values
            .Where(s => s.Groups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 根据标签获取客户端会话
    /// </summary>
    /// <param name="tagKey">标签键</param>
    /// <param name="tagValue">标签值</param>
    /// <returns>匹配标签的客户端会话集合</returns>
    public IEnumerable<IClientSession> GetSessionsByTag(string tagKey, string tagValue)
    {
        if (string.IsNullOrEmpty(tagKey)) return Enumerable.Empty<IClientSession>();

        return _sessions.Values
            .Where(s => s.Tags.TryGetValue(tagKey, out var value) &&
                        string.Equals(value, tagValue, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 向指定组广播Hub方法调用
    /// </summary>
    /// <typeparam name="THub">Hub接口类型</typeparam>
    /// <param name="groupName">组名</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="args">方法参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功调用的会话数量</returns>
    public async Task<int> BroadcastToGroupAsync<THub>(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub
    {
        var groupSessions = GetSessionsByGroup(groupName).ToList();
        var tasks = groupSessions.Select(async session =>
        {
            try
            {
                await session.InvokeAsync<THub>(methodName, args, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向会话 {SessionId} 广播Hub方法调用失败: {HubType}.{MethodName}",
                    session.Descriptor.Id, typeof(THub).Name, methodName);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(success => success);

        _logger.LogDebug("组广播完成: Group={GroupName}, Hub={HubType}, Method={MethodName}, Success={SuccessCount}/{TotalCount}",
            groupName, typeof(THub).Name, methodName, successCount, groupSessions.Count);

        return successCount;
    }

    /// <summary>
    /// 向所有已认证会话广播Hub方法调用
    /// </summary>
    /// <typeparam name="THub">Hub接口类型</typeparam>
    /// <param name="methodName">方法名称</param>
    /// <param name="args">方法参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功调用的会话数量</returns>
    public async Task<int> BroadcastToAllAsync<THub>(string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub
    {
        var authenticatedSessions = GetAuthenticatedSessions().ToList();
        var tasks = authenticatedSessions.Select(async session =>
        {
            try
            {
                await session.InvokeAsync<THub>(methodName, args, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向会话 {SessionId} 广播Hub方法调用失败: {HubType}.{MethodName}",
                    session.Descriptor.Id, typeof(THub).Name, methodName);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(success => success);

        _logger.LogDebug("全体广播完成: Hub={HubType}, Method={MethodName}, Success={SuccessCount}/{TotalCount}",
            typeof(THub).Name, methodName, successCount, authenticatedSessions.Count);

        return successCount;
    }

    /// <summary>
    /// 检查所有会话的健康状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果集合</returns>
    public async Task<IEnumerable<SessionHealthCheckResult>> CheckAllSessionsHealthAsync(CancellationToken cancellationToken = default)
    {
        var sessions = GetAllSessions().ToList();
        var tasks = sessions.Select(session => session.CheckHealthAsync(cancellationToken));
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 清理不健康的会话
    /// </summary>
    /// <param name="maxUnhealthyDuration">允许的最大不健康持续时间</param>
    /// <returns>清理的会话数量</returns>
    public async Task<int> CleanupUnhealthySessionsAsync(TimeSpan maxUnhealthyDuration)
    {
        var healthResults = await CheckAllSessionsHealthAsync();
        var cutoffTime = DateTime.UtcNow - maxUnhealthyDuration;

        var sessionsToRemove = healthResults
            .Where(r => (r.Health == SessionHealth.Failed || r.Health == SessionHealth.Unhealthy) &&
                       r.CheckedAt < cutoffTime)
            .Select(r => r.SessionId)
            .ToList();

        foreach (var sessionId in sessionsToRemove)
        {
            RemoveSession(sessionId);
        }

        if (sessionsToRemove.Count > 0)
        {
            _logger.LogInformation("已清理 {Count} 个不健康的会话", sessionsToRemove.Count);
        }

        return sessionsToRemove.Count;
    }

    /// <summary>
    /// 获取会话管理器统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    public SessionManagerStats GetSessionManagerStats()
    {
        var allSessions = GetAllSessions().ToList();
        var healthyCount = allSessions.Count(s => s.Health == SessionHealth.Healthy);
        var degradedCount = allSessions.Count(s => s.Health == SessionHealth.Degraded);
        var unhealthyCount = allSessions.Count(s => s.Health == SessionHealth.Unhealthy);
        var failedCount = allSessions.Count(s => s.Health == SessionHealth.Failed);

        var totalInvocations = allSessions.Sum(s => s.Statistics.HubInvocations);
        var totalSent = allSessions.Sum(s => s.Statistics.MessagesSent);
        var totalReceived = allSessions.Sum(s => s.Statistics.MessagesReceived);
        var totalBytes = allSessions.Sum(s => s.Statistics.BytesSent + s.Statistics.BytesReceived);

        return new SessionManagerStats
        {
            ActiveSessions = allSessions.Count,
            HealthySessions = healthyCount,
            DegradedSessions = degradedCount,
            UnhealthySessions = unhealthyCount,
            FailedSessions = failedCount,
            TotalSessionsCreated = Interlocked.Read(ref _totalSessionsCreated),
            TotalSessionsRemoved = Interlocked.Read(ref _totalSessionsRemoved),
            TotalHubInvocations = totalInvocations,
            TotalMessagesSent = totalSent,
            TotalMessagesReceived = totalReceived,
            TotalBytesTransferred = totalBytes
        };
    }

    #region IClientSessionManager Implementation

    /// <summary>
    /// 调用客户端Hub方法（有返回值）
    /// </summary>
    public Task<TResult> InvokeHubMethodAsync<THub, TResult>(IClientSession session, string methodName, object?[] args, CancellationToken cancellationToken)
        where THub : class, IPulseHub
    {
        Interlocked.Increment(ref _totalHubInvocations);

        // 这里应该实现实际的RPC调用逻辑
        // 目前返回一个模拟实现，实际项目中需要集成序列化和消息传输
        throw new NotImplementedException("Hub方法调用需要集成RPC消息传输逻辑");
    }

    /// <summary>
    /// 调用客户端Hub方法（无返回值）
    /// </summary>
    public Task InvokeHubMethodAsync<THub>(IClientSession session, string methodName, object?[] args, CancellationToken cancellationToken)
        where THub : class, IPulseHub
    {
        Interlocked.Increment(ref _totalHubInvocations);

        // 这里应该实现实际的RPC调用逻辑
        // 目前返回一个模拟实现，实际项目中需要集成序列化和消息传输
        return Task.CompletedTask;
    }

    #endregion

    #region Private Event Handlers

    /// <summary>
    /// 处理会话认证事件
    /// </summary>
    private void OnSessionAuthenticated(object? sender, AuthenticationChangedEventArgs e)
    {
        if (sender is IClientSession session && session.IsAuthenticated && session.AuthenticationContext != null)
        {
            SessionAuthenticated?.Invoke(this, new SessionAuthenticatedEventArgs(session, session.AuthenticationContext));

            _logger.LogInformation("会话已认证: SessionId={SessionId}, User={Username}",
                session.Descriptor.Id, session.AuthenticationContext.Name);
        }
    }

    /// <summary>
    /// 处理会话健康状态变化
    /// </summary>
    private void OnSessionHealthChanged(object? sender, SessionHealthChangedEventArgs e)
    {
        _logger.LogDebug("会话健康状态变化: SessionId={SessionId}, Health={PreviousHealth}->{CurrentHealth}, Reason={Reason}",
            e.SessionId, e.PreviousHealth, e.CurrentHealth, e.Reason);

        // 如果会话失败，自动移除
        if (e.CurrentHealth == SessionHealth.Failed)
        {
            _ = Task.Run(() => RemoveSession(e.SessionId));
        }
    }

    /// <summary>
    /// 执行定期健康检查
    /// </summary>
    private async void PerformHealthChecks(object? state)
    {
        if (_disposed) return;

        try
        {
            await CheckAllSessionsHealthAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行健康检查时发生异常");
        }
    }

    /// <summary>
    /// 执行定期清理
    /// </summary>
    private async void PerformCleanup(object? state)
    {
        if (_disposed) return;

        try
        {
            var maxUnhealthyDuration = TimeSpan.FromMilliseconds(SessionTimeoutMs);
            await CleanupUnhealthySessionsAsync(maxUnhealthyDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行清理时发生异常");
        }
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 停止定时器
        _healthCheckTimer.Dispose();
        _cleanupTimer.Dispose();

        // 关闭所有会话
        var allSessions = _sessions.Values.ToList();
        _sessions.Clear();
        _connectionToSessionMap.Clear();

        foreach (var session in allSessions)
        {
            try
            {
                session.AuthenticationChanged -= OnSessionAuthenticated;
                session.HealthChanged -= OnSessionHealthChanged;
                session.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放会话 {SessionId} 时发生异常", session.Descriptor.Id);
            }
        }

        var stats = GetSessionManagerStats();
        _logger.LogInformation("服务端会话管理器已释放，最终统计: 会话创建={SessionsCreated}, 会话移除={SessionsRemoved}, Hub调用={HubInvocations}",
            stats.TotalSessionsCreated, stats.TotalSessionsRemoved, stats.TotalHubInvocations);
    }

    #endregion
}