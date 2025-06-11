using PulseServiceDiscovery.Abstractions.Models;

namespace PulseServiceDiscovery.Abstractions.Events;

/// <summary>
/// 服务健康状态变化事件
/// </summary>
public class ServiceHealthChangedEvent : ServiceEvent
{
    /// <summary>
    /// 服务ID
    /// </summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>
    /// 原健康状态
    /// </summary>
    public HealthStatus OldStatus { get; init; }

    /// <summary>
    /// 新健康状态
    /// </summary>
    public HealthStatus NewStatus { get; init; }

    /// <summary>
    /// 健康检查详情
    /// </summary>
    public string? HealthCheckDetails { get; init; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 检查持续时间
    /// </summary>
    public TimeSpan CheckDuration { get; init; }

    /// <summary>
    /// 创建健康状态变化事件
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="endpoint">服务端点</param>
    /// <param name="oldStatus">原健康状态</param>
    /// <param name="newStatus">新健康状态</param>
    /// <param name="healthCheckDetails">健康检查详情</param>
    /// <param name="checkDuration">检查持续时间</param>
    /// <param name="source">事件源</param>
    /// <returns>健康状态变化事件</returns>
    public static ServiceHealthChangedEvent Create(
        string serviceId,
        string serviceName,
        ServiceEndpoint endpoint,
        HealthStatus oldStatus,
        HealthStatus newStatus,
        string? healthCheckDetails = null,
        TimeSpan checkDuration = default,
        string source = "") => new()
    {
        ServiceId = serviceId,
        ServiceName = serviceName,
        Endpoint = endpoint,
        OldStatus = oldStatus,
        NewStatus = newStatus,
        HealthCheckDetails = healthCheckDetails,
        CheckDuration = checkDuration,
        Source = source
    };

    /// <summary>
    /// 检查是否从健康变为不健康
    /// </summary>
    public bool IsBecameUnhealthy => OldStatus == HealthStatus.Healthy && NewStatus != HealthStatus.Healthy;

    /// <summary>
    /// 检查是否从不健康变为健康
    /// </summary>
    public bool IsBecameHealthy => OldStatus != HealthStatus.Healthy && NewStatus == HealthStatus.Healthy;
}
