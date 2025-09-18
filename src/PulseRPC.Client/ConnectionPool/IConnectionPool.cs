using Microsoft.Extensions.Logging;

namespace PulseRPC.Client.ConnectionPool;

/// <summary>
/// 连接池接口 - 高性能连接池管理
/// 实现思路：
/// - 租借模式：使用租借模式管理连接生命周期
/// - 动态扩缩容：根据负载动态调整连接池大小
/// - 健康检查：定期检查池中连接的健康状态
/// - 空闲回收：自动回收长时间空闲的连接
/// - 统计监控：提供详细的连接池统计信息
/// </summary>
public interface IConnectionPool : IDisposable
{
    /// <summary>
    /// 连接池名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 连接池状态
    /// </summary>
    ConnectionPoolState State { get; }

    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 连接池配置选项
    /// </summary>
    ConnectionPoolOptions Options { get; }

    /// <summary>
    /// 当前连接数
    /// </summary>
    int CurrentSize { get; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    int ActiveConnections { get; }

    /// <summary>
    /// 可用连接数
    /// </summary>
    int AvailableConnections { get; }

    /// <summary>
    /// 初始化连接池
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取连接（租借）
    /// </summary>
    Task<IConnectionLease> AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取连接（租借）- 带超时
    /// </summary>
    Task<IConnectionLease> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取连接（非阻塞）
    /// </summary>
    Task<IConnectionLease?> TryAcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放连接（归还给连接池）
    /// </summary>
    Task ReleaseAsync(IConnectionLease lease, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行健康检查
    /// </summary>
    Task<ConnectionPoolHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    Task<int> CleanupIdleConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    ConnectionPoolStatistics GetStatistics();

    /// <summary>
    /// 刷新连接池（重新创建所有连接）
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接池状态变化事件
    /// </summary>
    event EventHandler<ConnectionPoolStateChangedEventArgs> StateChanged;

    /// <summary>
    /// 连接获取事件
    /// </summary>
    event EventHandler<ConnectionAcquiredEventArgs> ConnectionAcquired;

    /// <summary>
    /// 连接释放事件
    /// </summary>
    event EventHandler<ConnectionReleasedEventArgs> ConnectionReleased;
}

/// <summary>
/// 连接租借接口 - 连接池中连接的租借凭证
/// 实现思路：
/// - RAII模式：通过Dispose自动归还连接
/// - 状态跟踪：跟踪租借状态，防止重复归还
/// - 使用统计：记录连接使用情况
/// - 超时保护：防止连接被长时间占用
/// </summary>
public interface IConnectionLease : IDisposable
{
    /// <summary>
    /// 租借ID
    /// </summary>
    string LeaseId { get; }

    /// <summary>
    /// 连接上下文
    /// </summary>
    IConnection Connection { get; }

    /// <summary>
    /// 租借时间
    /// </summary>
    DateTime AcquiredAt { get; }

    /// <summary>
    /// 最后使用时间
    /// </summary>
    DateTime LastUsedAt { get; }

    /// <summary>
    /// 是否有效
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// 租借标签
    /// </summary>
    Dictionary<string, object> Tags { get; }

    /// <summary>
    /// 更新最后使用时间
    /// </summary>
    void UpdateLastUsed();

    /// <summary>
    /// 标记为无效
    /// </summary>
    void MarkInvalid(string reason);
}

/// <summary>
/// 连接池工厂接口
/// </summary>
public interface IConnectionPoolFactory
{
    /// <summary>
    /// 创建连接池
    /// </summary>
    IConnectionPool CreatePool(string name, ConnectionDescriptor descriptor, ConnectionPoolOptions options, ILoggerFactory? loggerFactory = null);

    /// <summary>
    /// 获取连接池
    /// </summary>
    IConnectionPool? GetPool(string name);

    /// <summary>
    /// 移除连接池
    /// </summary>
    Task<bool> RemovePoolAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有连接池
    /// </summary>
    IReadOnlyList<IConnectionPool> GetAllPools();
}

/// <summary>
/// 连接池状态
/// </summary>
public enum ConnectionPoolState
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
    /// 关闭中
    /// </summary>
    Shutting,

    /// <summary>
    /// 已关闭
    /// </summary>
    Shutdown,

    /// <summary>
    /// 错误状态
    /// </summary>
    Error,

    /// <summary>
    /// 已释放
    /// </summary>
    Disposed
}

/// <summary>
/// 连接健康状态
/// </summary>
public enum ConnectionHealth
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 降级
    /// </summary>
    Degraded,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown
}

