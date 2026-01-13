namespace PulseRPC.Server.Health;

/// <summary>
/// 服务实例的健康状态枚举
/// </summary>
/// <remarks>
/// 实现 4 状态熔断器模式，用于服务实例的故障隔离和自动恢复：
/// <list type="bullet">
/// <item><description>Healthy: 正常运行状态，接受所有请求</description></item>
/// <item><description>Isolated: 已隔离状态，拒绝所有请求（连续超时触发）</description></item>
/// <item><description>CoolingDown: 冷却期状态，等待探测窗口（隔离后进入）</description></item>
/// <item><description>ProbeAllowed: 探测允许状态，接受有限探测请求测试恢复（冷却期结束后进入）</description></item>
/// </list>
/// </remarks>
public enum HealthState
{
    /// <summary>
    /// 健康状态 - 服务实例正常运行，接受所有请求
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// 隔离状态 - 服务实例已被隔离，拒绝所有新请求
    /// </summary>
    /// <remarks>
    /// 当连续超时次数达到阈值（默认 3 次）时自动进入此状态
    /// </remarks>
    Isolated = 1,

    /// <summary>
    /// 冷却期状态 - 服务实例处于冷却期，等待探测窗口开启
    /// </summary>
    /// <remarks>
    /// 从 Isolated 状态经过冷却期（默认 1 分钟）后自动进入此状态
    /// </remarks>
    CoolingDown = 2,

    /// <summary>
    /// 探测允许状态 - 服务实例允许有限的探测请求测试恢复
    /// </summary>
    /// <remarks>
    /// 从 CoolingDown 状态自动进入，允许少量请求（默认 5 次）测试恢复。
    /// 如果探测请求成功率达到阈值（默认 3/5）则恢复到 Healthy 状态，
    /// 否则重新进入 Isolated 状态。
    /// </remarks>
    ProbeAllowed = 3
}
