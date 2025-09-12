namespace PulseRPC.Client.Core;

/// <summary>
/// 服务端点信息
/// </summary>
public sealed record ServiceEndpoint
{
    /// <summary>
    /// 端点ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 端点地址
    /// </summary>
    public EndpointAddress? Address { get; set; }

    /// <summary>
    /// 端点权重（用于负载均衡）
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 端点优先级
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 健康状态
    /// </summary>
    public ServiceEndpointHealth Health { get; set; } = ServiceEndpointHealth.Unknown;

    /// <summary>
    /// 版本信息
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 区域/数据中心
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// 可用区
    /// </summary>
    public string? Zone { get; set; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// 端点标签
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 端点元数据
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

/// <summary>
/// 服务端点健康状态
/// </summary>
public enum ServiceEndpointHealth
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 警告状态
    /// </summary>
    Warning,

    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown
}

/// <summary>
/// 服务发现查询条件
/// </summary>
public sealed record ServiceDiscoveryQuery
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 版本过滤条件
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 区域过滤条件
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// 可用区过滤条件
    /// </summary>
    public string? Zone { get; set; }

    /// <summary>
    /// 只返回健康的端点
    /// </summary>
    public bool OnlyHealthy { get; set; } = true;

    /// <summary>
    /// 标签过滤条件
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 最大返回数量
    /// </summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}

/// <summary>
/// 服务发现结果
/// </summary>
public sealed record ServiceDiscoveryResult
{
    /// <summary>
    /// 查询条件
    /// </summary>
    public ServiceDiscoveryQuery? Query { get; set; }

    /// <summary>
    /// 发现的端点列表
    /// </summary>
    public IReadOnlyList<ServiceEndpoint> Endpoints { get; set; } = Array.Empty<ServiceEndpoint>();

