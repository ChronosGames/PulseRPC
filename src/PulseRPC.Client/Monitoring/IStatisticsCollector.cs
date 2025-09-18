using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Monitoring;

/// <summary>
/// 统计指标类型
/// </summary>
public enum StatisticsMetricType
{
    /// <summary>
    /// 计数器 - 累积值
    /// </summary>
    Counter,

    /// <summary>
    /// 计量器 - 瞬时值
    /// </summary>
    Gauge,

    /// <summary>
    /// 直方图 - 分布统计
    /// </summary>
    Histogram,

    /// <summary>
    /// 摘要 - 分位数统计
    /// </summary>
    Summary
}

/// <summary>
/// 统计指标
/// </summary>
public sealed class StatisticsMetric
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 指标类型
    /// </summary>
    public StatisticsMetricType Type { get; }

    /// <summary>
    /// 指标值
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// 标签
    /// </summary>
    public IReadOnlyDictionary<string, string> Labels { get; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// 单位
    /// </summary>
    public string? Unit { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public StatisticsMetric(
        string name,
        StatisticsMetricType type,
        double value,
        IReadOnlyDictionary<string, string>? labels = null,
        string? description = null,
        string? unit = null)
    {
        Name = name;
        Type = type;
        Value = value;
        Labels = labels ?? new Dictionary<string, string>();
        Timestamp = DateTime.UtcNow;
        Description = description;
        Unit = unit;
    }

    /// <summary>
    /// 创建计数器指标
    /// </summary>
    public static StatisticsMetric Counter(string name, double value, IReadOnlyDictionary<string, string>? labels = null, string? description = null)
        => new(name, StatisticsMetricType.Counter, value, labels, description);

    /// <summary>
    /// 创建计量器指标
    /// </summary>
    public static StatisticsMetric Gauge(string name, double value, IReadOnlyDictionary<string, string>? labels = null, string? description = null, string? unit = null)
        => new(name, StatisticsMetricType.Gauge, value, labels, description, unit);

    /// <summary>
    /// 创建直方图指标
    /// </summary>
    public static StatisticsMetric Histogram(string name, double value, IReadOnlyDictionary<string, string>? labels = null, string? description = null, string? unit = null)
        => new(name, StatisticsMetricType.Histogram, value, labels, description, unit);

    /// <summary>
    /// 创建摘要指标
    /// </summary>
    public static StatisticsMetric Summary(string name, double value, IReadOnlyDictionary<string, string>? labels = null, string? description = null, string? unit = null)
        => new(name, StatisticsMetricType.Summary, value, labels, description, unit);
}

/// <summary>
/// 时间序列数据点
/// </summary>
public sealed class TimeSeriesDataPoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 值
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// 标签
    /// </summary>
    public IReadOnlyDictionary<string, string> Labels { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public TimeSeriesDataPoint(double value, IReadOnlyDictionary<string, string>? labels = null)
    {
        Timestamp = DateTime.UtcNow;
        Value = value;
        Labels = labels ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public TimeSeriesDataPoint(DateTime timestamp, double value, IReadOnlyDictionary<string, string>? labels = null)
    {
        Timestamp = timestamp;
        Value = value;
        Labels = labels ?? new Dictionary<string, string>();
    }
}

/// <summary>
/// 时间序列
/// </summary>
public sealed class TimeSeries
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string MetricName { get; }

    /// <summary>
    /// 数据点
    /// </summary>
    public IReadOnlyList<TimeSeriesDataPoint> DataPoints { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public TimeSeries(string metricName, IReadOnlyList<TimeSeriesDataPoint> dataPoints)
    {
        MetricName = metricName;
        DataPoints = dataPoints;
    }
}

/// <summary>
/// 统计查询条件
/// </summary>
public sealed class StatisticsQuery
{
    /// <summary>
    /// 指标名称模式
    /// </summary>
    public string? MetricNamePattern { get; set; }

    /// <summary>
    /// 标签过滤器
    /// </summary>
    public Dictionary<string, string> LabelFilters { get; set; } = new();

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 聚合间隔
    /// </summary>
    public TimeSpan? AggregationInterval { get; set; }

    /// <summary>
    /// 最大数据点数量
    /// </summary>
    public int? MaxDataPoints { get; set; }

    /// <summary>
    /// 指标类型过滤器
    /// </summary>
    public StatisticsMetricType[]? MetricTypes { get; set; }
}

/// <summary>
/// 统计信息收集器接口
/// </summary>
public interface IStatisticsCollector
{
    /// <summary>
    /// 收集器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 收集间隔
    /// </summary>
    TimeSpan CollectionInterval { get; set; }

    /// <summary>
    /// 数据保留时间
    /// </summary>
    TimeSpan DataRetentionTime { get; set; }

    /// <summary>
    /// 记录指标
    /// </summary>
    void RecordMetric(StatisticsMetric metric);

    /// <summary>
    /// 记录计数器
    /// </summary>
    void RecordCounter(string name, double value, IReadOnlyDictionary<string, string>? labels = null);

    /// <summary>
    /// 记录计量器
    /// </summary>
    void RecordGauge(string name, double value, IReadOnlyDictionary<string, string>? labels = null);

    /// <summary>
    /// 记录直方图
    /// </summary>
    void RecordHistogram(string name, double value, IReadOnlyDictionary<string, string>? labels = null);

    /// <summary>
    /// 递增计数器
    /// </summary>
    void IncrementCounter(string name, double increment = 1.0, IReadOnlyDictionary<string, string>? labels = null);

    /// <summary>
    /// 设置计量器值
    /// </summary>
    void SetGauge(string name, double value, IReadOnlyDictionary<string, string>? labels = null);

    /// <summary>
    /// 获取当前指标
    /// </summary>
    IReadOnlyList<StatisticsMetric> GetCurrentMetrics();

    /// <summary>
    /// 查询时间序列数据
    /// </summary>
    Task<IReadOnlyList<TimeSeries>> QueryTimeSeriesAsync(StatisticsQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指标摘要
    /// </summary>
    Task<IReadOnlyDictionary<string, object>> GetSummaryAsync(TimeSpan? timeRange = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理过期数据
    /// </summary>
    Task CleanupExpiredDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动收集器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止收集器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 统计事件参数
/// </summary>
public sealed class StatisticsEventArgs : EventArgs
{
    /// <summary>
    /// 指标
    /// </summary>
    public StatisticsMetric Metric { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public StatisticsEventArgs(StatisticsMetric metric)
    {
        Metric = metric;
    }
}

/// <summary>
/// 统计信息聚合器接口
/// </summary>
public interface IStatisticsAggregator
{
    /// <summary>
    /// 聚合器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 支持的聚合函数
    /// </summary>
    IReadOnlyList<string> SupportedAggregations { get; }

    /// <summary>
    /// 聚合数据
    /// </summary>
    Task<IReadOnlyList<StatisticsMetric>> AggregateAsync(
        IReadOnlyList<StatisticsMetric> metrics,
        string aggregationFunction,
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算分位数
    /// </summary>
    Task<double> CalculateQuantileAsync(
        IReadOnlyList<StatisticsMetric> metrics,
        double quantile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算统计摘要
    /// </summary>
    Task<IReadOnlyDictionary<string, double>> CalculateSummaryStatisticsAsync(
        IReadOnlyList<StatisticsMetric> metrics,
        CancellationToken cancellationToken = default);
}
