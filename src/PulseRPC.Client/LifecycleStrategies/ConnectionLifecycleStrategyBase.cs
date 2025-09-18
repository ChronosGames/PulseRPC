using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace PulseRPC.Client.Core.LifecycleStrategies;

/// <summary>
/// 连接生命周期策略基类
/// </summary>
public abstract class ConnectionLifecycleStrategyBase : IConnectionLifecycleStrategy
{
    protected readonly IConnectionManager _connectionManager;
    protected readonly ILogger _logger;
    protected readonly LifecycleStrategyOptions _options;
    protected readonly LifecycleStrategyStatistics _statistics;
    protected readonly ConcurrentDictionary<string, ConnectionLifecycleInfo> _managedConnections = new();
    protected readonly Timer? _maintenanceTimer;

    private volatile LifecycleStrategyState _state = LifecycleStrategyState.Uninitialized;
    private volatile bool _disposed;

    /// <summary>
    /// 策略名称
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// 连接策略类型
    /// </summary>
    public abstract ConnectionStrategy Strategy { get; }

    /// <summary>
    /// 是否支持自动重连
    /// </summary>
    public abstract bool SupportsAutoReconnect { get; }

    /// <summary>
    /// 是否支持连接池
    /// </summary>
    public abstract bool SupportsPooling { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public LifecycleStrategyState State => _state;

    /// <summary>
    /// 策略状态变化事件
    /// </summary>
    public event EventHandler<LifecycleStrategyStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 构造函数
    /// </summary>
    protected ConnectionLifecycleStrategyBase(
        IConnectionManager connectionManager,
        LifecycleStrategyOptions? options = null,
        ILogger? logger = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _options = options ?? new LifecycleStrategyOptions();
        _logger = logger ?? NullLogger.Instance;

        _statistics = new LifecycleStrategyStatistics
        {
            StrategyName = Name,
            CreatedAt = DateTime.UtcNow
        };

        // 创建定期维护定时器（每分钟执行一次）
        if (_options.EnableHeartbeat || _options.DisconnectOnIdle)
        {
            _maintenanceTimer = new Timer(PerformMaintenance, null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        _logger.LogDebug("生命周期策略已创建: {StrategyName}", Name);
    }

    /// <summary>
    /// 初始化策略
    /// </summary>
    public virtual async Task InitializeAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_state != LifecycleStrategyState.Uninitialized)
        {
            throw new InvalidOperationException($"策略已初始化，当前状态: {_state}");
        }

        ChangeState(LifecycleStrategyState.Initializing, "开始初始化");

        try
        {
            _logger.LogInformation("初始化生命周期策略: {StrategyName}", Name);

            // 执行具体策略的初始化逻辑
            await OnInitializeAsync(descriptor, cancellationToken);

            ChangeState(LifecycleStrategyState.Running, "初始化完成");
            _logger.LogInformation("生命周期策略初始化完成: {StrategyName}", Name);
        }
        catch (Exception ex)
        {
            ChangeState(LifecycleStrategyState.Error, "初始化失败", ex);
            _logger.LogError(ex, "生命周期策略初始化失败: {StrategyName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 创建连接
    /// </summary>
    public virtual async Task<IConnectionContext> CreateConnectionAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureRunning();

        var connection = await _connectionManager.ConnectAsync(descriptor, cancellationToken);

        // 注册连接到管理列表
        var lifecycleInfo = new ConnectionLifecycleInfo(connection, DateTime.UtcNow);
        _managedConnections.TryAdd(connection.Id, lifecycleInfo);

        // 订阅连接状态变化事件
        connection.StateChanged += OnConnectionStateChanged;

        _statistics.ManagedConnections = _managedConnections.Count;
        _logger.LogDebug("连接已创建并注册到生命周期管理: {ConnectionId}, 策略: {StrategyName}",
            connection.Id, Name);

        return connection;
    }

    /// <summary>
    /// 管理连接生命周期
    /// </summary>
    public virtual async Task ManageConnectionAsync(IConnectionContext connection, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureRunning();

        if (!_managedConnections.ContainsKey(connection.Id))
        {
            var lifecycleInfo = new ConnectionLifecycleInfo(connection, DateTime.UtcNow);
            _managedConnections.TryAdd(connection.Id, lifecycleInfo);
            connection.StateChanged += OnConnectionStateChanged;
        }

        // 执行具体策略的连接管理逻辑
        await OnManageConnectionAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    public virtual async Task OnConnectionDisconnectedAsync(IConnectionContext connection, string reason, Exception? exception = null)
    {
        ThrowIfDisposed();

        _logger.LogWarning("连接断开: {ConnectionId}, 策略: {StrategyName}, 原因: {Reason}",
            connection.Id, Name, reason);

        if (_managedConnections.TryGetValue(connection.Id, out var lifecycleInfo))
        {
            lifecycleInfo.LastDisconnectedAt = DateTime.UtcNow;
            lifecycleInfo.DisconnectReason = reason;
        }

        // 执行具体策略的断开处理逻辑
        await OnConnectionDisconnectedInternalAsync(connection, reason, exception);
    }

    /// <summary>
    /// 处理连接失败
    /// </summary>
    public virtual async Task OnConnectionFailedAsync(IConnectionContext connection, Exception exception)
    {
        ThrowIfDisposed();

        _logger.LogError(exception, "连接失败: {ConnectionId}, 策略: {StrategyName}",
            connection.Id, Name);

        if (_managedConnections.TryGetValue(connection.Id, out var lifecycleInfo))
        {
            lifecycleInfo.FailureCount++;
            lifecycleInfo.LastFailureAt = DateTime.UtcNow;
            lifecycleInfo.LastException = exception;
        }

        // 执行具体策略的失败处理逻辑
        await OnConnectionFailedInternalAsync(connection, exception);
    }

    /// <summary>
    /// 清理连接
    /// </summary>
    public virtual async Task CleanupConnectionAsync(IConnectionContext connection, string reason)
    {
        ThrowIfDisposed();

        _logger.LogDebug("清理连接: {ConnectionId}, 策略: {StrategyName}, 原因: {Reason}",
            connection.Id, Name, reason);

        // 从管理列表中移除
        if (_managedConnections.TryRemove(connection.Id, out var lifecycleInfo))
        {
            connection.StateChanged -= OnConnectionStateChanged;
            UpdateAverageLifetime(lifecycleInfo);
            _statistics.ConnectionsCleanedUp++;
        }

        // 执行具体策略的清理逻辑
        await OnCleanupConnectionAsync(connection, reason);

        _statistics.ManagedConnections = _managedConnections.Count;
    }

    /// <summary>
    /// 检查连接是否应该保持活跃
    /// </summary>
    public virtual bool ShouldKeepAlive(IConnectionContext connection, TimeSpan idleDuration)
    {
        if (_options.DisconnectOnIdle && idleDuration > _options.IdleTimeout)
        {
            return false;
        }

        if (_options.MaxConnectionLifetime.HasValue &&
            _managedConnections.TryGetValue(connection.Id, out var lifecycleInfo))
        {
            var lifetime = DateTime.UtcNow - lifecycleInfo.CreatedAt;
            if (lifetime > _options.MaxConnectionLifetime.Value)
            {
                return false;
            }
        }

        return ShouldKeepAliveInternal(connection, idleDuration);
    }

    /// <summary>
    /// 获取重连延迟时间
    /// </summary>
    public virtual TimeSpan GetReconnectDelay(int attemptCount)
    {
        if (!_options.UseExponentialBackoff)
        {
            return _options.InitialReconnectDelay;
        }

        // 指数退避算法
        var delay = TimeSpan.FromMilliseconds(
            _options.InitialReconnectDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1));

        return delay > _options.MaxReconnectDelay ? _options.MaxReconnectDelay : delay;
    }

    /// <summary>
    /// 获取策略统计信息
    /// </summary>
    public virtual LifecycleStrategyStatistics GetStatistics()
    {
        _statistics.ManagedConnections = _managedConnections.Count;
        _statistics.Timestamp = DateTime.UtcNow;
        return _statistics;
    }

    /// <summary>
    /// 具体策略的初始化逻辑（子类实现）
    /// </summary>
    protected virtual async Task OnInitializeAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 具体策略的连接管理逻辑（子类实现）
    /// </summary>
    protected virtual async Task OnManageConnectionAsync(IConnectionContext connection, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 具体策略的连接断开处理逻辑（子类实现）
    /// </summary>
    protected virtual async Task OnConnectionDisconnectedInternalAsync(IConnectionContext connection, string reason, Exception? exception = null)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 具体策略的连接失败处理逻辑（子类实现）
    /// </summary>
    protected virtual async Task OnConnectionFailedInternalAsync(IConnectionContext connection, Exception exception)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 具体策略的连接清理逻辑（子类实现）
    /// </summary>
    protected virtual async Task OnCleanupConnectionAsync(IConnectionContext connection, string reason)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 具体策略的保持活跃检查逻辑（子类实现）
    /// </summary>
    protected virtual bool ShouldKeepAliveInternal(IConnectionContext connection, TimeSpan idleDuration)
    {
        return true;
    }

    /// <summary>
    /// 定期维护
    /// </summary>
    private async void PerformMaintenance(object? state)
    {
        if (_disposed || _state != LifecycleStrategyState.Running)
            return;

        try
        {
            await PerformMaintenanceAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生命周期策略定期维护失败: {StrategyName}", Name);
        }
    }

    /// <summary>
    /// 执行维护任务
    /// </summary>
    protected virtual async Task PerformMaintenanceAsync()
    {
        var now = DateTime.UtcNow;
        var connectionsToCleanup = new List<IConnectionContext>();

        foreach (var kvp in _managedConnections)
        {
            var connectionId = kvp.Key;
            var lifecycleInfo = kvp.Value;
            var connection = lifecycleInfo.Connection;

            try
            {
                // 心跳检测
                if (_options.EnableHeartbeat)
                {
                    var timeSinceLastHeartbeat = now - lifecycleInfo.LastHeartbeatAt;
                    if (timeSinceLastHeartbeat > _options.HeartbeatInterval)
                    {
                        await PerformHeartbeatAsync(connection, lifecycleInfo);
                    }
                }

                // 检查空闲超时
                if (_options.DisconnectOnIdle)
                {
                    var idleTime = now - connection.Statistics.LastActiveAt;
                    if (idleTime > _options.IdleTimeout)
                    {
                        connectionsToCleanup.Add(connection);
                    }
                }

                // 检查最大存活时间
                if (_options.MaxConnectionLifetime.HasValue)
                {
                    var lifetime = now - lifecycleInfo.CreatedAt;
                    if (lifetime > _options.MaxConnectionLifetime.Value)
                    {
                        connectionsToCleanup.Add(connection);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接维护检查失败: {ConnectionId}", connectionId);
            }
        }

        // 清理需要关闭的连接
        foreach (var connection in connectionsToCleanup)
        {
            try
            {
                await CleanupConnectionAsync(connection, "维护清理");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "维护清理连接失败: {ConnectionId}", connection.Id);
            }
        }
    }

    /// <summary>
    /// 执行心跳检测
    /// </summary>
    protected virtual async Task PerformHeartbeatAsync(IConnectionContext connection, ConnectionLifecycleInfo lifecycleInfo)
    {
        try
        {
            lifecycleInfo.LastHeartbeatAt = DateTime.UtcNow;
            _statistics.HeartbeatChecks++;

            // 简单的心跳检测 - 检查连接状态
            if (connection.State != ExtendedConnectionState.Connected &&
                connection.State != ExtendedConnectionState.Active &&
                connection.State != ExtendedConnectionState.Idle)
            {
                _statistics.HeartbeatFailures++;
                _logger.LogWarning("心跳检测失败: {ConnectionId}, 状态: {State}", connection.Id, connection.State);

                if (SupportsAutoReconnect)
                {
                    await OnConnectionFailedAsync(connection, new InvalidOperationException("心跳检测失败"));
                }
            }
        }
        catch (Exception ex)
        {
            _statistics.HeartbeatFailures++;
            _logger.LogError(ex, "心跳检测异常: {ConnectionId}", connection.Id);
        }
    }

    /// <summary>
    /// 处理连接状态变化
    /// </summary>
    protected async void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (_disposed)
            return;

        try
        {
            if (sender is IConnectionContext connection)
            {
                switch (e.CurrentState)
                {
                    case ExtendedConnectionState.Disconnected:
                        await OnConnectionDisconnectedAsync(connection, e.Reason ?? "状态变化", e.Exception);
                        break;

                    case ExtendedConnectionState.Failed:
                        if (e.Exception != null)
                        {
                            await OnConnectionFailedAsync(connection, e.Exception);
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理连接状态变化失败: {ConnectionId}", e.ConnectionId);
        }
    }

    /// <summary>
    /// 更新平均连接存活时间
    /// </summary>
    private void UpdateAverageLifetime(ConnectionLifecycleInfo lifecycleInfo)
    {
        var lifetime = DateTime.UtcNow - lifecycleInfo.CreatedAt;
        var totalConnections = _statistics.ConnectionsCleanedUp + 1;
        var totalTime = _statistics.AverageConnectionLifetime.TotalMilliseconds * _statistics.ConnectionsCleanedUp +
                       lifetime.TotalMilliseconds;
        _statistics.AverageConnectionLifetime = TimeSpan.FromMilliseconds(totalTime / totalConnections);
    }

    /// <summary>
    /// 状态变化处理
    /// </summary>
    protected virtual void ChangeState(LifecycleStrategyState newState, string reason, Exception? exception = null)
    {
        var previousState = _state;
        _state = newState;

        StateChanged?.Invoke(this, new LifecycleStrategyStateChangedEventArgs
        {
            StrategyName = Name,
            PreviousState = previousState,
            CurrentState = newState,
            Reason = reason,
            Exception = exception
        });

        _logger.LogDebug("生命周期策略状态变化: {StrategyName}, {PreviousState} -> {CurrentState}, 原因: {Reason}",
            Name, previousState, newState, reason);
    }

    /// <summary>
    /// 确保策略处于运行状态
    /// </summary>
    protected void EnsureRunning()
    {
        if (_state != LifecycleStrategyState.Running)
        {
            throw new InvalidOperationException($"生命周期策略未处于运行状态，当前状态: {_state}");
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            ChangeState(LifecycleStrategyState.Stopped, "策略正在关闭");

            // 停止维护定时器
            _maintenanceTimer?.Dispose();

            // 清理所有管理的连接
            var cleanupTasks = _managedConnections.Values.Select(async lifecycleInfo =>
            {
                try
                {
                    lifecycleInfo.Connection.StateChanged -= OnConnectionStateChanged;
                    await CleanupConnectionAsync(lifecycleInfo.Connection, "策略关闭");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "关闭时清理连接失败: {ConnectionId}", lifecycleInfo.Connection.Id);
                }
            });

            Task.WaitAll(cleanupTasks.ToArray(), TimeSpan.FromSeconds(30));
            _managedConnections.Clear();

            _logger.LogInformation("生命周期策略已关闭: {StrategyName}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭生命周期策略时发生错误: {StrategyName}", Name);
        }
    }
}

/// <summary>
/// 连接生命周期信息
/// </summary>
public sealed class ConnectionLifecycleInfo
{
    public IConnectionContext Connection { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastHeartbeatAt { get; set; }
    public DateTime? LastDisconnectedAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public string? DisconnectReason { get; set; }
    public Exception? LastException { get; set; }
    public int ReconnectAttempts { get; set; }
    public int FailureCount { get; set; }

    public ConnectionLifecycleInfo(IConnectionContext connection, DateTime createdAt)
    {
        Connection = connection;
        CreatedAt = createdAt;
        LastHeartbeatAt = createdAt;
    }
}
