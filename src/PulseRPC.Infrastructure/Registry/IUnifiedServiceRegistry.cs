using PulseRPC.HealthCheck;
using PulseRPC.LoadBalancing;

namespace PulseRPC.Infrastructure.Registry;

/// <summary>
/// 统一的服务注册与发现接口
/// </summary>
public interface IUnifiedServiceRegistry
{
    #region 服务注册 (服务端使用)

    /// <summary>
    /// 注册服务实例
    /// </summary>
    Task RegisterServiceAsync(ServiceEndpoint serviceEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册通道端点
    /// </summary>
    Task RegisterChannelAsync(ChannelEndpoint channelEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销服务
    /// </summary>
    Task UnregisterServiceAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销通道
    /// </summary>
    Task UnregisterChannelAsync(string channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新服务健康状态
    /// </summary>
    Task UpdateServiceHealthAsync(string serviceId, HealthStatus health, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量更新服务健康状态
    /// </summary>
    Task UpdateServicesHealthAsync(Dictionary<string, HealthStatus> healthUpdates, CancellationToken cancellationToken = default);

    #endregion

    #region 服务发现 (客户端使用)

    /// <summary>
    /// 发现服务实例（按服务类型）
    /// </summary>
    Task<IReadOnlyList<ServiceEndpoint>> DiscoverServicesAsync(string serviceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发现服务实例（按标签过滤）
    /// </summary>
    Task<IReadOnlyList<ServiceEndpoint>> DiscoverServicesByTagsAsync(
        string serviceType,
        Dictionary<string, string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发现健康的服务实例
    /// </summary>
    Task<IReadOnlyList<ServiceEndpoint>> DiscoverHealthyServicesAsync(string serviceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取特定服务实例
    /// </summary>
    Task<ServiceEndpoint?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发现通道端点（按通道名称）
    /// </summary>
    Task<IReadOnlyList<ChannelEndpoint>> DiscoverChannelsAsync(string channelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取特定通道端点
    /// </summary>
    Task<ChannelEndpoint?> GetChannelAsync(string channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有已注册的服务类型
    /// </summary>
    Task<IReadOnlyList<string>> GetServiceTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有已注册的通道名称
    /// </summary>
    Task<IReadOnlyList<string>> GetChannelNamesAsync(CancellationToken cancellationToken = default);

    #endregion

    #region 负载均衡集成

    /// <summary>
    /// 选择最佳服务实例（结合负载均衡）
    /// </summary>
    Task<ServiceEndpoint?> SelectServiceAsync(
        string serviceType,
        LoadBalancingContext context,
        LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 选择最佳通道端点（结合负载均衡）
    /// </summary>
    Task<ChannelEndpoint?> SelectChannelAsync(
        string channelName,
        LoadBalancingContext context,
        LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin,
        CancellationToken cancellationToken = default);

    #endregion

    #region 事件通知

    /// <summary>
    /// 服务注册事件
    /// </summary>
    event Func<ServiceRegisteredEvent, Task>? ServiceRegistered;

    /// <summary>
    /// 服务注销事件
    /// </summary>
    event Func<ServiceUnregisteredEvent, Task>? ServiceUnregistered;

    /// <summary>
    /// 服务健康变化事件
    /// </summary>
    event Func<ServiceHealthChangedEvent, Task>? ServiceHealthChanged;

    /// <summary>
    /// 通道状态变化事件
    /// </summary>
    event Func<ChannelStateChangedEvent, Task>? ChannelStateChanged;

    #endregion

    #region 管理功能

    /// <summary>
    /// 获取注册统计信息
    /// </summary>
    Task<RegistryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理过期服务
    /// </summary>
    Task CleanupExpiredServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 健康检查所有服务
    /// </summary>
    Task PerformHealthCheckAsync(CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// 通道状态变化事件
/// </summary>
public class ChannelStateChangedEvent
{
    /// <summary>
    /// 通道端点
    /// </summary>
    public required ChannelEndpoint Channel { get; init; }

    /// <summary>
    /// 之前的状态
    /// </summary>
    public ChannelState PreviousState { get; init; }

    /// <summary>
    /// 当前的状态
    /// </summary>
    public ChannelState CurrentState { get; init; }

    /// <summary>
    /// 事件时间戳
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 事件源
    /// </summary>
    public string Source { get; init; } = string.Empty;
}

/// <summary>
/// 通道状态
/// </summary>
public enum ChannelState
{
    /// <summary>未知</summary>
    Unknown,
    /// <summary>活跃</summary>
    Active,
    /// <summary>不活跃</summary>
    Inactive,
    /// <summary>连接中</summary>
    Connecting,
    /// <summary>断开连接</summary>
    Disconnected,
    /// <summary>错误状态</summary>
    Error
}

/// <summary>
/// 注册统计信息
/// </summary>
public class RegistryStatistics
{
    /// <summary>
    /// 注册的服务总数
    /// </summary>
    public int TotalServices { get; init; }

    /// <summary>
    /// 健康的服务数量
    /// </summary>
    public int HealthyServices { get; init; }

    /// <summary>
    /// 不健康的服务数量
    /// </summary>
    public int UnhealthyServices { get; init; }

    /// <summary>
    /// 注册的通道总数
    /// </summary>
    public int TotalChannels { get; init; }

    /// <summary>
    /// 活跃的通道数量
    /// </summary>
    public int ActiveChannels { get; init; }

    /// <summary>
    /// 服务类型数量
    /// </summary>
    public int ServiceTypeCount { get; init; }

    /// <summary>
    /// 按服务类型分组的统计
    /// </summary>
    public Dictionary<string, int> ServicesByType { get; init; } = new();

    /// <summary>
    /// 按通道名称分组的统计
    /// </summary>
    public Dictionary<string, int> ServicesByChannel { get; init; } = new();

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}
