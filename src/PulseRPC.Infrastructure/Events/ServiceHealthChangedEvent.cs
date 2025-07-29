using PulseRPC.HealthCheck;
using PulseRPC.ServiceDiscovery;

namespace PulseRPC.Infrastructure;

/// <summary>
/// 服务健康状态变化事件
/// </summary>
public class ServiceHealthChangedEvent : ServiceEvent
{
    /// <summary>
    /// 之前的健康状态
    /// </summary>
    public HealthStatus PreviousHealth { get; init; }

    /// <summary>
    /// 当前的健康状态
    /// </summary>
    public HealthStatus CurrentHealth { get; init; }

    /// <summary>
    /// 健康检查结果详情
    /// </summary>
    public string? HealthDetails { get; init; }

    /// <summary>
    /// 创建健康状态变化事件
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="previousHealth">之前的健康状态</param>
    /// <param name="currentHealth">当前的健康状态</param>
    /// <param name="healthDetails">健康检查详情</param>
    /// <param name="source">事件源</param>
    /// <returns>健康状态变化事件</returns>
    public static ServiceHealthChangedEvent Create(
        PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint,
        HealthStatus previousHealth,
        HealthStatus currentHealth,
        string? healthDetails = null,
        string source = "") => new()
    {
        Endpoint = endpoint,
        PreviousHealth = previousHealth,
        CurrentHealth = currentHealth,
        HealthDetails = healthDetails,
        Source = source
    };

    /// <summary>
    /// 检查健康状态是否发生了实质性变化
    /// </summary>
    public bool HasSignificantChange => PreviousHealth != CurrentHealth;

    /// <summary>
    /// 检查是否从健康变为不健康
    /// </summary>
    public bool IsBecomingUnhealthy =>
        PreviousHealth == HealthStatus.Healthy && CurrentHealth != HealthStatus.Healthy;

    /// <summary>
    /// 检查是否从不健康变为健康
    /// </summary>
    public bool IsBecomingHealthy =>
        PreviousHealth != HealthStatus.Healthy && CurrentHealth == HealthStatus.Healthy;
}
