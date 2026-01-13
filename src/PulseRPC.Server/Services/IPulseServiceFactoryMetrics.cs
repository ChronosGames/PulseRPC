namespace PulseRPC.Server.Services;

/// <summary>
/// 服务工厂指标接口
/// </summary>
/// <remarks>
/// <para>
/// 提供 <see cref="IPulseServiceFactory{TService}"/> 的运行时指标，用于监控和性能分析。
/// </para>
/// <para>
/// <strong>典型使用场景</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>性能监控：跟踪缓存命中率、实例数量等</description></item>
/// <item><description>容量规划：根据实例数量和驱逐次数调整配置</description></item>
/// <item><description>告警触发：当缓存命中率过低或驱逐次数过高时发出告警</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var metrics = serviceProvider.GetRequiredService&lt;IPulseServiceFactoryMetrics&gt;();
///
/// Console.WriteLine($"Active Instances: {metrics.ActiveInstances}");
/// Console.WriteLine($"Cache Hit Rate: {metrics.CacheHitRate:P2}");
/// Console.WriteLine($"Total Created: {metrics.TotalCreated}");
/// Console.WriteLine($"Total Removed: {metrics.TotalRemoved}");
/// </code>
/// </example>
public interface IPulseServiceFactoryMetrics
{
    /// <summary>
    /// 当前活跃实例数
    /// </summary>
    /// <value>当前缓存中的服务实例数量</value>
    /// <remarks>
    /// <para>
    /// 表示当前在工厂缓存中的服务实例数量。
    /// </para>
    /// <para>
    /// <strong>监控建议</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>如果持续接近 MaxCachedInstances，考虑增加缓存大小</description></item>
    /// <item><description>如果长期很低，考虑减少 IdleTimeout 以更快释放资源</description></item>
    /// </list>
    /// </remarks>
    int ActiveInstances { get; }

    /// <summary>
    /// 总创建次数
    /// </summary>
    /// <value>自工厂启动以来创建的服务实例总数</value>
    /// <remarks>
    /// <para>
    /// 包括所有成功创建的实例，包括已被移除的实例。
    /// </para>
    /// <para>
    /// <strong>分析用途</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>结合 TotalRemoved 计算实例周转率</description></item>
    /// <item><description>结合 CacheMisses 计算创建成功率</description></item>
    /// </list>
    /// </remarks>
    long TotalCreated { get; }

    /// <summary>
    /// 总移除次数
    /// </summary>
    /// <value>自工厂启动以来移除的服务实例总数</value>
    /// <remarks>
    /// <para>
    /// 包括所有被移除的实例，触发原因包括：
    /// </para>
    /// <list type="bullet">
    /// <item><description>空闲超时</description></item>
    /// <item><description>健康检查失败</description></item>
    /// <item><description>LRU 驱逐</description></item>
    /// <item><description>手动移除</description></item>
    /// </list>
    /// </remarks>
    long TotalRemoved { get; }

    /// <summary>
    /// 缓存命中次数
    /// </summary>
    /// <value>GetOrCreateAsync 命中缓存的次数</value>
    /// <remarks>
    /// <para>
    /// 当调用 <see cref="IPulseServiceFactory{TService}.GetOrCreateAsync"/> 时实例已存在于缓存中的次数。
    /// </para>
    /// </remarks>
    long CacheHits { get; }

    /// <summary>
    /// 缓存未命中次数
    /// </summary>
    /// <value>GetOrCreateAsync 未命中缓存的次数</value>
    /// <remarks>
    /// <para>
    /// 当调用 <see cref="IPulseServiceFactory{TService}.GetOrCreateAsync"/> 时需要创建新实例的次数。
    /// </para>
    /// </remarks>
    long CacheMisses { get; }

    /// <summary>
    /// 缓存命中率
    /// </summary>
    /// <value>缓存命中率（0.0 到 1.0 之间）</value>
    /// <remarks>
    /// <para>
    /// 计算公式：CacheHits / (CacheHits + CacheMisses)
    /// </para>
    /// <para>
    /// <strong>性能指标</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>&gt;95%：非常好，缓存策略有效</description></item>
    /// <item><description>80%-95%：良好，可以考虑优化</description></item>
    /// <item><description>&lt;80%：需要调整 IdleTimeout 或 MaxCachedInstances</description></item>
    /// </list>
    /// </remarks>
    double CacheHitRate { get; }

    /// <summary>
    /// 驱逐次数
    /// </summary>
    /// <value>由于 LRU 策略被驱逐的实例数量</value>
    /// <remarks>
    /// <para>
    /// 当缓存满时（达到 MaxCachedInstances），根据 LRU 策略驱逐最少使用的实例。
    /// </para>
    /// <para>
    /// <strong>优化建议</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>驱逐次数过高：增加 MaxCachedInstances</description></item>
    /// <item><description>驱逐次数为 0：可能 MaxCachedInstances 设置过大，浪费内存</description></item>
    /// </list>
    /// </remarks>
    long EvictionCount { get; }
}
