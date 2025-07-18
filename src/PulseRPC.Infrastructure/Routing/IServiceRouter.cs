namespace PulseRPC.Cluster.Routing;

/// <summary>
/// 服务路由器 - 结合服务发现和负载均衡
/// </summary>
public interface IServiceRouter
{
    /// <summary>
    /// 路由到最佳服务实例
    /// </summary>
    Task<ServiceEndpoint?> RouteToServiceAsync(
        string serviceType,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 路由到最佳通道
    /// </summary>
    Task<ChannelEndpoint?> RouteToChannelAsync(
        string channelName,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量路由（用于多个并发请求）
    /// </summary>
    Task<IReadOnlyList<ServiceEndpoint>> RouteToMultipleServicesAsync(
        string serviceType,
        int count,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 路由上下文
/// </summary>
public class RoutingContext
{
    /// <summary>
    /// 路由偏好设置
    /// </summary>
    public RoutingPreferences? Preferences { get; init; }

    /// <summary>
    /// 服务标签过滤
    /// </summary>
    public Dictionary<string, string>? ServiceTags { get; init; }

    /// <summary>
    /// 首选通道名称
    /// </summary>
    public string? PreferredChannel { get; init; }

    /// <summary>
    /// 排除的服务ID列表
    /// </summary>
    public HashSet<string>? ExcludedServices { get; init; }

    /// <summary>
    /// 排除的通道ID列表
    /// </summary>
    public HashSet<string>? ExcludedChannels { get; init; }

    /// <summary>
    /// 请求ID（用于跟踪和调试）
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// 自定义路由属性
    /// </summary>
    public Dictionary<string, object>? Properties { get; init; }
}

/// <summary>
/// 路由偏好设置
/// </summary>
public class RoutingPreferences
{
    /// <summary>
    /// 优先的传输协议
    /// </summary>
    public TransportProtocol? PreferredProtocol { get; init; }

    /// <summary>
    /// 是否要求 TLS
    /// </summary>
    public bool RequireTls { get; init; } = false;

    /// <summary>
    /// 最大延迟要求（毫秒）
    /// </summary>
    public int? MaxLatencyMs { get; init; }

    /// <summary>
    /// 地理位置偏好
    /// </summary>
    public string? PreferredRegion { get; init; }

    /// <summary>
    /// 负载均衡策略偏好
    /// </summary>
    public LoadBalancing.LoadBalancingStrategy? PreferredLoadBalancing { get; init; }

    /// <summary>
    /// 是否只选择健康的服务
    /// </summary>
    public bool HealthyOnly { get; init; } = true;

    /// <summary>
    /// 最小权重要求
    /// </summary>
    public int? MinWeight { get; init; }

    /// <summary>
    /// 服务版本偏好
    /// </summary>
    public string? PreferredVersion { get; init; }
}
