using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Processing;

// 类型别名兼容性
using TieredProcessorOptions = PulseRPC.Server.Engine.TieredMessageProcessorOptions;

namespace PulseRPC.Server.Engine;

/// <summary>
/// TieredMessageEngine服务扩展
/// </summary>
public static class TieredMessageEngineServiceExtensions
{
    /// <summary>
    /// 添加分层消息引擎服务，替代原有的ServerHighThroughputMessageProcessor
    /// </summary>
    public static IServiceCollection AddTieredMessageEngine(
        this IServiceCollection services,
        Action<TieredEngineManagerOptions>? configureManagerOptions = null)
    {
        // 注册引擎管理器选项
        if (configureManagerOptions != null)
        {
            services.Configure(configureManagerOptions);
        }
        else
        {
            // 使用默认配置
            services.Configure<TieredEngineManagerOptions>(options =>
            {
                options.MaxConnections = 10000;
                options.DefaultL1BufferSize = 4096;
                options.DefaultL2QueueCapacity = 256;
                options.DefaultL3QueueCapacity = 128;
                options.DefaultMaxBatchSize = 64;
                options.DefaultBatchIntervalMs = 5;
                options.EnableDetailedLogging = false;
            });
        }

        // 注册TieredProcessorOptions配置（使用默认值）
        services.Configure<TieredProcessorOptions>(options =>
        {
            // 使用默认配置值，这些值将通过类的默认属性值设置
            // 可以通过其他配置方法进行自定义
        });

        // 注册核心引擎管理器
        services.AddSingleton<ITieredMessageEngine, HighPerformanceMessageEngine>();

        return services;
    }

    /// <summary>
    /// 启用性能监控和统计收集
    /// </summary>
    public static IServiceCollection AddTieredMessageEngineMonitoring(
        this IServiceCollection services,
        Action<MonitoringOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddSingleton<ITieredEngineMonitorService, TieredEngineMonitorService>();
        services.AddHostedService<TieredEngineMonitoringHostedService>();

        return services;
    }
}

/// <summary>
/// 监控配置选项
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// 统计收集间隔
    /// </summary>
    public TimeSpan StatisticsCollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用详细监控
    /// </summary>
    public bool EnableDetailedMonitoring { get; set; } = true;

    /// <summary>
    /// 性能告警阈值
    /// </summary>
    public PerformanceThresholds Thresholds { get; set; } = new();
}

/// <summary>
/// 性能告警阈值
/// </summary>
public class PerformanceThresholds
{
    /// <summary>
    /// L1缓冲区利用率告警阈值
    /// </summary>
    public double L1UtilizationWarningThreshold { get; set; } = 0.8;

    /// <summary>
    /// 背压率告警阈值
    /// </summary>
    public double BackpressureRateWarningThreshold { get; set; } = 0.1;

    /// <summary>
    /// 消息错误率告警阈值
    /// </summary>
    public double ErrorRateWarningThreshold { get; set; } = 0.01;

    /// <summary>
    /// P95延迟告警阈值（毫秒）
    /// </summary>
    public double P95LatencyWarningThresholdMs { get; set; } = 100;
}

/// <summary>
/// 引擎监控服务接口
/// </summary>
public interface ITieredEngineMonitorService
{
    Task<MonitoringReport> GetCurrentReportAsync();
    Task<List<MonitoringAlert>> CheckAlertsAsync();
}

/// <summary>
/// 监控报告
/// </summary>
public class MonitoringReport
{
    public DateTime Timestamp { get; set; }
    public ManagerStatistics ManagerStatistics { get; set; } = new();
    public List<ConnectionPerformanceReport> ConnectionReports { get; set; } = new();
    public SystemResourceReport SystemResources { get; set; } = new();
}

/// <summary>
/// 连接性能报告
/// </summary>
public class ConnectionPerformanceReport
{
    public string ConnectionId { get; set; } = "";
    public double Throughput { get; set; }
    public TimeSpan AverageLatency { get; set; }
    public TimeSpan P95Latency { get; set; }
    public double ErrorRate { get; set; }
    public double BackpressureRate { get; set; }
}

/// <summary>
/// 系统资源报告
/// </summary>
public class SystemResourceReport
{
    public long TotalMemoryUsage { get; set; }
    public double CpuUsage { get; set; }
    public int ActiveThreadCount { get; set; }
    public long GarbageCollectorPressure { get; set; }
}

/// <summary>
/// 监控告警
/// </summary>
public class MonitoringAlert
{
    public string ConnectionId { get; set; } = "";
    public string AlertType { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public AlertSeverity Severity { get; set; }
}

/// <summary>
/// 告警严重程度
/// </summary>
public enum AlertSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// 引擎监控服务实现
/// </summary>
internal sealed class TieredEngineMonitorService : ITieredEngineMonitorService
{
    private readonly ITieredMessageEngine _engineManager;
    private readonly IOptions<MonitoringOptions> _options;

