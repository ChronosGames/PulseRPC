using PulseRPC.Client.ConnectionPool;
using PulseRPC.Client.Health;
using PulseRPC.Client.LifecycleStrategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Monitoring;

/// <summary>
/// 系统健康状态
/// </summary>
public enum SystemHealthStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// 严重
    /// </summary>
    Critical,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown
}

/// <summary>
/// 仪表板指标
/// </summary>
public sealed class DashboardMetric
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 当前值
    /// </summary>
    public double CurrentValue { get; set; }

    /// <summary>
    /// 前一个值
    /// </summary>
    public double? PreviousValue { get; set; }

    /// <summary>
    /// 变化百分比
    /// </summary>
    public double? ChangePercentage => PreviousValue.HasValue && PreviousValue.Value != 0
        ? (CurrentValue - PreviousValue.Value) / PreviousValue.Value * 100
        : null;

    /// <summary>
    /// 单位
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 格式化字符串
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// 趋势方向
    /// </summary>
    public TrendDirection Trend { get; set; } = TrendDirection.Stable;

    /// <summary>
    /// 状态
    /// </summary>
    public SystemHealthStatus Status { get; set; } = SystemHealthStatus.Healthy;

    /// <summary>
    /// 阈值配置
    /// </summary>
    public ThresholdConfiguration? Thresholds { get; set; }
}

/// <summary>
/// 趋势方向
/// </summary>
public enum TrendDirection
{
    /// <summary>
    /// 上升
    /// </summary>
    Up,

    /// <summary>
    /// 下降
    /// </summary>
    Down,

    /// <summary>
    /// 稳定
    /// </summary>
    Stable
}

/// <summary>
/// 阈值配置
/// </summary>
public sealed class ThresholdConfiguration
{
    /// <summary>
    /// 警告阈值
    /// </summary>
    public double? WarningThreshold { get; set; }

    /// <summary>
    /// 严重阈值
    /// </summary>
    public double? CriticalThreshold { get; set; }

    /// <summary>
    /// 是否为最大值阈值（true: 超过阈值为异常，false: 低于阈值为异常）
    /// </summary>
    public bool IsMaxThreshold { get; set; } = true;
}

/// <summary>
/// 连接概览数据
/// </summary>
public sealed class ConnectionOverview
{
    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 空闲连接数
    /// </summary>
    public int IdleConnections { get; set; }

    /// <summary>
    /// 失败连接数
    /// </summary>
    public int FailedConnections { get; set; }

    /// <summary>
    /// 连接成功率
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public TimeSpan? AverageResponseTime { get; set; }

    /// <summary>
    /// 连接状态分布
    /// </summary>
    public Dictionary<string, int> ConnectionStates { get; set; } = new();

    /// <summary>
    /// 连接池状态
    /// </summary>
    public Dictionary<string, ConnectionPoolStatus> PoolStatuses { get; set; } = new();
}

/// <summary>
/// 连接池状态
/// </summary>
public sealed class ConnectionPoolStatus
{
    /// <summary>
    /// 池名称
    /// </summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// 池状态
    /// </summary>
    public ConnectionPoolState State { get; set; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 租借的连接数
    /// </summary>
    public int LeasedConnections { get; set; }

    /// <summary>
    /// 利用率
    /// </summary>
    public double UtilizationRate { get; set; }

    /// <summary>
    /// 健康状态
    /// </summary>
    public SystemHealthStatus HealthStatus { get; set; }
}

/// <summary>
/// 性能指标概览
/// </summary>
public sealed class PerformanceOverview
{
    /// <summary>
    /// CPU 使用率
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// 内存使用量 (MB)
    /// </summary>
    public double MemoryUsage { get; set; }

    /// <summary>
    /// 网络吞吐量 (bytes/sec)
    /// </summary>
    public double NetworkThroughput { get; set; }

    /// <summary>
    /// 请求每秒数 (RPS)
    /// </summary>
    public double RequestsPerSecond { get; set; }

    /// <summary>
    /// 错误率
    /// </summary>
    public double ErrorRate { get; set; }

    /// <summary>
    /// 平均延迟 (ms)
    /// </summary>
    public double AverageLatency { get; set; }

    /// <summary>
    /// 99% 分位延迟 (ms)
    /// </summary>
    public double P99Latency { get; set; }

    /// <summary>
    /// 当前并发连接数
    /// </summary>
    public int ConcurrentConnections { get; set; }
}

/// <summary>
/// 健康检查概览
/// </summary>
public sealed class HealthCheckOverview
{
    /// <summary>
    /// 整体健康状态
    /// </summary>
    public SystemHealthStatus OverallStatus { get; set; }

    /// <summary>
    /// 健康检查结果
    /// </summary>
    public Dictionary<string, HealthCheckSummary> CheckResults { get; set; } = new();

    /// <summary>
    /// 最近的异常
    /// </summary>
    public List<HealthIssue> RecentIssues { get; set; } = new();

    /// <summary>
    /// 最后检查时间
    /// </summary>
    public DateTime LastCheckTime { get; set; }
}

/// <summary>
/// 健康检查摘要
/// </summary>
public sealed class HealthCheckSummary
{
    /// <summary>
    /// 检查器名称
    /// </summary>
    public string CheckerName { get; set; } = string.Empty;

