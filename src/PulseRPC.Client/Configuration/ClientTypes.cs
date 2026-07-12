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
/// Legacy service-connection options retained for compatibility.
/// </summary>
[Obsolete("This type is not consumed by the client runtime. Configure ConnectionDescriptor directly.", false)]
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
    /// Legacy exact-connection selector that is not consumed by automatic routing.
    /// </summary>
    [Obsolete("This property is not consumed. Use the generated overload that accepts connectionId explicitly.", false)]
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Legacy channel-name filter that is not consumed by routing.
    /// </summary>
    [Obsolete("This property is not consumed by client routing.", false)]
    public string? ChannelName { get; set; }

    /// <summary>
    /// Legacy tag filter that is not consumed by routing.
    /// </summary>
    [Obsolete("This property is not consumed by client routing.", false)]
    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>
    /// Legacy region preference that is not consumed by routing.
    /// </summary>
    [Obsolete("This property is not consumed by client routing.", false)]
    public string? PreferredRegion { get; set; }

    /// <summary>
    /// 负载均衡提示
    /// </summary>
    public LoadBalancingHint LoadBalancingHint { get; set; } = LoadBalancingHint.None;

    /// <summary>
    /// Stable key for <see cref="LoadBalancingStrategy.ConsistentHash"/> or a sticky load-balancing hint.
    /// The same logical user, tenant, or session must supply the same non-empty value across calls.
    /// </summary>
    public string? StickyKey { get; set; }

    /// <summary>
    /// Legacy per-proxy timeout that is not propagated to generated stubs.
    /// </summary>
    [Obsolete("This property is not propagated to generated stubs. Use the CancellationToken parameter on the generated RPC method.", false)]
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Legacy retry policy that is not connected to generated stub calls.
    /// </summary>
    [Obsolete("This property is not connected to generated stub calls.", false)]
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// Legacy proxy-cache flag that is not consumed.
    /// </summary>
    [Obsolete("This property is not consumed; generated service factories do not maintain a proxy cache.", false)]
    public bool UseCache { get; set; } = true;
}

/// <summary>
/// Legacy event-listener options retained for generated API compatibility.
/// </summary>
[Obsolete("EventListenerOptions is not consumed by generated registration. Pass null and use IClientChannel.RegisterReceiver<T>() for current code.", false)]
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
/// Legacy builder retry DTO retained for compatibility.
/// </summary>
[Obsolete("This retry policy is not connected to generated calls or connection attempts.", false)]
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
/// Legacy connection-pool strategy retained for compatibility.
/// </summary>
[Obsolete("Connection pooling is not connected to the client runtime.", false)]
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
