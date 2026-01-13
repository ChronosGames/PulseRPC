namespace PulseRPC.Server.Services.Management;

/// <summary>
/// 服务工厂配置选项
/// </summary>
/// <remarks>
/// <para>
/// 控制 <see cref="PulseServiceFactory{TService}"/> 的行为，包括缓存策略、清理策略和健康检查。
/// </para>
/// </remarks>
public class PulseServiceFactoryOptions
{
    /// <summary>
    /// 实例空闲超时时间
    /// </summary>
    /// <value>默认 5 分钟</value>
    /// <remarks>
    /// <para>
    /// 当实例在此时间内没有被访问，将被自动移除。
    /// </para>
    /// <para>
    /// <strong>调优建议</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>频繁访问的服务：10-30 分钟</description></item>
    /// <item><description>偶尔访问的服务：2-5 分钟</description></item>
    /// <item><description>内存敏感场景：1-2 分钟</description></item>
    /// </list>
    /// </remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 清理任务执行间隔
    /// </summary>
    /// <value>默认 1 分钟</value>
    /// <remarks>
    /// <para>
    /// 定期检查并清理空闲实例的间隔时间。
    /// </para>
    /// <para>
    /// <strong>调优建议</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>高负载场景：2-5 分钟（减少清理开销）</description></item>
    /// <item><description>低负载场景：30 秒 - 1 分钟（更及时释放资源）</description></item>
    /// </list>
    /// </remarks>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 最大缓存实例数
    /// </summary>
    /// <value>默认 10000</value>
    /// <remarks>
    /// <para>
    /// 当缓存实例数超过此值时，使用 LRU 策略驱逐最少使用的实例。
    /// </para>
    /// <para>
    /// <strong>内存估算</strong>：
    /// 每个 Entry 约 64 字节 + 服务实例大小。
    /// </para>
    /// <list type="bullet">
    /// <item><description>1,000 实例：约 64 KB（不含服务实例）</description></item>
    /// <item><description>10,000 实例：约 640 KB（不含服务实例）</description></item>
    /// <item><description>100,000 实例：约 6.4 MB（不含服务实例）</description></item>
    /// </list>
    /// <para>
    /// <strong>调优建议</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>根据实际服务实例大小和可用内存调整</description></item>
    /// <item><description>监控 EvictionCount 指标，如果过高则增加此值</description></item>
    /// </list>
    /// </remarks>
    public int MaxCachedInstances { get; set; } = 10000;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    /// <value>默认 true</value>
    /// <remarks>
    /// <para>
    /// 如果启用，将定期调用 <see cref="Abstractions.IUnifiedServiceHealthCheck.CheckHealthAsync"/> 检查实例健康状态。
    /// </para>
    /// <para>
    /// <strong>使用建议</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>生产环境：建议启用</description></item>
    /// <item><description>开发/测试环境：可以禁用以简化调试</description></item>
    /// </list>
    /// </remarks>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    /// <value>默认 30 秒</value>
    /// <remarks>
    /// <para>
    /// 定期执行健康检查的间隔时间。
    /// </para>
    /// <para>
    /// <strong>调优建议</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>关键服务：10-30 秒（快速发现问题）</description></item>
    /// <item><description>普通服务：30-60 秒</description></item>
    /// <item><description>低优先级服务：1-5 分钟</description></item>
    /// </list>
    /// <para>
    /// <strong>注意</strong>：间隔过短会增加 CPU 开销。
    /// </para>
    /// </remarks>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用指标收集
    /// </summary>
    /// <value>默认 true</value>
    /// <remarks>
    /// <para>
    /// 如果启用，工厂会收集缓存命中率、创建/移除次数等指标。
    /// </para>
    /// <para>
    /// <strong>性能影响</strong>：
    /// 指标收集使用原子操作，性能影响极小（&lt;1%）。
    /// </para>
    /// </remarks>
    public bool EnableMetrics { get; set; } = true;
}