    /// <summary>
    /// 状态
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan ResponseTime { get; set; }

    /// <summary>
    /// 最后检查时间
    /// </summary>
    public DateTime LastCheckTime { get; set; }

    /// <summary>
    /// 检查次数
    /// </summary>
    public int CheckCount { get; set; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => CheckCount > 0 ? (double)(CheckCount - FailureCount) / CheckCount * 100 : 100;
}

/// <summary>
/// 健康问题
/// </summary>
public sealed class HealthIssue
{
    /// <summary>
    /// 严重程度
    /// </summary>
    public SystemHealthStatus Severity { get; set; }

    /// <summary>
    /// 问题描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 来源
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 发生时间
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 详细信息
    /// </summary>
    public Dictionary<string, object> Details { get; set; } = new();
}

/// <summary>
/// 监控仪表板数据
/// </summary>
public sealed class MonitoringDashboardData
{
    /// <summary>
    /// 数据时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 系统整体健康状态
    /// </summary>
    public SystemHealthStatus OverallHealthStatus { get; set; }

    /// <summary>
    /// 关键指标
    /// </summary>
    public Dictionary<string, DashboardMetric> KeyMetrics { get; set; } = new();

    /// <summary>
    /// 连接概览
    /// </summary>
    public ConnectionOverview ConnectionOverview { get; set; } = new();

    /// <summary>
    /// 性能概览
    /// </summary>
    public PerformanceOverview PerformanceOverview { get; set; } = new();

    /// <summary>
    /// 健康检查概览
    /// </summary>
    public HealthCheckOverview HealthCheckOverview { get; set; } = new();

    /// <summary>
    /// 警报列表
    /// </summary>
    public List<HealthIssue> ActiveAlerts { get; set; } = new();

    /// <summary>
    /// 趋势数据（最近24小时）
    /// </summary>
    public Dictionary<string, List<TimeSeriesDataPoint>> TrendData { get; set; } = new();

    /// <summary>
    /// 系统信息
    /// </summary>
    public Dictionary<string, object> SystemInfo { get; set; } = new();
}

/// <summary>
/// 监控仪表板服务
/// </summary>
public interface IMonitoringDashboard
{
    /// <summary>
    /// 获取仪表板数据
    /// </summary>
    Task<MonitoringDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取特定时间范围的趋势数据
    /// </summary>
    Task<Dictionary<string, List<TimeSeriesDataPoint>>> GetTrendDataAsync(
        TimeSpan timeRange,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取实时指标
    /// </summary>
    Task<Dictionary<string, DashboardMetric>> GetRealTimeMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取系统健康状态
    /// </summary>
    Task<SystemHealthStatus> GetSystemHealthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取活跃警报
    /// </summary>
    Task<List<HealthIssue>> GetActiveAlertsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出监控数据
    /// </summary>
    Task<byte[]> ExportDataAsync(
        TimeSpan timeRange,
        string format = "json",
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 仪表板配置
/// </summary>
public sealed class DashboardConfiguration
{
    /// <summary>
    /// 刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 数据保留时间
    /// </summary>
    public TimeSpan DataRetentionTime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 指标阈值配置
    /// </summary>
    public Dictionary<string, ThresholdConfiguration> MetricThresholds { get; set; } = new();

    /// <summary>
    /// 启用的功能
    /// </summary>
    public DashboardFeatures EnabledFeatures { get; set; } = DashboardFeatures.All;

    /// <summary>
    /// 最大趋势数据点数
    /// </summary>
    public int MaxTrendDataPoints { get; set; } = 1000;

    /// <summary>
    /// 警报配置
    /// </summary>
    public AlertConfiguration AlertConfiguration { get; set; } = new();
}

/// <summary>
/// 仪表板功能标志
/// </summary>
[Flags]
public enum DashboardFeatures
{
    /// <summary>
    /// 无功能
    /// </summary>
    None = 0,

    /// <summary>
    /// 连接监控
    /// </summary>
    ConnectionMonitoring = 1,

    /// <summary>
    /// 性能监控
    /// </summary>
    PerformanceMonitoring = 2,

    /// <summary>
    /// 健康检查
    /// </summary>
    HealthChecks = 4,

    /// <summary>
    /// 趋势分析
    /// </summary>
    TrendAnalysis = 8,

    /// <summary>
    /// 警报系统
    /// </summary>
    AlertSystem = 16,

    /// <summary>
    /// 数据导出
    /// </summary>
    DataExport = 32,

    /// <summary>
    /// 所有功能
    /// </summary>
    All = ConnectionMonitoring | PerformanceMonitoring | HealthChecks | TrendAnalysis | AlertSystem | DataExport
}

/// <summary>
/// 警报配置
/// </summary>
public sealed class AlertConfiguration
{
    /// <summary>
    /// 是否启用警报
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 警报检查间隔
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 警报抑制时间
    /// </summary>
    public TimeSpan SuppressionTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大警报数量
    /// </summary>
    public int MaxAlerts { get; set; } = 100;

    /// <summary>
    /// 警报规则
    /// </summary>
    public List<AlertRule> Rules { get; set; } = new();
}


