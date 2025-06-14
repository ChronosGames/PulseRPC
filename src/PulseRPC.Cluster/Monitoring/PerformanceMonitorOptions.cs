namespace PulseRPC.Monitoring.Performance;

/// <summary>
/// 性能监控配置选项
/// </summary>
public class PerformanceMonitorOptions
{
    /// <summary>
    /// 是否启用性能监控
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 采样间隔
    /// </summary>
    public TimeSpan SamplingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否启用详细指标收集
    /// </summary>
    public bool EnableDetailedMetrics { get; set; } = false;

    /// <summary>
    /// 是否收集系统级指标
    /// </summary>
    public bool CollectSystemMetrics { get; set; } = true;

    /// <summary>
    /// 是否收集进程级指标
    /// </summary>
    public bool CollectProcessMetrics { get; set; } = true;

    /// <summary>
    /// 是否收集GC指标
    /// </summary>
    public bool CollectGcMetrics { get; set; } = true;

    /// <summary>
    /// 是否收集线程池指标
    /// </summary>
    public bool CollectThreadPoolMetrics { get; set; } = true;

    /// <summary>
    /// 指标保留时间
    /// </summary>
    public TimeSpan MetricsRetentionTime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 最大指标数量
    /// </summary>
    public int MaxMetricsCount { get; set; } = 10000;

    /// <summary>
    /// 是否启用指标导出
    /// </summary>
    public bool EnableMetricsExport { get; set; } = false;

    /// <summary>
    /// 指标导出间隔
    /// </summary>
    public TimeSpan MetricsExportInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 指标导出端点
    /// </summary>
    public string? MetricsExportEndpoint { get; set; }

    /// <summary>
    /// 是否启用警报
    /// </summary>
    public bool EnableAlerts { get; set; } = false;

    /// <summary>
    /// CPU使用率警报阈值 (%)
    /// </summary>
    public double CpuUsageAlertThreshold { get; set; } = 80.0;

    /// <summary>
    /// 内存使用率警报阈值 (%)
    /// </summary>
    public double MemoryUsageAlertThreshold { get; set; } = 80.0;

    /// <summary>
    /// GC频率警报阈值 (次/分钟)
    /// </summary>
    public double GcFrequencyAlertThreshold { get; set; } = 10.0;

    /// <summary>
    /// 错误率警报阈值 (%)
    /// </summary>
    public double ErrorRateAlertThreshold { get; set; } = 5.0;
}

/// <summary>
/// 监控配置选项
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// 性能监控选项
    /// </summary>
    public PerformanceMonitorOptions Performance { get; set; } = new();

    /// <summary>
    /// 指标收集选项
    /// </summary>
    public MetricsOptions Metrics { get; set; } = new();

    /// <summary>
    /// 日志选项
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();

    /// <summary>
    /// 跟踪选项
    /// </summary>
    public TracingOptions Tracing { get; set; } = new();
}

/// <summary>
/// 指标配置选项
/// </summary>
public class MetricsOptions
{
    /// <summary>
    /// 是否启用指标收集
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认直方图分桶
    /// </summary>
    public double[] DefaultHistogramBuckets { get; set; } =
    {
        0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1.0, 2.5, 5.0, 7.5, 10.0
    };

    /// <summary>
    /// 是否收集详细的RPC指标
    /// </summary>
    public bool CollectDetailedRpcMetrics { get; set; } = true;

    /// <summary>
    /// 是否收集负载均衡指标
    /// </summary>
    public bool CollectLoadBalancingMetrics { get; set; } = true;

    /// <summary>
    /// 是否收集服务发现指标
    /// </summary>
    public bool CollectServiceDiscoveryMetrics { get; set; } = true;

    /// <summary>
    /// 是否收集健康检查指标
    /// </summary>
    public bool CollectHealthCheckMetrics { get; set; } = true;

    /// <summary>
    /// 是否收集连接池指标
    /// </summary>
    public bool CollectConnectionPoolMetrics { get; set; } = true;

    /// <summary>
    /// 指标标签过滤器
    /// </summary>
    public Dictionary<string, string> TagFilters { get; set; } = new();

    /// <summary>
    /// 最大标签数量
    /// </summary>
    public int MaxTagsPerMetric { get; set; } = 20;
}

/// <summary>
/// 日志配置选项
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// 是否启用结构化日志
    /// </summary>
    public bool EnableStructuredLogging { get; set; } = true;

    /// <summary>
    /// 是否记录请求和响应
    /// </summary>
    public bool LogRequestResponse { get; set; } = false;

    /// <summary>
    /// 是否记录性能信息
    /// </summary>
    public bool LogPerformance { get; set; } = true;

    /// <summary>
    /// 最低日志级别
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// 日志输出目标
    /// </summary>
    public string[] Outputs { get; set; } = { "Console" };

    /// <summary>
    /// 是否启用日志采样
    /// </summary>
    public bool EnableSampling { get; set; } = false;

    /// <summary>
    /// 采样率 (0.0 - 1.0)
    /// </summary>
    public double SamplingRate { get; set; } = 0.1;
}

/// <summary>
/// 跟踪配置选项
/// </summary>
public class TracingOptions
{
    /// <summary>
    /// 是否启用分布式跟踪
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 跟踪系统类型
    /// </summary>
    public string TracingSystem { get; set; } = "OpenTelemetry";

    /// <summary>
    /// 跟踪端点
    /// </summary>
    public string? TracingEndpoint { get; set; }

    /// <summary>
    /// 采样率 (0.0 - 1.0)
    /// </summary>
    public double SamplingRate { get; set; } = 0.1;

    /// <summary>
    /// 是否跟踪数据库操作
    /// </summary>
    public bool TraceDatabaseOperations { get; set; } = false;

    /// <summary>
    /// 是否跟踪HTTP请求
    /// </summary>
    public bool TraceHttpRequests { get; set; } = true;

    /// <summary>
    /// 是否跟踪RPC调用
    /// </summary>
    public bool TraceRpcCalls { get; set; } = true;

    /// <summary>
    /// 最大跟踪长度
    /// </summary>
    public int MaxTraceLength { get; set; } = 1000;

    /// <summary>
    /// 跟踪标签
    /// </summary>
    public Dictionary<string, string> TraceTags { get; set; } = new();
}
