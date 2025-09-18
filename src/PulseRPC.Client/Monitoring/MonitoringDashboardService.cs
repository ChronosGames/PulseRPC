using Microsoft.Extensions.Logging;
using PulseRPC.Client.ConnectionPool;
using PulseRPC.Client.Health;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Monitoring;

/// <summary>
/// 监控仪表板服务
/// </summary>
public sealed class MonitoringDashboardService : IMonitoringDashboard, IDisposable
{
    private readonly IStatisticsCollector _statisticsCollector;
    private readonly IHealthCheckManager _healthCheckManager;
    private readonly ILogger<MonitoringDashboardService> _logger;
    private readonly DashboardConfiguration _configuration;
    private readonly Timer? _alertTimer;
    private readonly List<HealthIssue> _activeAlerts = new();
    private readonly object _alertLock = new();
    private volatile bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    public MonitoringDashboardService(
        IStatisticsCollector statisticsCollector,
        IHealthCheckManager healthCheckManager,
        DashboardConfiguration? configuration = null,
        ILogger<MonitoringDashboardService>? logger = null)
    {
        _statisticsCollector = statisticsCollector;
        _healthCheckManager = healthCheckManager;
        _configuration = configuration ?? new DashboardConfiguration();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MonitoringDashboardService>.Instance;

        // 如果启用了警报系统，创建警报检查定时器
        if (_configuration.EnabledFeatures.HasFlag(DashboardFeatures.AlertSystem) && _configuration.AlertConfiguration.Enabled)
        {
            _alertTimer = new Timer(
                callback: async _ => await CheckAlertsAsync(),
                state: null,
                dueTime: _configuration.AlertConfiguration.CheckInterval,
                period: _configuration.AlertConfiguration.CheckInterval);
        }

        _logger.LogDebug("监控仪表板服务已创建");
    }

