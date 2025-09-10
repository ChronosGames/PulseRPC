using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Processing;

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
        Action<TieredEngineManagerOptions>? configureManagerOptions = null,
        bool useCompatibilityAdapter = true)
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

        // 注册TieredProcessorOptions配置
        services.Configure<TieredProcessorOptions>(options =>
        {
            // 从现有的ServerOptions中获取配置
            var serviceProvider = services.BuildServiceProvider();
            var serverOptions = serviceProvider.GetService<IOptions<ServerOptions>>();
            
            if (serverOptions?.Value.HighThroughputProcessor != null)
            {
                var htOptions = serverOptions.Value.HighThroughputProcessor;
                
                // 映射现有配置到新的TieredProcessorOptions
                options.L1BufferSize = htOptions.L1BufferSize;
                options.L1BackpressureThreshold = (int)(htOptions.L1BufferSize * 0.8);
                options.L2MaxBatchSize = htOptions.MaxBatchSize;
                options.L2QueueCapacity = htOptions.L2QueueCapacity;
                options.L2BatchIntervalMs = htOptions.BatchIntervalMs;
                options.L3SmallPoolSize = 1024;
                options.L3MediumPoolSize = 256;
                options.L3LargePoolSize = 64;
                options.L3MaxPooledBufferSize = 1024 * 1024;
                options.NormalMessageDropRate = htOptions.NormalMessageDropRate;
                options.CriticalMessageTimeoutMs = htOptions.CriticalMessageTimeoutUs / 1000;
                options.L2BackpressureWaitMs = htOptions.L2BackpressureWaitMs;
                options.EnablePerformanceMonitoring = true;
                options.EnableDetailedLogging = htOptions.EnableDetailedLogging;
                options.PerformanceCheckFrequency = htOptions.PerformanceCheckFrequency;
                options.BatchSoftTimeoutMs = htOptions.BatchSoftTimeoutMs;
            }
        });

        // 注册核心引擎管理器
        services.AddSingleton<ITieredMessageEngineManager, TieredMessageEngineManager>();
        
        if (useCompatibilityAdapter)
        {
            // 注册兼容性适配器，替换原有的IHighThroughputProcessorManager
            services.Replace(ServiceDescriptor.Singleton<IHighThroughputProcessorManager, TieredProcessorManagerAdapter>());
        }

        return services;
    }

    /// <summary>
    /// 从现有HighThroughputProcessorOptions迁移配置
    /// </summary>
    public static IServiceCollection MigrateTieredMessageEngineFromExistingConfig(
        this IServiceCollection services)
    {
        // 配置后处理器，用于从现有配置迁移
        services.AddSingleton<IConfigureOptions<TieredEngineManagerOptions>, TieredEngineConfigurationMigrator>();
        services.AddSingleton<IConfigureOptions<TieredProcessorOptions>, TieredProcessorConfigurationMigrator>();
        
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
/// 引擎配置迁移器
/// </summary>
internal sealed class TieredEngineConfigurationMigrator : IConfigureOptions<TieredEngineManagerOptions>
{
    private readonly IOptions<ServerOptions> _serverOptions;
    
    public TieredEngineConfigurationMigrator(IOptions<ServerOptions> serverOptions)
    {
        _serverOptions = serverOptions;
    }
    
    public void Configure(TieredEngineManagerOptions options)
    {
        var htOptions = _serverOptions.Value.HighThroughputProcessor;
        
        // 从现有配置迁移设置
        options.DefaultL1BufferSize = htOptions.L1BufferSize;
        options.DefaultL2QueueCapacity = htOptions.L2QueueCapacity;
        options.DefaultL3QueueCapacity = htOptions.L3QueueCapacity;
        options.DefaultMaxBatchSize = htOptions.MaxBatchSize;
        options.DefaultBatchIntervalMs = htOptions.BatchIntervalMs;
        options.EnableDetailedLogging = htOptions.EnableDetailedLogging;
        options.DefaultNormalMessageDropRate = htOptions.NormalMessageDropRate;
        options.DefaultCriticalMessageTimeoutUs = htOptions.CriticalMessageTimeoutUs;
        options.DefaultL2BackpressureWaitMs = htOptions.L2BackpressureWaitMs;
        options.DefaultPerformanceCheckFrequency = htOptions.PerformanceCheckFrequency;
        options.DefaultBatchSoftTimeoutMs = htOptions.BatchSoftTimeoutMs;
    }
}

/// <summary>
/// 处理器配置迁移器
/// </summary>
internal sealed class TieredProcessorConfigurationMigrator : IConfigureOptions<TieredProcessorOptions>
{
    private readonly IOptions<ServerOptions> _serverOptions;
    
    public TieredProcessorConfigurationMigrator(IOptions<ServerOptions> serverOptions)
    {
        _serverOptions = serverOptions;
    }
    
    public void Configure(TieredProcessorOptions options)
    {
        var htOptions = _serverOptions.Value.HighThroughputProcessor;
        
        // L1缓冲区配置
        options.L1BufferSize = htOptions.L1BufferSize;
        options.L1BackpressureThreshold = (int)(htOptions.L1BufferSize * 0.8);
        
        // L2批处理配置
        options.L2MaxBatchSize = htOptions.MaxBatchSize;
        options.L2QueueCapacity = htOptions.L2QueueCapacity;
        options.L2BatchIntervalMs = htOptions.BatchIntervalMs;
        
        // L3内存池配置（使用优化的默认值）
        options.L3SmallPoolSize = 1024;
        options.L3MediumPoolSize = 256;
        options.L3LargePoolSize = 64;
        options.L3MaxPooledBufferSize = 1024 * 1024;
        
        // 背压策略配置
        options.NormalMessageDropRate = htOptions.NormalMessageDropRate;
        options.CriticalMessageTimeoutMs = Math.Max(1, htOptions.CriticalMessageTimeoutUs / 1000);
        options.L2BackpressureWaitMs = htOptions.L2BackpressureWaitMs;
        
        // 性能监控配置
        options.EnablePerformanceMonitoring = true;
        options.EnableDetailedLogging = htOptions.EnableDetailedLogging;
        options.PerformanceCheckFrequency = htOptions.PerformanceCheckFrequency;
        options.BatchSoftTimeoutMs = htOptions.BatchSoftTimeoutMs;
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
    private readonly ITieredMessageEngineManager _engineManager;
    private readonly IOptions<MonitoringOptions> _options;
    
    public TieredEngineMonitorService(
        ITieredMessageEngineManager engineManager,
        IOptions<MonitoringOptions> options)
    {
        _engineManager = engineManager;
        _options = options;
    }
    
    public async Task<MonitoringReport> GetCurrentReportAsync()
    {
        var managerStats = await _engineManager.GetStatisticsAsync();
        
        return new MonitoringReport
        {
            Timestamp = DateTime.UtcNow,
            ManagerStatistics = managerStats,
            ConnectionReports = CreateConnectionReports(managerStats),
            SystemResources = CreateSystemResourceReport()
        };
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
    private readonly Microsoft.Extensions.Logging.ILogger<TieredEngineMonitoringHostedService> _logger;
    
    public TieredEngineMonitoringHostedService(
        ITieredEngineMonitorService monitorService,
        IOptions<MonitoringOptions> options,
        Microsoft.Extensions.Logging.ILogger<TieredEngineMonitoringHostedService> logger)
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
                        AlertSeverity.Info => Microsoft.Extensions.Logging.LogLevel.Information,
                        AlertSeverity.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
                        AlertSeverity.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                        AlertSeverity.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
                        _ => Microsoft.Extensions.Logging.LogLevel.Information
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