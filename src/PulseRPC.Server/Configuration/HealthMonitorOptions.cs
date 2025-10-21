namespace PulseRPC.Server.Configuration;

/// <summary>
/// 服务实例健康监控配置选项
/// </summary>
/// <remarks>
/// 用于配置熔断器策略的隔离阈值、冷却期、探测窗口等参数。
/// 这些配置影响服务实例故障检测的灵敏度和恢复策略。
/// </remarks>
public sealed class HealthMonitorOptions
{
    /// <summary>
    /// 触发隔离的连续失败阈值
    /// </summary>
    /// <remarks>
    /// <para>默认值：3 次连续超时</para>
    /// <para>有效范围：1 到 10</para>
    /// <para>
    /// 调优建议：
    /// - 提高灵敏度（快速隔离故障）：设置为 2
    /// - 降低灵敏度（减少误报）：设置为 5
    /// - 生产环境推荐：3（平衡误报率和响应速度）
    /// </para>
    /// </remarks>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 隔离后的冷却期时长
    /// </summary>
    /// <remarks>
    /// <para>默认值：1 分钟</para>
    /// <para>
    /// 服务实例被隔离后，需等待此时长才允许探测请求测试恢复。
    /// 冷却期用于避免立即重试加重故障服务负担。
    /// </para>
    /// <para>
    /// 调优建议：
    /// - 快速恢复场景：30 秒
    /// - 数据库连接池故障：2-5 分钟（等待连接池恢复）
    /// - 外部服务依赖故障：1 分钟（默认）
    /// </para>
    /// </remarks>
    public TimeSpan CoolingPeriod { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 探测窗口允许的最大探测请求数
    /// </summary>
    /// <remarks>
    /// <para>默认值：5 次</para>
    /// <para>有效范围：3 到 20</para>
    /// <para>
    /// 冷却期结束后，允许此数量的请求用于测试服务实例是否恢复。
    /// 探测期间的成功率达到 ProbeSuccessThreshold 则恢复为 Healthy 状态。
    /// </para>
    /// </remarks>
    public int ProbeRequestLimit { get; set; } = 5;

    /// <summary>
    /// 恢复为 Healthy 状态所需的探测成功次数阈值
    /// </summary>
    /// <remarks>
    /// <para>默认值：3 次（即 3/5 成功率 = 60%）</para>
    /// <para>有效范围：1 到 ProbeRequestLimit</para>
    /// <para>
    /// 调优建议：
    /// - 宽松恢复策略：2/5 (40%)
    /// - 严格恢复策略：4/5 (80%)
    /// - 推荐：3/5 (60%) 平衡误判和快速恢复
    /// </para>
    /// </remarks>
    public int ProbeSuccessThreshold { get; set; } = 3;

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">当配置参数超出有效范围时抛出</exception>
    public void Validate()
    {
        if (FailureThreshold < 1 || FailureThreshold > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FailureThreshold),
                FailureThreshold,
                "失败阈值必须在 1 到 10 之间");
        }

        if (CoolingPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CoolingPeriod),
                CoolingPeriod,
                "冷却期不能为负数");
        }

        if (ProbeRequestLimit < 3 || ProbeRequestLimit > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ProbeRequestLimit),
                ProbeRequestLimit,
                "探测请求限制必须在 3 到 20 之间");
        }

        if (ProbeSuccessThreshold < 1 || ProbeSuccessThreshold > ProbeRequestLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ProbeSuccessThreshold),
                ProbeSuccessThreshold,
                $"探测成功阈值必须在 1 到 {ProbeRequestLimit} 之间");
        }
    }
}