    /// <summary>
    /// 获取仪表板数据
    /// </summary>
    public async Task<MonitoringDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MonitoringDashboardService));
        }

        var dashboardData = new MonitoringDashboardData
        {
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // 并行获取各种数据
            var tasks = new List<Task>
            {
                Task.Run(async () => dashboardData.KeyMetrics = await GetRealTimeMetricsAsync(cancellationToken), cancellationToken),
                Task.Run(async () => dashboardData.OverallHealthStatus = await GetSystemHealthStatusAsync(cancellationToken), cancellationToken),
                Task.Run(async () => dashboardData.ActiveAlerts = await GetActiveAlertsAsync(cancellationToken), cancellationToken)
            };

            if (_configuration.EnabledFeatures.HasFlag(DashboardFeatures.ConnectionMonitoring))
            {
                tasks.Add(Task.Run(async () => dashboardData.ConnectionOverview = await GetConnectionOverviewAsync(cancellationToken), cancellationToken));
            }

            if (_configuration.EnabledFeatures.HasFlag(DashboardFeatures.PerformanceMonitoring))
            {
                tasks.Add(Task.Run(async () => dashboardData.PerformanceOverview = await GetPerformanceOverviewAsync(cancellationToken), cancellationToken));
            }

            if (_configuration.EnabledFeatures.HasFlag(DashboardFeatures.HealthChecks))
            {
                tasks.Add(Task.Run(async () => dashboardData.HealthCheckOverview = await GetHealthCheckOverviewAsync(cancellationToken), cancellationToken));
            }

            if (_configuration.EnabledFeatures.HasFlag(DashboardFeatures.TrendAnalysis))
            {
                tasks.Add(Task.Run(async () => dashboardData.TrendData = await GetTrendDataAsync(TimeSpan.FromHours(24), cancellationToken), cancellationToken));
            }

            await Task.WhenAll(tasks);

            // 获取系统信息
            dashboardData.SystemInfo = GetSystemInfo();

            _logger.LogDebug("仪表板数据获取完成");
            return dashboardData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取仪表板数据失败");
            throw;
        }
    }

    /// <summary>
    /// 获取趋势数据
    /// </summary>
    public async Task<Dictionary<string, List<TimeSeriesDataPoint>>> GetTrendDataAsync(TimeSpan timeRange, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new Dictionary<string, List<TimeSeriesDataPoint>>();
        }

        try
        {
            var query = new StatisticsQuery
            {
                StartTime = DateTime.UtcNow - timeRange,
                MaxDataPoints = _configuration.MaxTrendDataPoints,
                AggregationInterval = CalculateAggregationInterval(timeRange)
            };

            var timeSeries = await _statisticsCollector.QueryTimeSeriesAsync(query, cancellationToken);

            var trendData = new Dictionary<string, List<TimeSeriesDataPoint>>();

            foreach (var series in timeSeries)
            {
                trendData[series.MetricName] = series.DataPoints.ToList();
            }

            return trendData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取趋势数据失败");
            return new Dictionary<string, List<TimeSeriesDataPoint>>();
        }
    }

    /// <summary>
    /// 获取实时指标
    /// </summary>
    public async Task<Dictionary<string, DashboardMetric>> GetRealTimeMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new Dictionary<string, DashboardMetric>();
        }

        try
        {
            var metrics = new Dictionary<string, DashboardMetric>();
            var currentMetrics = _statisticsCollector.GetCurrentMetrics();

            foreach (var metric in currentMetrics)
            {
                var dashboardMetric = new DashboardMetric
                {
                    Name = metric.Name,
                    CurrentValue = metric.Value,
                    Unit = metric.Unit
                };

                // 应用阈值配置
                if (_configuration.MetricThresholds.TryGetValue(metric.Name, out var threshold))
                {
                    dashboardMetric.Thresholds = threshold;
                    dashboardMetric.Status = EvaluateMetricStatus(metric.Value, threshold);
                }

                metrics[metric.Name] = dashboardMetric;
            }

            // 添加一些计算出的指标
            await AddCalculatedMetricsAsync(metrics, cancellationToken);

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取实时指标失败");
            return new Dictionary<string, DashboardMetric>();
        }
    }

    /// <summary>
    /// 获取系统健康状态
    /// </summary>
    public async Task<SystemHealthStatus> GetSystemHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return SystemHealthStatus.Unknown;
        }

        try
        {
            // 这里应该根据各种健康检查结果来确定整体状态
            // 目前简化实现，返回健康状态
            await Task.Yield();

            // 检查是否有严重警报
            lock (_alertLock)
            {
                if (_activeAlerts.Any(a => a.Severity == SystemHealthStatus.Critical))
                {
                    return SystemHealthStatus.Critical;
                }

                if (_activeAlerts.Any(a => a.Severity == SystemHealthStatus.Warning))
                {
                    return SystemHealthStatus.Warning;
                }
            }

            return SystemHealthStatus.Healthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取系统健康状态失败");
            return SystemHealthStatus.Unknown;
        }
    }

    /// <summary>
    /// 获取活跃警报
    /// </summary>
    public async Task<List<HealthIssue>> GetActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        lock (_alertLock)
        {
            return _activeAlerts.ToList();
        }
    }

    /// <summary>
    /// 导出监控数据
    /// </summary>
    public async Task<byte[]> ExportDataAsync(TimeSpan timeRange, string format = "json", CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MonitoringDashboardService));
        }

        try
        {
            var dashboardData = await GetDashboardDataAsync(cancellationToken);
            var trendData = await GetTrendDataAsync(timeRange, cancellationToken);

            var exportData = new
            {
                Dashboard = dashboardData,
                TrendData = trendData,
                ExportTime = DateTime.UtcNow,
                TimeRange = timeRange.ToString(),
                Format = format
            };

            return format.ToLowerInvariant() switch
            {
                "json" => JsonSerializer.SerializeToUtf8Bytes(exportData, new JsonSerializerOptions { WriteIndented = true }),
                _ => throw new NotSupportedException($"不支持的导出格式: {format}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出监控数据失败");
            throw;
        }
    }

    /// <summary>
    /// 获取连接概览
    /// </summary>
    private async Task<ConnectionOverview> GetConnectionOverviewAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        // 这里应该从连接管理器获取实际数据
        // 目前提供模拟数据
        return new ConnectionOverview
        {
            TotalConnections = 10,
            ActiveConnections = 8,
            IdleConnections = 2,
            FailedConnections = 0,
            SuccessRate = 100.0,
            AverageResponseTime = TimeSpan.FromMilliseconds(50),
            ConnectionStates = new Dictionary<string, int>
            {
                ["Connected"] = 8,
                ["Idle"] = 2,
                ["Failed"] = 0
            }
        };
    }

    /// <summary>
    /// 获取性能概览
    /// </summary>
    private async Task<PerformanceOverview> GetPerformanceOverviewAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        var process = Process.GetCurrentProcess();

        return new PerformanceOverview
        {
            CpuUsage = 0, // 需要实际的CPU监控
            MemoryUsage = process.WorkingSet64 / 1024.0 / 1024.0, // MB
            NetworkThroughput = 0, // 需要网络监控
            RequestsPerSecond = 0, // 需要从统计数据计算
            ErrorRate = 0, // 需要从统计数据计算
            AverageLatency = 0, // 需要从统计数据计算
            P99Latency = 0, // 需要从统计数据计算
            ConcurrentConnections = 0 // 需要从连接管理器获取
        };
    }

    /// <summary>
    /// 获取健康检查概览
    /// </summary>
    private async Task<HealthCheckOverview> GetHealthCheckOverviewAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        var overview = new HealthCheckOverview
        {
            OverallStatus = SystemHealthStatus.Healthy,
            LastCheckTime = DateTime.UtcNow
        };

        // 这里应该从健康检查管理器获取实际数据
        var checkers = _healthCheckManager.GetAllCheckers();
        foreach (var checker in checkers)
        {
            overview.CheckResults[checker.Name] = new HealthCheckSummary
            {
                CheckerName = checker.Name,
                Status = HealthStatus.Healthy,
                Description = "健康检查正常",
                LastCheckTime = DateTime.UtcNow,
                CheckCount = 1,
                FailureCount = 0
            };
        }

        return overview;
    }

    /// <summary>
    /// 添加计算出的指标
    /// </summary>
    private async Task AddCalculatedMetricsAsync(Dictionary<string, DashboardMetric> metrics, CancellationToken cancellationToken)
    {
        await Task.Yield();

        // 系统运行时间
        var uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        metrics["system.uptime"] = new DashboardMetric
        {
            Name = "system.uptime",
            CurrentValue = uptime.TotalSeconds,
            Unit = "seconds",
            Status = SystemHealthStatus.Healthy
        };

        // 内存使用量
        var process = Process.GetCurrentProcess();
        metrics["system.memory_usage"] = new DashboardMetric
        {
            Name = "system.memory_usage",
            CurrentValue = process.WorkingSet64 / 1024.0 / 1024.0,
            Unit = "MB",
            Status = SystemHealthStatus.Healthy
        };
    }

    /// <summary>
    /// 计算聚合间隔
    /// </summary>
    private static TimeSpan CalculateAggregationInterval(TimeSpan timeRange)
    {
        return timeRange.TotalHours switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            <= 6 => TimeSpan.FromMinutes(5),
            <= 24 => TimeSpan.FromMinutes(15),
            <= 168 => TimeSpan.FromHours(1), // 1 week
            _ => TimeSpan.FromHours(6)
        };
    }

    /// <summary>
    /// 评估指标状态
    /// </summary>
    private static SystemHealthStatus EvaluateMetricStatus(double value, ThresholdConfiguration threshold)
    {
        if (threshold.CriticalThreshold.HasValue)
        {
            if (threshold.IsMaxThreshold && value > threshold.CriticalThreshold.Value)
                return SystemHealthStatus.Critical;
            if (!threshold.IsMaxThreshold && value < threshold.CriticalThreshold.Value)
                return SystemHealthStatus.Critical;
        }

        if (threshold.WarningThreshold.HasValue)
        {
            if (threshold.IsMaxThreshold && value > threshold.WarningThreshold.Value)
                return SystemHealthStatus.Warning;
            if (!threshold.IsMaxThreshold && value < threshold.WarningThreshold.Value)
                return SystemHealthStatus.Warning;
        }

        return SystemHealthStatus.Healthy;
    }

    /// <summary>
    /// 检查警报
    /// </summary>
    private async Task CheckAlertsAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var currentMetrics = await GetRealTimeMetricsAsync();

            foreach (var rule in _configuration.AlertConfiguration.Rules.Where(r => r.Enabled))
            {
                if (currentMetrics.TryGetValue(rule.MetricName, out var metric))
                {
                    var isTriggered = EvaluateAlertRule(rule, metric.CurrentValue);

                    if (isTriggered)
                    {
                        var alert = new HealthIssue
                        {
                            Severity = MapAlertLevelToHealthStatus(rule.Level),
                            Description = rule.Description,
                            Source = rule.Name,
                            Timestamp = DateTime.UtcNow,
                            Details = new Dictionary<string, object>
                            {
                                ["MetricName"] = rule.MetricName,
                                ["CurrentValue"] = metric.CurrentValue,
                                ["Threshold"] = rule.Threshold,
                                ["Condition"] = rule.Condition
                            }
                        };

                        AddAlert(alert);
                    }
                }
            }

            // 清理过期警报
            CleanupExpiredAlerts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查警报失败");
        }
    }

    /// <summary>
    /// 评估警报规则
    /// </summary>
    private static bool EvaluateAlertRule(AlertRule rule, double value)
    {
        return rule.Condition.ToLowerInvariant() switch
        {
            ">" or "greater_than" => value > rule.Threshold,
            "<" or "less_than" => value < rule.Threshold,
            ">=" or "greater_equal" => value >= rule.Threshold,
            "<=" or "less_equal" => value <= rule.Threshold,
            "==" or "equal" => Math.Abs(value - rule.Threshold) < double.Epsilon,
            "!=" or "not_equal" => Math.Abs(value - rule.Threshold) > double.Epsilon,
            _ => false
        };
    }

    /// <summary>
    /// 添加警报
    /// </summary>
    private void AddAlert(HealthIssue alert)
    {
        lock (_alertLock)
        {
            // 检查是否已存在相同的警报（防止重复）
            var existingAlert = _activeAlerts.FirstOrDefault(a =>
                a.Source == alert.Source &&
                a.Description == alert.Description &&
                (DateTime.UtcNow - a.Timestamp) < _configuration.AlertConfiguration.SuppressionTime);

            if (existingAlert == null)
            {
                _activeAlerts.Add(alert);

                // 限制警报数量
                while (_activeAlerts.Count > _configuration.AlertConfiguration.MaxAlerts)
                {
                    _activeAlerts.RemoveAt(0);
                }

                _logger.LogWarning("新警报: {Severity} - {Description} (来源: {Source})",
                    alert.Severity, alert.Description, alert.Source);
            }
        }
    }

    /// <summary>
    /// 清理过期警报
    /// </summary>
    private void CleanupExpiredAlerts()
    {
        lock (_alertLock)
        {
            var cutoffTime = DateTime.UtcNow - TimeSpan.FromHours(24); // 24小时后过期
            _activeAlerts.RemoveAll(a => a.Timestamp < cutoffTime);
        }
    }

    /// <summary>
    /// 获取系统信息
    /// </summary>
    private static Dictionary<string, object> GetSystemInfo()
    {
        var process = Process.GetCurrentProcess();

        return new Dictionary<string, object>
        {
            ["ProcessId"] = process.Id,
            ["ProcessName"] = process.ProcessName,
            ["StartTime"] = process.StartTime,
            ["MachineName"] = Environment.MachineName,
            ["UserName"] = Environment.UserName,
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["ProcessorCount"] = Environment.ProcessorCount,
            ["WorkingSet"] = process.WorkingSet64,
            ["PrivateMemorySize"] = process.PrivateMemorySize64,
            ["VirtualMemorySize"] = process.VirtualMemorySize64
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogInformation("正在关闭监控仪表板服务");

        _alertTimer?.Dispose();

        lock (_alertLock)
        {
            _activeAlerts.Clear();
        }

        _logger.LogInformation("监控仪表板服务已关闭");
    }

    /// <summary>
    /// 将 AlertLevel 映射到 SystemHealthStatus
    /// </summary>
    private static SystemHealthStatus MapAlertLevelToHealthStatus(AlertLevel alertLevel)
    {
        return alertLevel switch
        {
            AlertLevel.Info => SystemHealthStatus.Healthy,
            AlertLevel.Warning => SystemHealthStatus.Warning,
            AlertLevel.Error => SystemHealthStatus.Critical,
            AlertLevel.Critical => SystemHealthStatus.Critical,
            _ => SystemHealthStatus.Warning
        };
    }
}
