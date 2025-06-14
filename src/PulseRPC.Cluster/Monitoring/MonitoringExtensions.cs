using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PulseRPC.Monitoring.Metrics;
using PulseRPC.Monitoring.Performance;

namespace PulseRPC.Monitoring;

/// <summary>
/// 监控扩展方法
/// </summary>
public static class MonitoringExtensions
{
    /// <summary>
    /// 添加 PulseRPC 监控
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcMonitoring(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册配置选项
        services.Configure<MonitoringOptions>(configuration.GetSection("PulseRPC:Monitoring"));
        services.Configure<PerformanceMonitorOptions>(configuration.GetSection("PulseRPC:Monitoring:Performance"));
        services.Configure<MetricsOptions>(configuration.GetSection("PulseRPC:Monitoring:Metrics"));

        return AddMonitoringCore(services);
    }

    /// <summary>
    /// 获取性能监控器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>性能监控器</returns>
    public static PerformanceMonitor? GetPerformanceMonitor(this IServiceProvider serviceProvider)
    {
        return serviceProvider.GetServices<IHostedService>()
            .OfType<PerformanceMonitor>()
            .FirstOrDefault();
    }

    /// <summary>
    /// 添加监控核心服务
    /// </summary>
    private static IServiceCollection AddMonitoringCore(IServiceCollection services)
    {
        // 注册指标收集器
        services.TryAddSingleton<IMetricsCollector, MetricsCollector>();

        // 注册性能监控器作为后台服务
        services.AddHostedService<PerformanceMonitor>();

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 监控 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcMonitoring(
        this IServiceCollection services,
        Action<MonitoringOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // 提取性能监控配置
        var monitoringOptions = new MonitoringOptions();
        configureOptions(monitoringOptions);

        services.Configure<PerformanceMonitorOptions>(options =>
        {
            var perfOptions = monitoringOptions.Performance;
            options.Enabled = perfOptions.Enabled;
            options.SamplingInterval = perfOptions.SamplingInterval;
            options.EnableDetailedMetrics = perfOptions.EnableDetailedMetrics;
            options.CollectSystemMetrics = perfOptions.CollectSystemMetrics;
            options.CollectProcessMetrics = perfOptions.CollectProcessMetrics;
            options.CollectGcMetrics = perfOptions.CollectGcMetrics;
            options.CollectThreadPoolMetrics = perfOptions.CollectThreadPoolMetrics;
            options.MetricsRetentionTime = perfOptions.MetricsRetentionTime;
            options.MaxMetricsCount = perfOptions.MaxMetricsCount;
            options.EnableMetricsExport = perfOptions.EnableMetricsExport;
            options.MetricsExportInterval = perfOptions.MetricsExportInterval;
            options.MetricsExportEndpoint = perfOptions.MetricsExportEndpoint;
            options.EnableAlerts = perfOptions.EnableAlerts;
            options.CpuUsageAlertThreshold = perfOptions.CpuUsageAlertThreshold;
            options.MemoryUsageAlertThreshold = perfOptions.MemoryUsageAlertThreshold;
            options.GcFrequencyAlertThreshold = perfOptions.GcFrequencyAlertThreshold;
            options.ErrorRateAlertThreshold = perfOptions.ErrorRateAlertThreshold;
        });

        services.Configure<MetricsOptions>(options =>
        {
            var metricsOptions = monitoringOptions.Metrics;
            options.Enabled = metricsOptions.Enabled;
            options.DefaultHistogramBuckets = metricsOptions.DefaultHistogramBuckets;
            options.CollectDetailedRpcMetrics = metricsOptions.CollectDetailedRpcMetrics;
            options.CollectLoadBalancingMetrics = metricsOptions.CollectLoadBalancingMetrics;
            options.CollectServiceDiscoveryMetrics = metricsOptions.CollectServiceDiscoveryMetrics;
            options.CollectHealthCheckMetrics = metricsOptions.CollectHealthCheckMetrics;
            options.CollectConnectionPoolMetrics = metricsOptions.CollectConnectionPoolMetrics;
            options.TagFilters = metricsOptions.TagFilters;
            options.MaxTagsPerMetric = metricsOptions.MaxTagsPerMetric;
        });

        return AddMonitoringCore(services);
    }

    /// <summary>
    /// 添加指标收集
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcMetrics(
        this IServiceCollection services,
        Action<MetricsOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 注册指标收集器
        services.TryAddSingleton<IMetricsCollector, MetricsCollector>();

        return services;
    }

    /// <summary>
    /// 添加性能监控
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcPerformanceMonitoring(
        this IServiceCollection services,
        Action<PerformanceMonitorOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 确保指标收集器已注册
        services.TryAddSingleton<IMetricsCollector, MetricsCollector>();

        // 注册性能监控器作为后台服务
        services.AddHostedService<PerformanceMonitor>();

        return services;
    }

    /// <summary>
    /// 添加自定义指标收集器
    /// </summary>
    /// <typeparam name="TMetricsCollector">指标收集器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCustomMetricsCollector<TMetricsCollector>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TMetricsCollector : class, IMetricsCollector
    {
        services.Add(new ServiceDescriptor(typeof(IMetricsCollector), typeof(TMetricsCollector), lifetime));
        return services;
    }

    /// <summary>
    /// 配置指标导出
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="endpoint">导出端点</param>
    /// <param name="interval">导出间隔</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureMetricsExport(
        this IServiceCollection services,
        string endpoint,
        TimeSpan? interval = null)
    {
        services.Configure<PerformanceMonitorOptions>(options =>
        {
            options.EnableMetricsExport = true;
            options.MetricsExportEndpoint = endpoint;
            if (interval.HasValue)
            {
                options.MetricsExportInterval = interval.Value;
            }
        });

        return services;
    }

