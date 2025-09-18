using PulseRPC.Client.Reliability;
using PulseRPC.Client.Routing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Monitoring;

/// <summary>
/// 监控级别
/// </summary>
public enum MonitoringLevel
{
    /// <summary>
    /// 基础监控
    /// </summary>
    Basic,

    /// <summary>
    /// 详细监控
    /// </summary>
    Detailed,

    /// <summary>
    /// 调试级监控
    /// </summary>
    Debug,

    /// <summary>
    /// 性能分析
    /// </summary>
    Profiling
}

/// <summary>
/// 指标类型
/// </summary>
public enum MetricType
{
    /// <summary>
    /// 计数器
    /// </summary>
    Counter,

    /// <summary>
    /// 计量器
    /// </summary>
    Gauge,

    /// <summary>
    /// 直方图
    /// </summary>
    Histogram,

    /// <summary>
    /// 摘要
    /// </summary>
    Summary,

    /// <summary>
    /// 计时器
    /// </summary>
    Timer
}

/// <summary>
/// 监控指标
/// </summary>
public sealed class MonitoringMetric
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 指标类型
    /// </summary>
    public MetricType Type { get; set; }

    /// <summary>
    /// 指标值
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// 单位
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = new();

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 扩展数据
    /// </summary>
    public Dictionary<string, object> ExtendedData { get; set; } = new();
}

/// <summary>
/// 健康检查结果
/// </summary>
public sealed class HealthCheckResult
{
    /// <summary>
    /// 检查名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// 描述信息
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 检查耗时
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 详细数据
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// 健康状态
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 降级
    /// </summary>
    Degraded,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown
}

/// <summary>
/// 警报级别
/// </summary>
public enum AlertLevel
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,

    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// 错误
    /// </summary>
    Error,

    /// <summary>
    /// 严重
    /// </summary>
    Critical
}

/// <summary>
/// 监控警报
/// </summary>
public sealed class MonitoringAlert
{
    /// <summary>
    /// 警报ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 警报名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 警报级别
    /// </summary>
    public AlertLevel Level { get; set; }

    /// <summary>
    /// 警报消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 警报来源
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 触发条件
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// 触发值
    /// </summary>
    public double? TriggerValue { get; set; }

    /// <summary>
    /// 阈值
    /// </summary>
    public double? Threshold { get; set; }

    /// <summary>
    /// 发生时间
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否已解决
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// 解决时间
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = new();

    /// <summary>
    /// 扩展数据
    /// </summary>
    public Dictionary<string, object> ExtendedData { get; set; } = new();
}

/// <summary>
/// 监控配置
/// </summary>
public sealed class MonitoringConfiguration
{
    /// <summary>
    /// 监控级别
    /// </summary>
    public MonitoringLevel Level { get; set; } = MonitoringLevel.Basic;

    /// <summary>
    /// 指标收集间隔
    /// </summary>
    public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 指标保留时间
    /// </summary>
    public TimeSpan MetricsRetentionTime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 警报规则
    /// </summary>
    public List<AlertRule> AlertRules { get; set; } = new();

    /// <summary>
    /// 启用的监控组件
    /// </summary>
    public HashSet<string> EnabledComponents { get; set; } = new();

    /// <summary>
    /// 自定义指标提供者
    /// </summary>
    public List<Func<Task<IEnumerable<MonitoringMetric>>>> CustomMetricProviders { get; set; } = new();

    /// <summary>
    /// 自定义健康检查
    /// </summary>
    public List<Func<CancellationToken, Task<HealthCheckResult>>> CustomHealthChecks { get; set; } = new();

    /// <summary>
    /// 扩展配置
    /// </summary>
    public Dictionary<string, object> ExtendedConfiguration { get; set; } = new();
}

/// <summary>
/// 警报规则
/// </summary>
public sealed class AlertRule
{
    /// <summary>
    /// 规则名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 规则描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 指标名称
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 条件表达式
    /// </summary>
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// 阈值
    /// </summary>
    public double Threshold { get; set; }

    /// <summary>
    /// 持续时间
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 警报级别
    /// </summary>
    public AlertLevel Level { get; set; } = AlertLevel.Warning;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 标签过滤器
    /// </summary>
    public Dictionary<string, string> LabelFilters { get; set; } = new();

    /// <summary>
    /// 警报消息模板
    /// </summary>
    public string MessageTemplate { get; set; } = string.Empty;
}

/// <summary>
/// 监控报告
/// </summary>
public sealed class MonitoringReport
{
    /// <summary>
    /// 报告生成时间
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 报告时间范围
    /// </summary>
    public TimeSpan TimeRange { get; set; }

    /// <summary>
    /// 整体健康状态
    /// </summary>
    public HealthStatus OverallHealth { get; set; }

    /// <summary>
    /// 客户端统计信息
    /// </summary>
    public ClientStatistics ClientStatistics { get; set; } = new();

    /// <summary>
    /// 连接统计信息
    /// </summary>
    public List<ConnectionStatistics> ConnectionStatistics { get; set; } = new();

    /// <summary>
    /// 路由统计信息
    /// </summary>
    public RoutingStatistics? RoutingStatistics { get; set; }

