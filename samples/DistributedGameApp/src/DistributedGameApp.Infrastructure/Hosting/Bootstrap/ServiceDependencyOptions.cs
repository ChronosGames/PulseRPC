using DistributedGameApp.Infrastructure.ServiceClient;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 服务依赖配置选项
/// </summary>
/// <remarks>
/// <para>
/// 定义当前服务可能调用的其他服务类型。所有服务类型都是"按需连接"的：
/// - 启动时只注册服务类型，不等待服务就绪
/// - 运行时调用时，通过 GetServer/GetHub 的重试机制自动建立连接
/// </para>
/// <para>
/// 这种设计允许服务按任意顺序启动，只要在实际发送请求前目标服务启动即可。
/// </para>
/// </remarks>
public class ServiceDependencyOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "ServiceDependency";

    /// <summary>
    /// 需要调用的服务类型列表
    /// </summary>
    /// <remarks>
    /// 在启动时注册这些服务类型到 UnifiedServiceClientManager，
    /// 但不等待它们就绪。运行时调用时按需建立连接。
    /// </remarks>
    public ServerType[] ServerTypes { get; set; } = [];

    /// <summary>
    /// 路由策略（默认一致性哈希）
    /// </summary>
    public RoutingStrategy RoutingStrategy { get; set; } = RoutingStrategy.ConsistentHash;

    /// <summary>
    /// 运行时获取连接的最大等待时间（默认 10 秒）
    /// </summary>
    /// <remarks>
    /// 当调用 GetServer/GetHub 时，如果目标服务不可用，
    /// 会在此时间内重试直到获取到连接或超时。
    /// </remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 运行时获取连接的重试间隔（默认 500ms）
    /// </summary>
    public TimeSpan RequestRetryInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}