    /// <summary>
    /// 记录RPC调用指标的便捷方法
    /// </summary>
    /// <param name="metricsCollector">指标收集器</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="success">是否成功</param>
    /// <param name="duration">耗时</param>
    /// <param name="requestSize">请求大小</param>
    /// <param name="responseSize">响应大小</param>
    public static void RecordRpcCall(
        this IMetricsCollector metricsCollector,
        string serviceName,
        string methodName,
        bool success,
        TimeSpan duration,
        long requestSize = 0,
        long responseSize = 0)
    {
        var status = success ? "success" : "error";
        metricsCollector.RecordRpcCall(serviceName, methodName, status, duration, requestSize, responseSize);
    }

    /// <summary>
    /// 使用计时器自动记录耗时
    /// </summary>
    /// <param name="metricsCollector">指标收集器</param>
    /// <param name="name">计时器名称</param>
    /// <param name="tags">标签</param>
    /// <returns>计时器上下文</returns>
    public static ITimerContext StartTiming(
        this IMetricsCollector metricsCollector,
        string name,
        IDictionary<string, string>? tags = null)
    {
        var timer = metricsCollector.GetTimer(name, $"Timer for {name}", tags);
        return timer.StartTimer();
    }

    /// <summary>
    /// 记录计数事件
    /// </summary>
    /// <param name="metricsCollector">指标收集器</param>
    /// <param name="name">计数器名称</param>
    /// <param name="value">计数值</param>
    /// <param name="tags">标签</param>
    public static void IncrementCounter(
        this IMetricsCollector metricsCollector,
        string name,
        double value = 1.0,
        IDictionary<string, string>? tags = null)
    {
        var counter = metricsCollector.GetCounter(name, $"Counter for {name}", tags);
        counter.Increment(value);
    }

    /// <summary>
    /// 设置仪表值
    /// </summary>
    /// <param name="metricsCollector">指标收集器</param>
    /// <param name="name">仪表名称</param>
    /// <param name="value">仪表值</param>
    /// <param name="tags">标签</param>
    public static void SetGauge(
        this IMetricsCollector metricsCollector,
        string name,
        double value,
        IDictionary<string, string>? tags = null)
    {
        var gauge = metricsCollector.GetGauge(name, $"Gauge for {name}", tags);
        gauge.Set(value);
    }

    /// <summary>
    /// 记录直方图观察值
    /// </summary>
    /// <param name="metricsCollector">指标收集器</param>
    /// <param name="name">直方图名称</param>
    /// <param name="value">观察值</param>
    /// <param name="tags">标签</param>
    public static void ObserveHistogram(
        this IMetricsCollector metricsCollector,
        string name,
        double value,
        IDictionary<string, string>? tags = null)
    {
        var histogram = metricsCollector.GetHistogram(name, $"Histogram for {name}", null, tags);
        histogram.Observe(value);
    }
}