    public TieredEngineMonitorService(
        ITieredMessageEngine engineManager,
        IOptions<MonitoringOptions> options)
    {
        _engineManager = engineManager;
        _options = options;
    }

    public Task<MonitoringReport> GetCurrentReportAsync()
    {
        var managerStats = _engineManager.GetStatistics();
        var abc = new ManagerStatistics();

        return Task.FromResult(new MonitoringReport
        {
            Timestamp = DateTime.UtcNow,
            ManagerStatistics = abc,
            ConnectionReports = CreateConnectionReports(abc),
            SystemResources = CreateSystemResourceReport()
        });
    }

    public async Task<List<MonitoringAlert>> CheckAlertsAsync()
    {
        var report = await GetCurrentReportAsync();
        var alerts = new List<MonitoringAlert>();
        var thresholds = _options.Value.Thresholds;

        foreach (var connectionReport in report.ConnectionReports)
        {
            // 检查背压率告警
            if (connectionReport.BackpressureRate > thresholds.BackpressureRateWarningThreshold)
            {
                alerts.Add(new MonitoringAlert
                {
                    ConnectionId = connectionReport.ConnectionId,
                    AlertType = "HIGH_BACKPRESSURE",
                    Message = $"连接背压率过高: {connectionReport.BackpressureRate:P2}",
                    Timestamp = DateTime.UtcNow,
                    Severity = AlertSeverity.Warning
                });
            }

            // 检查错误率告警
            if (connectionReport.ErrorRate > thresholds.ErrorRateWarningThreshold)
            {
                alerts.Add(new MonitoringAlert
                {
                    ConnectionId = connectionReport.ConnectionId,
                    AlertType = "HIGH_ERROR_RATE",
                    Message = $"连接错误率过高: {connectionReport.ErrorRate:P2}",
                    Timestamp = DateTime.UtcNow,
                    Severity = AlertSeverity.Error
                });
            }

            // 检查P95延迟告警
            if (connectionReport.P95Latency.TotalMilliseconds > thresholds.P95LatencyWarningThresholdMs)
            {
                alerts.Add(new MonitoringAlert
                {
                    ConnectionId = connectionReport.ConnectionId,
                    AlertType = "HIGH_LATENCY",
                    Message = $"连接P95延迟过高: {connectionReport.P95Latency.TotalMilliseconds:F2}ms",
                    Timestamp = DateTime.UtcNow,
                    Severity = AlertSeverity.Warning
                });
            }
        }

        return alerts;
    }

    private List<ConnectionPerformanceReport> CreateConnectionReports(ManagerStatistics managerStats)
    {
        var reports = new List<ConnectionPerformanceReport>();

        foreach (var adapterStat in managerStats.AdapterStatistics)
        {
            if (adapterStat.TieredProcessorSummary != null)
            {
                var summary = adapterStat.TieredProcessorSummary;
                reports.Add(new ConnectionPerformanceReport
                {
                    ConnectionId = adapterStat.ConnectionId,
                    Throughput = summary.CurrentThroughput,
                    AverageLatency = summary.AverageBatchProcessingTime,
                    P95Latency = summary.P95BatchProcessingTime,
                    ErrorRate = summary.MessageErrorRate,
                    BackpressureRate = summary.L1BackpressureRate
                });
            }
        }

        return reports;
    }

    private SystemResourceReport CreateSystemResourceReport()
    {
        return new SystemResourceReport
        {
            TotalMemoryUsage = GC.GetTotalMemory(false),
            CpuUsage = 0, // 需要实现CPU使用率监控
            ActiveThreadCount = System.Threading.ThreadPool.ThreadCount,
            GarbageCollectorPressure = GC.CollectionCount(2)
        };
    }
}

/// <summary>
/// 监控后台服务
/// </summary>
internal sealed class TieredEngineMonitoringHostedService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ITieredEngineMonitorService _monitorService;
    private readonly IOptions<MonitoringOptions> _options;
    private readonly ILogger<TieredEngineMonitoringHostedService> _logger;

    public TieredEngineMonitoringHostedService(
        ITieredEngineMonitorService monitorService,
        IOptions<MonitoringOptions> options,
        ILogger<TieredEngineMonitoringHostedService> logger)
    {
        _monitorService = monitorService;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var alerts = await _monitorService.CheckAlertsAsync();

                foreach (var alert in alerts)
                {
                    var logLevel = alert.Severity switch
                    {
                        AlertSeverity.Info => LogLevel.Information,
                        AlertSeverity.Warning => LogLevel.Warning,
                        AlertSeverity.Error => LogLevel.Error,
                        AlertSeverity.Critical => LogLevel.Critical,
                        _ => LogLevel.Information
                    };

                    _logger.Log(logLevel, "TieredEngine监控告警: {AlertType} - {Message} (连接: {ConnectionId})",
                        alert.AlertType, alert.Message, alert.ConnectionId);
                }

                await Task.Delay(_options.Value.StatisticsCollectionInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "监控服务执行异常");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
