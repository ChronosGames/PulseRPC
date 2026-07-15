using PulseRPC.Client.Health;
using PulseRPC.Shared;

namespace PulseRPC.Client.Configuration;

/// <summary>
/// 客户端状态
/// </summary>
public enum ClientState
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
    /// 停止中
    /// </summary>
    Stopping,

    /// <summary>
    /// 已停止
    /// </summary>
    Stopped,

    /// <summary>
    /// 错误状态
    /// </summary>
    Error
}

/// <summary>
/// 客户端状态变化事件参数
/// </summary>
public sealed class ClientStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 之前的状态
    /// </summary>
    public ClientState PreviousState { get; set; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public ClientState CurrentState { get; set; }

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
/// 客户端统计信息
/// </summary>
public sealed class ClientStatistics
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// 启动时间
    /// </summary>
    public DateTime StartTime { get; set; }

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
    /// 总服务请求数
    /// </summary>
    public long TotalServiceRequests { get; set; }

    /// <summary>
    /// 成功服务请求数
    /// </summary>
    public long SuccessfulServiceRequests { get; set; }

    /// <summary>
    /// 失败服务请求数
    /// </summary>
    public long FailedServiceRequests { get; set; }

    /// <summary>
    /// 统计时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 客户端健康检查结果
/// </summary>
public sealed class ClientHealthCheckResult
{
    /// <summary>
    /// 整体健康状态
    /// </summary>
    public HealthStatus OverallHealth { get; set; }

    /// <summary>
    /// 连接健康检查结果
    /// </summary>
    public IReadOnlyList<HealthCheckResult> ConnectionResults { get; set; } = Array.Empty<HealthCheckResult>();

    /// <summary>
    /// 服务发现健康状态
    /// </summary>
    public HealthStatus ServiceDiscoveryHealth { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// 总检查时间
    /// </summary>
    public TimeSpan TotalCheckTime { get; set; }
}

/// <summary>
/// 服务代理选项
/// </summary>
public sealed class ServiceProxyOptions
{
    /// <summary>
    /// 负载均衡提示
    /// </summary>
    public LoadBalancingHint LoadBalancingHint { get; set; } = LoadBalancingHint.None;

    /// <summary>
    /// Stable key for <see cref="LoadBalancingStrategy.ConsistentHash"/> or a sticky load-balancing hint.
    /// The same logical user, tenant, or session must supply the same non-empty value across calls.
    /// </summary>
    public string? StickyKey { get; set; }
}

/// <summary>
/// 负载均衡提示
/// </summary>
public enum LoadBalancingHint
{
    /// <summary>
    /// 无特定提示
    /// </summary>
    None,

    /// <summary>
    /// 优先使用最少连接
    /// </summary>
    LeastConnections,

    /// <summary>
    /// 优先使用本地连接
    /// </summary>
    PreferLocal,

    /// <summary>
    /// 优先快速响应
    /// </summary>
    PreferFast,

    /// <summary>
    /// 使用粘性会话
    /// </summary>
    StickySession,

    /// <summary>
    /// 粘性模式（简化版）
    /// </summary>
    Sticky,

    /// <summary>
    /// 分布式模式
    /// </summary>
    Distribute
}

/// <summary>
/// 负载均衡策略
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// 随机选择
    /// </summary>
    Random,

    /// <summary>
    /// 轮询
    /// </summary>
    RoundRobin,

    /// <summary>
    /// 最少连接
    /// </summary>
    LeastConnections,

    /// <summary>
    /// 加权轮询
    /// </summary>
    WeightedRoundRobin,

    /// <summary>
    /// 一致性哈希
    /// </summary>
    ConsistentHash
}

/// <summary>
/// 连接生命周期
/// </summary>
public enum ConnectionLifetime
{
    /// <summary>
    /// 单例 - 全局共享
    /// </summary>
    Singleton,

    /// <summary>
    /// 会话 - 会话级别的连接
    /// </summary>
    Session,

    /// <summary>
    /// 持久 - 持久化连接
    /// </summary>
    Persistent,

    /// <summary>
    /// 作用域 - 在特定作用域内共享
    /// </summary>
    Scoped,

    /// <summary>
    /// 瞬态 - 每次创建新实例
    /// </summary>
    Transient
}