    /// <summary>
    /// 查询时间
    /// </summary>
    public DateTime QueriedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 结果来源
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// 是否来自缓存
    /// </summary>
    public bool FromCache { get; set; }

    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public DateTime? CacheExpiresAt { get; set; }
}

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery : IDisposable
{
    /// <summary>
    /// 发现服务端点
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>发现结果</returns>
    Task<ServiceDiscoveryResult> DiscoverAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发现服务端点（简化版本）
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>端点列表</returns>
    Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听服务变化
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="callback">变化回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>监听令牌</returns>
    Task<IServiceWatcher> WatchAsync(string serviceName, Action<ServiceChangeEvent> callback, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取支持的服务列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务名称列表</returns>
    Task<IReadOnlyList<string>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否存在</returns>
    Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新缓存
    /// </summary>
    /// <param name="serviceName">服务名称，为null时刷新全部</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新任务</returns>
    Task RefreshAsync(string? serviceName = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务监听器接口
/// </summary>
public interface IServiceWatcher : IDisposable
{
    /// <summary>
    /// 监听的服务名称
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// 是否正在监听
    /// </summary>
    bool IsWatching { get; }

    /// <summary>
    /// 停止监听
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>停止任务</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务变化事件
/// </summary>
public sealed record ServiceChangeEvent
{
    /// <summary>
    /// 变化类型
    /// </summary>
    public ServiceChangeType ChangeType { get; set; }

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 端点信息
    /// </summary>
    public ServiceEndpoint? Endpoint { get; set; }

    /// <summary>
    /// 变化时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 变化来源
    /// </summary>
    public string? Source { get; set; }
}

/// <summary>
/// 服务变化类型
/// </summary>
public enum ServiceChangeType
{
    /// <summary>
    /// 端点添加
    /// </summary>
    EndpointAdded,

    /// <summary>
    /// 端点移除
    /// </summary>
    EndpointRemoved,

    /// <summary>
    /// 端点更新
    /// </summary>
    EndpointUpdated,

    /// <summary>
    /// 端点健康状态变化
    /// </summary>
    EndpointHealthChanged,

    /// <summary>
    /// 服务移除
    /// </summary>
    ServiceRemoved
}

/// <summary>
/// 负载均衡策略
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// 轮询
    /// </summary>
    RoundRobin,

    /// <summary>
    /// 随机
    /// </summary>
    Random,

    /// <summary>
    /// 加权轮询
    /// </summary>
    WeightedRoundRobin,

    /// <summary>
    /// 加权随机
    /// </summary>
    WeightedRandom,

    /// <summary>
    /// 最少连接
    /// </summary>
    LeastConnections,

    /// <summary>
    /// 最快响应
    /// </summary>
    FastestResponse,

    /// <summary>
    /// 一致性哈希
    /// </summary>
    ConsistentHash,

    /// <summary>
    /// IP哈希
    /// </summary>
    IpHash,

    /// <summary>
    /// 优先级优先
    /// </summary>
    Priority,

    /// <summary>
    /// 自定义策略
    /// </summary>
    Custom
}

/// <summary>
/// 负载均衡器接口
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// 负载均衡策略
    /// </summary>
    LoadBalancingStrategy Strategy { get; }

    /// <summary>
    /// 选择端点
    /// </summary>
    /// <param name="endpoints">可用端点列表</param>
    /// <param name="context">负载均衡上下文</param>
    /// <returns>选中的端点</returns>
    ServiceEndpoint? Select(IReadOnlyList<ServiceEndpoint> endpoints, LoadBalancingContext? context = null);

    /// <summary>
    /// 记录请求结果（用于统计和调整）
    /// </summary>
    /// <param name="endpoint">请求的端点</param>
    /// <param name="result">请求结果</param>
    void RecordResult(ServiceEndpoint endpoint, RequestResult result);

    /// <summary>
    /// 获取端点统计信息
    /// </summary>
    /// <param name="endpoint">端点</param>
    /// <returns>统计信息</returns>
    EndpointStatistics? GetStatistics(ServiceEndpoint endpoint);

    /// <summary>
    /// 重置统计信息
    /// </summary>
    void ResetStatistics();
}

/// <summary>
/// 负载均衡上下文
/// </summary>
public sealed record LoadBalancingContext
{
    /// <summary>
    /// 请求ID
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// 客户端ID
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 用户ID（用于一致性哈希）
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 会话ID（用于粘性会话）
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 哈希键（用于一致性哈希）
    /// </summary>
    public string? HashKey { get; set; }

    /// <summary>
    /// 排除的端点（已失败的端点）
    /// </summary>
    public ISet<string> ExcludedEndpoints { get; set; } = new HashSet<string>();

    /// <summary>
    /// 首选区域
    /// </summary>
    public string? PreferredRegion { get; set; }

    /// <summary>
    /// 首选可用区
    /// </summary>
    public string? PreferredZone { get; set; }

    /// <summary>
    /// 扩展属性
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}

/// <summary>
/// 请求结果
/// </summary>
public sealed record RequestResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan ResponseTime { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// HTTP状态码（如果适用）
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// 请求时间
    /// </summary>
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 端点统计信息
/// </summary>
public sealed record EndpointStatistics
{
    /// <summary>
    /// 端点ID
    /// </summary>
    public string EndpointId { get; set; } = string.Empty;

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// 当前活跃请求数
    /// </summary>
    public int ActiveRequests { get; set; }

    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// 最小响应时间（毫秒）
    /// </summary>
    public double MinResponseTimeMs { get; set; }

    /// <summary>
    /// 最大响应时间（毫秒）
    /// </summary>
    public double MaxResponseTimeMs { get; set; }

    /// <summary>
    /// 成功率（0-1）
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// 错误率（0-1）
    /// </summary>
    public double ErrorRate { get; set; }

    /// <summary>
    /// 最后请求时间
    /// </summary>
    public DateTime? LastRequestTime { get; set; }

    /// <summary>
    /// 统计开始时间
    /// </summary>
    public DateTime StatisticsStartTime { get; set; }

    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 负载均衡器工厂接口
/// </summary>
public interface ILoadBalancerFactory
{
    /// <summary>
    /// 创建负载均衡器
    /// </summary>
    /// <param name="strategy">负载均衡策略</param>
    /// <param name="options">选项</param>
    /// <returns>负载均衡器实例</returns>
    ILoadBalancer CreateLoadBalancer(LoadBalancingStrategy strategy, IReadOnlyDictionary<string, object>? options = null);

    /// <summary>
    /// 注册自定义负载均衡器
    /// </summary>
    /// <param name="name">策略名称</param>
    /// <param name="factory">负载均衡器工厂方法</param>
    void RegisterCustomStrategy(string name, Func<IReadOnlyDictionary<string, object>?, ILoadBalancer> factory);

    /// <summary>
    /// 支持的策略列表
    /// </summary>
    IReadOnlyList<LoadBalancingStrategy> SupportedStrategies { get; }
}
