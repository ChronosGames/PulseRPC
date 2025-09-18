namespace PulseRPC.Client.Core.LifecycleStrategies;

/// <summary>
/// 连接生命周期策略接口
/// </summary>
public interface IConnectionLifecycleStrategy : IDisposable
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 连接策略类型
    /// </summary>
    ConnectionStrategy Strategy { get; }

    /// <summary>
    /// 是否支持自动重连
    /// </summary>
    bool SupportsAutoReconnect { get; }

    /// <summary>
    /// 是否支持连接池
    /// </summary>
    bool SupportsPooling { get; }

    /// <summary>
    /// 初始化策略
    /// </summary>
    Task InitializeAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建连接
    /// </summary>
    Task<IConnectionContext> CreateConnectionAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理连接生命周期
    /// </summary>
    Task ManageConnectionAsync(IConnectionContext connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理连接断开
    /// </summary>
    Task OnConnectionDisconnectedAsync(IConnectionContext connection, string reason, Exception? exception = null);

    /// <summary>
    /// 处理连接失败
    /// </summary>
    Task OnConnectionFailedAsync(IConnectionContext connection, Exception exception);

    /// <summary>
    /// 清理连接
    /// </summary>
    Task CleanupConnectionAsync(IConnectionContext connection, string reason);

    /// <summary>
    /// 检查连接是否应该保持活跃
    /// </summary>
    bool ShouldKeepAlive(IConnectionContext connection, TimeSpan idleDuration);

    /// <summary>
    /// 获取重连延迟时间
    /// </summary>
    TimeSpan GetReconnectDelay(int attemptCount);

    /// <summary>
    /// 策略状态变化事件
    /// </summary>
    event EventHandler<LifecycleStrategyStateChangedEventArgs> StateChanged;
}

/// <summary>
/// 生命周期策略状态
/// </summary>
public enum LifecycleStrategyState
{
    /// <summary>
    /// 未初始化
    /// </summary>
    Uninitialized,

    /// <summary>
    /// 初始化中
    /// </summary>
    Initializing,

    /// <summary>
    /// 运行中
    /// </summary>
    Running,

    /// <summary>
    /// 暂停
    /// </summary>
    Paused,

    /// <summary>
    /// 错误状态
    /// </summary>
    Error,

    /// <summary>
    /// 已停止
    /// </summary>
    Stopped
}

/// <summary>
/// 生命周期策略状态变化事件参数
/// </summary>
public sealed class LifecycleStrategyStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 策略名称
    /// </summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// 之前的状态
    /// </summary>
    public LifecycleStrategyState PreviousState { get; set; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public LifecycleStrategyState CurrentState { get; set; }

    /// <summary>
    /// 状态变化原因
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 连接生命周期策略配置
/// </summary>
public sealed class LifecycleStrategyOptions
{
    /// <summary>
    /// 最大重连尝试次数
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// 初始重连延迟
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大重连延迟
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 连接空闲超时时间
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 心跳超时时间
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否启用心跳检测
    /// </summary>
    public bool EnableHeartbeat { get; set; } = true;

    /// <summary>
    /// 连接最大存活时间
    /// </summary>
    public TimeSpan? MaxConnectionLifetime { get; set; }

    /// <summary>
    /// 是否在连接空闲时自动断开
    /// </summary>
    public bool DisconnectOnIdle { get; set; } = false;
}

/// <summary>
/// 生命周期策略统计信息
/// </summary>
public sealed class LifecycleStrategyStatistics
{
    /// <summary>
    /// 策略名称
    /// </summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 管理的连接数
    /// </summary>
    public int ManagedConnections { get; set; }

    /// <summary>
    /// 总重连次数
    /// </summary>
    public long TotalReconnections { get; set; }

    /// <summary>
    /// 成功重连次数
    /// </summary>
    public long SuccessfulReconnections { get; set; }

    /// <summary>
    /// 失败重连次数
    /// </summary>
    public long FailedReconnections { get; set; }

    /// <summary>
    /// 连接清理次数
    /// </summary>
    public long ConnectionsCleanedUp { get; set; }

    /// <summary>
    /// 心跳检测次数
    /// </summary>
    public long HeartbeatChecks { get; set; }

    /// <summary>
    /// 心跳失败次数
    /// </summary>
    public long HeartbeatFailures { get; set; }

    /// <summary>
    /// 平均连接存活时间
    /// </summary>
    public TimeSpan AverageConnectionLifetime { get; set; }

    /// <summary>
    /// 统计时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }
}