    /// <summary>
    /// 重试统计信息
    /// </summary>
    public List<RetryPolicyStatistics> RetryStatistics { get; set; } = new();

    /// <summary>
    /// 故障转移统计信息
    /// </summary>
    public FailoverStatistics? FailoverStatistics { get; set; }

    /// <summary>
    /// 活跃警报
    /// </summary>
    public List<MonitoringAlert> ActiveAlerts { get; set; } = new();

    /// <summary>
    /// 性能指标
    /// </summary>
    public List<MonitoringMetric> PerformanceMetrics { get; set; } = new();

    /// <summary>
    /// 健康检查结果
    /// </summary>
    public List<HealthCheckResult> HealthCheckResults { get; set; } = new();

    /// <summary>
    /// 建议和洞察
    /// </summary>
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// 连接统计信息
/// </summary>
public sealed class ConnectionStatistics
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 连接名称
    /// </summary>
    public string ConnectionName { get; set; } = string.Empty;

    /// <summary>
    /// 连接状态
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// 连接时长
    /// </summary>
    public TimeSpan ConnectedDuration { get; set; }

    /// <summary>
    /// 发送的消息数
    /// </summary>
    public long MessagesSent { get; set; }

    /// <summary>
    /// 接收的消息数
    /// </summary>
    public long MessagesReceived { get; set; }

    /// <summary>
    /// 发送的字节数
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// 接收的字节数
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }

    /// <summary>
    /// 错误计数
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivity { get; set; }
}

/// <summary>
/// 客户端监控器接口
/// </summary>
public interface IClientMonitor
{
    /// <summary>
    /// 监控器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// 监控配置
    /// </summary>
    MonitoringConfiguration Configuration { get; }

    /// <summary>
    /// 启动监控
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止监控
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录指标
    /// </summary>
    void RecordMetric(MonitoringMetric metric);

    /// <summary>
    /// 批量记录指标
    /// </summary>
    void RecordMetrics(IEnumerable<MonitoringMetric> metrics);

    /// <summary>
    /// 获取指标
    /// </summary>
    Task<IEnumerable<MonitoringMetric>> GetMetricsAsync(
        string? metricName = null,
        TimeSpan? timeRange = null,
        Dictionary<string, string>? labelFilters = null);

    /// <summary>
    /// 执行健康检查
    /// </summary>
    Task<IEnumerable<HealthCheckResult>> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取活跃警报
    /// </summary>
    IEnumerable<MonitoringAlert> GetActiveAlerts();

    /// <summary>
    /// 添加警报规则
    /// </summary>
    void AddAlertRule(AlertRule rule);

    /// <summary>
    /// 移除警报规则
    /// </summary>
    bool RemoveAlertRule(string ruleName);

    /// <summary>
    /// 生成监控报告
    /// </summary>
    Task<MonitoringReport> GenerateReportAsync(TimeSpan? timeRange = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出指标
    /// </summary>
    Task<string> ExportMetricsAsync(string format = "json", TimeSpan? timeRange = null);

    /// <summary>
    /// 清理过期数据
    /// </summary>
    Task CleanupExpiredDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 警报触发事件
    /// </summary>
    event EventHandler<MonitoringAlert>? AlertTriggered;

    /// <summary>
    /// 警报解决事件
    /// </summary>
    event EventHandler<MonitoringAlert>? AlertResolved;

    /// <summary>
    /// 指标记录事件
    /// </summary>
    event EventHandler<MonitoringMetric>? MetricRecorded;
}

/// <summary>
/// 监控器构建器接口
/// </summary>
public interface IClientMonitorBuilder
{
    /// <summary>
    /// 设置名称
    /// </summary>
    IClientMonitorBuilder WithName(string name);

    /// <summary>
    /// 设置监控级别
    /// </summary>
    IClientMonitorBuilder WithLevel(MonitoringLevel level);

    /// <summary>
    /// 设置指标收集间隔
    /// </summary>
    IClientMonitorBuilder WithMetricsInterval(TimeSpan interval);

    /// <summary>
    /// 设置健康检查间隔
    /// </summary>
    IClientMonitorBuilder WithHealthCheckInterval(TimeSpan interval);

    /// <summary>
    /// 设置数据保留时间
    /// </summary>
    IClientMonitorBuilder WithDataRetention(TimeSpan retention);

    /// <summary>
    /// 添加警报规则
    /// </summary>
    IClientMonitorBuilder WithAlertRule(AlertRule rule);

    /// <summary>
    /// 启用组件监控
    /// </summary>
    IClientMonitorBuilder EnableComponent(string componentName);

    /// <summary>
    /// 添加自定义指标提供者
    /// </summary>
    IClientMonitorBuilder WithCustomMetricProvider(Func<Task<IEnumerable<MonitoringMetric>>> provider);

    /// <summary>
    /// 添加自定义健康检查
    /// </summary>
    IClientMonitorBuilder WithCustomHealthCheck(Func<CancellationToken, Task<HealthCheckResult>> healthCheck);

    /// <summary>
    /// 构建监控器
    /// </summary>
    IClientMonitor Build();
}
