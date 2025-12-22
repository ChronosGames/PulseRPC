using PulseRPC.Client.Health;
using PulseRPC.Transport;

namespace PulseRPC.Client;

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
/// 服务连接选项
/// </summary>
public sealed class ServiceConnectionOptions
{
    /// <summary>
    /// 偏好的传输类型
    /// </summary>
    public TransportType? PreferredTransport { get; set; }

    /// <summary>
    /// 连接策略
    /// </summary>
    public ConnectionStrategy? Strategy { get; set; }

    /// <summary>
    /// 自动重连
    /// </summary>
    public bool? AutoReconnect { get; set; }

    /// <summary>
    /// 连接超时
    /// </summary>
    public TimeSpan? ConnectTimeout { get; set; }
}

/// <summary>
/// 服务代理选项
/// </summary>
public sealed class ServiceProxyOptions
{
    /// <summary>
    /// 指定连接ID（精确路由）
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// 指定通道名称（按name过滤）
    /// </summary>
    public string? ChannelName { get; set; }

    /// <summary>
    /// 路由标签
    /// </summary>
    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>
    /// 偏好区域
    /// </summary>
    public string? PreferredRegion { get; set; }

    /// <summary>
    /// 负载均衡提示
    /// </summary>
    public LoadBalancingHint LoadBalancingHint { get; set; } = LoadBalancingHint.None;

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 重试策略
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// 缓存策略
    /// </summary>
    public bool UseCache { get; set; } = true;

    /// <summary>
    /// 启用缓存
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// 事件监听器选项
/// </summary>
public sealed class EventListenerOptions
{
    /// <summary>
    /// 监听器名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 自动重新订阅
    /// </summary>
    public bool AutoResubscribe { get; set; } = true;

    /// <summary>
    /// 缓冲区大小
    /// </summary>
    public int BufferSize { get; set; } = 1000;
}

/// <summary>
/// 重试策略
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试间隔
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 最大重试间隔
    /// </summary>
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
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

/// <summary>
/// 连接池策略
/// </summary>
public enum PoolingStrategy
{
    /// <summary>
    /// 无连接池
    /// </summary>
    None,

    /// <summary>
    /// 固定大小池
    /// </summary>
    FixedSize,

    /// <summary>
    /// 动态大小池
    /// </summary>
    Dynamic,

    /// <summary>
    /// 按需创建池
    /// </summary>
    OnDemand
}

