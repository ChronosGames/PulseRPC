namespace PulseServiceDiscovery.Abstractions.Models;

/// <summary>
/// 健康状态枚举
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 健康状态
    /// </summary>
    Healthy = 1,

    /// <summary>
    /// 不健康状态
    /// </summary>
    Unhealthy = 2,

    /// <summary>
    /// 降级状态（可用但性能受限）
    /// </summary>
    Degraded = 3,

    /// <summary>
    /// 维护状态
    /// </summary>
    Maintenance = 4,

    /// <summary>
    /// 离线状态
    /// </summary>
    Offline = 5
}
