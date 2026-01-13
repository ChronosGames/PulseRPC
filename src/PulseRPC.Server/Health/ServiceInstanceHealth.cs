using PulseRPC.Scheduling;

namespace PulseRPC.Server.Health;

/// <summary>
/// 服务实例的健康状态记录
/// </summary>
/// <remarks>
/// 跟踪服务实例的请求统计、健康状态和探测窗口信息，
/// 用于熔断器策略决策和诊断端点查询。
/// </remarks>
public sealed class ServiceInstanceHealth
{
    /// <summary>
    /// 服务调度键（ServiceName + ServiceId）
    /// </summary>
    public required ServiceSchedulingKey Key { get; init; }

    /// <summary>
    /// 当前健康状态
    /// </summary>
    public HealthState State { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 连续超时次数（用于触发隔离）
    /// </summary>
    public int ConsecutiveTimeouts { get; set; }

    /// <summary>
    /// 冷却期过期时间（UTC），仅在 Isolated 状态时有效
    /// </summary>
    public DateTime? CoolingPeriodExpiresUtc { get; set; }

    /// <summary>
    /// 探测窗口剩余允许的探测请求数
    /// </summary>
    public int ProbeRequestsAllowed { get; set; }

    /// <summary>
    /// 探测窗口中的成功请求数
    /// </summary>
    public int ProbeSuccessCount { get; set; }

    /// <summary>
    /// 最后活动时间（UTC）
    /// </summary>
    public DateTime LastActivityUtc { get; set; }

    /// <summary>
    /// 成功率（0.0 到 1.0）
    /// </summary>
    public double SuccessRate => TotalRequests > 0
        ? (double)SuccessfulRequests / TotalRequests
        : 0.0;

    /// <summary>
    /// 判断服务实例是否可以接受新请求
    /// </summary>
    /// <returns>
    /// 如果状态为 Healthy 返回 true；
    /// 如果状态为 ProbeAllowed 且剩余探测次数 > 0 返回 true；
    /// 否则返回 false
    /// </returns>
    public bool CanAcceptRequests()
    {
        return State switch
        {
            HealthState.Healthy => true,
            HealthState.ProbeAllowed => ProbeRequestsAllowed > 0,
            HealthState.Isolated => false,
            HealthState.CoolingDown => false,
            _ => false
        };
    }

    /// <summary>
    /// 重置健康状态到 Healthy
    /// </summary>
    /// <remarks>
    /// 用于手动恢复隔离的服务实例（诊断端点调用）
    /// </remarks>
    public void Reset()
    {
        State = HealthState.Healthy;
        ConsecutiveTimeouts = 0;
        CoolingPeriodExpiresUtc = null;
        ProbeRequestsAllowed = 0;
        ProbeSuccessCount = 0;
    }
}