/// <summary>
/// 连接池健康检查结果
/// </summary>
public sealed class ConnectionPoolHealthResult
{
    /// <summary>
    /// 连接池名称
    /// </summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// 整体健康状态
    /// </summary>
    public ConnectionHealth OverallHealth { get; set; }

    /// <summary>
    /// 连接健康检查结果
    /// </summary>
    public IReadOnlyList<HealthCheckResult> ConnectionResults { get; set; } = Array.Empty<HealthCheckResult>();

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// 检查耗时
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 连接池统计信息
/// </summary>
public sealed class ConnectionPoolStatistics
{
    /// <summary>
    /// 连接池名称
    /// </summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// 连接池状态
    /// </summary>
    public ConnectionPoolState State { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 运行时间
    /// </summary>
    public TimeSpan Uptime { get; set; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 空闲连接数
    /// </summary>
    public int IdleConnections { get; set; }

    /// <summary>
    /// 已租借连接数
    /// </summary>
    public int LeasedConnections { get; set; }

    /// <summary>
    /// 最小连接池大小
    /// </summary>
    public int MinPoolSize { get; set; }

    /// <summary>
    /// 最大连接池大小
    /// </summary>
    public int MaxPoolSize { get; set; }

    /// <summary>
    /// 总获取次数
    /// </summary>
    public long TotalAcquisitions { get; set; }

    /// <summary>
    /// 成功获取次数
    /// </summary>
    public long SuccessfulAcquisitions { get; set; }

    /// <summary>
    /// 失败获取次数
    /// </summary>
    public long FailedAcquisitions { get; set; }

    /// <summary>
    /// 平均获取时间
    /// </summary>
    public TimeSpan? AverageAcquisitionTime { get; set; }

    /// <summary>
    /// 最大获取时间
    /// </summary>
    public TimeSpan MaxAcquisitionTime { get; set; }

    /// <summary>
    /// 等待中的请求数
    /// </summary>
    public int PendingRequests { get; set; }

    /// <summary>
    /// 连接创建次数
    /// </summary>
    public long ConnectionsCreated { get; set; }

    /// <summary>
    /// 连接销毁次数
    /// </summary>
    public long ConnectionsDestroyed { get; set; }

    /// <summary>
    /// 总连接创建次数
    /// </summary>
    public long TotalCreations { get; set; }

    /// <summary>
    /// 成功连接创建次数
    /// </summary>
    public long SuccessfulCreations { get; set; }

    /// <summary>
    /// 失败连接创建次数
    /// </summary>
    public long FailedCreations { get; set; }

    /// <summary>
    /// 总共创建的连接数（历史总数）
    /// </summary>
    public long ConnectionsCreatedTotal { get; set; }

    /// <summary>
    /// 总共销毁的连接数（历史总数）
    /// </summary>
    public long ConnectionsDestroyedTotal { get; set; }

    /// <summary>
    /// 统计时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 最后维护时间
    /// </summary>
    public DateTime? LastMaintenanceAt { get; set; }

    /// <summary>
    /// 平均连接生命周期
    /// </summary>
    public TimeSpan? AverageConnectionLifetime { get; set; }
}

/// <summary>
/// 连接池状态变化事件参数
/// </summary>
public sealed class ConnectionPoolStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 连接池名称
    /// </summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// 之前的状态
    /// </summary>
    public ConnectionPoolState PreviousState { get; set; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public ConnectionPoolState CurrentState { get; set; }

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
/// 连接获取事件参数
/// </summary>
public sealed class ConnectionAcquiredEventArgs : EventArgs
{
    /// <summary>
    /// 连接池名称
    /// </summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// 租借ID
    /// </summary>
    public string LeaseId { get; set; } = string.Empty;

    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 获取耗时
    /// </summary>
    public TimeSpan AcquisitionTime { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 连接释放事件参数
/// </summary>
public sealed class ConnectionReleasedEventArgs : EventArgs
{
    /// <summary>
    /// 连接池名称
    /// </summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// 租借ID
    /// </summary>
    public string LeaseId { get; set; } = string.Empty;

    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 使用时长
    /// </summary>
    public TimeSpan UsageDuration { get; set; }

    /// <summary>
    /// 释放原因
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
