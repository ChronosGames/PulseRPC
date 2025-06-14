using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Monitoring.Metrics;
using PulseRPC.Monitoring.Performance;
using PulseRPC.ServiceDiscovery;

namespace PulseRPC;

/// <summary>
/// 监控系统依赖注入扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 启用服务过期清理
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="expiration">过期时间（可选）</param>
    /// <param name="interval">清理间隔（可选）</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection EnableServiceExpiration(
        this IServiceCollection services,
        TimeSpan? expiration = null,
        TimeSpan? interval = null)
    {
        services.Configure<CleanupOptions>(options =>
        {
            options.Enabled = true;

            if (expiration.HasValue)
            {
                options.ServiceExpiration = expiration.Value;
            }

            if (interval.HasValue)
            {
                options.Interval = interval.Value;
            }
        });

        return services;
    }

    /// <summary>
    /// 配置性能警报
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="cpuThreshold">CPU使用率阈值</param>
    /// <param name="memoryThreshold">内存使用率阈值</param>
    /// <param name="gcThreshold">GC频率阈值</param>
    /// <param name="errorRateThreshold">错误率阈值</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigurePerformanceAlerts(
        this IServiceCollection services,
        double cpuThreshold = 80.0,
        double memoryThreshold = 80.0,
        double gcThreshold = 10.0,
        double errorRateThreshold = 5.0)
    {
        services.Configure<PerformanceMonitorOptions>(options =>
        {
            options.EnableAlerts = true;
            options.CpuUsageAlertThreshold = cpuThreshold;
            options.MemoryUsageAlertThreshold = memoryThreshold;
            options.GcFrequencyAlertThreshold = gcThreshold;
            options.ErrorRateAlertThreshold = errorRateThreshold;
        });

        return services;
    }

    /// <summary>
    /// 启用详细指标收集
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection EnableDetailedMetrics(this IServiceCollection services)
    {
        services.Configure<PerformanceMonitorOptions>(options =>
        {
            options.EnableDetailedMetrics = true;
        });

        services.Configure<MetricsOptions>(options =>
        {
            options.CollectDetailedRpcMetrics = true;
            options.CollectLoadBalancingMetrics = true;
            options.CollectServiceDiscoveryMetrics = true;
            options.CollectHealthCheckMetrics = true;
            options.CollectConnectionPoolMetrics = true;
        });

        return services;
    }

    /// <summary>
    /// 配置指标保留策略
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="retentionTime">保留时间</param>
    /// <param name="maxCount">最大数量</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureMetricsRetention(
        this IServiceCollection services,
        TimeSpan retentionTime,
        int maxCount = 10000)
    {
        services.Configure<PerformanceMonitorOptions>(options =>
        {
            options.MetricsRetentionTime = retentionTime;
            options.MaxMetricsCount = maxCount;
        });

        return services;
    }

    /// <summary>
    /// 配置采样间隔
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="interval">采样间隔</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureSamplingInterval(
        this IServiceCollection services,
        TimeSpan interval)
    {
        services.Configure<PerformanceMonitorOptions>(options =>
        {
            options.SamplingInterval = interval;
        });

        return services;
    }

    /// <summary>
    /// 获取指标收集器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>指标收集器</returns>
    public static IMetricsCollector GetMetricsCollector(this IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IMetricsCollector>();
    }
}

