using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Models;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 熔断器策略,实现 4 状态健康状态机
/// </summary>
/// <remarks>
/// <para>
/// 实现服务实例健康状态的自动转换逻辑,基于请求成功/失败情况:
/// </para>
/// <code>
/// Healthy → Isolated (连续超时 >= FailureThreshold)
/// Isolated → CoolingDown (冷却期过期)
/// CoolingDown → ProbeAllowed (自动转换)
/// ProbeAllowed → Healthy (探测成功率 >= ProbeSuccessThreshold/ProbeRequestLimit)
/// ProbeAllowed → Isolated (探测失败)
/// </code>
/// <para>
/// <strong>状态说明</strong>:
/// </para>
/// <list type="bullet">
/// <item><description><strong>Healthy</strong>: 正常运行,接受所有请求</description></item>
/// <item><description><strong>Isolated</strong>: 已隔离,拒绝所有请求,等待冷却期</description></item>
/// <item><description><strong>CoolingDown</strong>: 冷却期中,仍拒绝请求,等待探测窗口</description></item>
/// <item><description><strong>ProbeAllowed</strong>: 允许有限探测请求,测试是否恢复</description></item>
/// </list>
/// </remarks>
public sealed class CircuitBreakerPolicy
{
    private readonly HealthMonitorOptions _options;

    /// <summary>
    /// 创建熔断器策略实例
    /// </summary>
    /// <param name="options">健康监控配置选项</param>
    public CircuitBreakerPolicy(IOptions<HealthMonitorOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <summary>
    /// 评估并执行健康状态转换
    /// </summary>
    /// <param name="health">当前健康状态记录</param>
    /// <param name="requestSucceeded">最近一次请求是否成功</param>
    /// <param name="now">当前时间 (UTC)</param>
    /// <returns>如果状态发生转换返回 true,否则返回 false</returns>
    public bool EvaluateTransition(ServiceInstanceHealth health, bool requestSucceeded, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(health);

        var previousState = health.State;

        switch (health.State)
        {
            case HealthState.Healthy:
                EvaluateHealthyState(health, requestSucceeded);
                break;

            case HealthState.Isolated:
                EvaluateIsolatedState(health, now);
                break;

            case HealthState.CoolingDown:
                EvaluateCoolingDownState(health, now);
                break;

            case HealthState.ProbeAllowed:
                EvaluateProbeAllowedState(health, requestSucceeded);
                break;
        }

        return previousState != health.State;
    }

    /// <summary>
    /// 评估 Healthy 状态,检查是否需要转换到 Isolated
    /// </summary>
    private void EvaluateHealthyState(ServiceInstanceHealth health, bool requestSucceeded)
    {
        if (requestSucceeded)
        {
            // 成功请求,重置连续超时计数
            health.ConsecutiveTimeouts = 0;
        }
        else
        {
            // 失败请求,增加连续超时计数
            health.ConsecutiveTimeouts++;

            // 如果达到阈值,转换到 Isolated 状态
            if (health.ConsecutiveTimeouts >= _options.FailureThreshold)
            {
                health.State = HealthState.Isolated;
                health.CoolingPeriodExpiresUtc = DateTime.UtcNow.Add(_options.CoolingPeriod);
            }
        }
    }

    /// <summary>
    /// 评估 Isolated 状态,检查冷却期是否过期
    /// </summary>
    private void EvaluateIsolatedState(ServiceInstanceHealth health, DateTime now)
    {
        // 检查冷却期是否过期
        if (health.CoolingPeriodExpiresUtc.HasValue && now >= health.CoolingPeriodExpiresUtc.Value)
        {
            // 冷却期过期,转换到 CoolingDown 状态
            health.State = HealthState.CoolingDown;
            health.CoolingPeriodExpiresUtc = null;
        }
    }

    /// <summary>
    /// 评估 CoolingDown 状态,自动转换到 ProbeAllowed
    /// </summary>
    private void EvaluateCoolingDownState(ServiceInstanceHealth health, DateTime now)
    {
        // 立即转换到 ProbeAllowed 状态,允许探测请求
        health.State = HealthState.ProbeAllowed;
        health.ProbeRequestsAllowed = _options.ProbeRequestLimit;
        health.ProbeSuccessCount = 0;
    }

    /// <summary>
    /// 评估 ProbeAllowed 状态,检查是否恢复或重新隔离
    /// </summary>
    private void EvaluateProbeAllowedState(ServiceInstanceHealth health, bool requestSucceeded)
    {
        // 减少剩余探测请求数
        health.ProbeRequestsAllowed--;

        if (requestSucceeded)
        {
            // 成功探测,增加成功计数
            health.ProbeSuccessCount++;
            health.ConsecutiveTimeouts = 0;

            // 检查是否达到恢复阈值
            if (health.ProbeSuccessCount >= _options.ProbeSuccessThreshold)
            {
                // 恢复到 Healthy 状态
                health.State = HealthState.Healthy;
                health.ProbeRequestsAllowed = 0;
                health.ProbeSuccessCount = 0;
            }
        }
        else
        {
            // 失败探测,增加连续超时计数
            health.ConsecutiveTimeouts++;
        }

        // 如果探测窗口用尽,根据成功率决定
        if (health.ProbeRequestsAllowed <= 0)
        {
            if (health.ProbeSuccessCount >= _options.ProbeSuccessThreshold)
            {
                // 探测成功率足够,恢复到 Healthy
                health.State = HealthState.Healthy;
            }
            else
            {
                // 探测失败,重新隔离
                health.State = HealthState.Isolated;
                health.CoolingPeriodExpiresUtc = DateTime.UtcNow.Add(_options.CoolingPeriod);
            }

            health.ProbeRequestsAllowed = 0;
            health.ProbeSuccessCount = 0;
        }
    }
}
