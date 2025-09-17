using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core.Monitoring;

/// <summary>
/// 统计信息收集器
/// </summary>
public sealed class StatisticsCollector : IStatisticsCollector, IDisposable
{
    private readonly ConcurrentDictionary<string, StatisticsMetric> _currentMetrics = new();
    private readonly ConcurrentQueue<StatisticsMetric> _timeSeriesData = new();
    private readonly ILogger<StatisticsCollector> _logger;
    private readonly Timer? _cleanupTimer;
    private readonly object _lockObject = new();
    private volatile bool _disposed;
    private volatile bool _isRunning;

    /// <summary>
    /// 收集器名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning && !_disposed;

    /// <summary>
    /// 收集间隔
    /// </summary>
    public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 数据保留时间
    /// </summary>
    public TimeSpan DataRetentionTime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 构造函数
    /// </summary>
    public StatisticsCollector(
        string name,
        ILogger<StatisticsCollector>? logger = null)
    {
        Name = name;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsCollector>.Instance;

        // 创建清理定时器
        _cleanupTimer = new Timer(
            callback: async _ => await CleanupExpiredDataAsync(),
            state: null,
            dueTime: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5));

        _logger.LogDebug("统计信息收集器已创建: {CollectorName}", Name);
    }

    /// <summary>
    /// 记录指标
    /// </summary>
    public void RecordMetric(StatisticsMetric metric)
    {
        if (metric == null) throw new ArgumentNullException(nameof(metric));

        if (_disposed)
        {
            return;
        }

        try
        {
            // 生成指标键
            var key = GenerateMetricKey(metric.Name, metric.Labels);

            // 更新当前指标
            _currentMetrics.AddOrUpdate(key, metric, (_, existing) =>
            {
                // 对于计数器类型，累加值
                if (metric.Type == StatisticsMetricType.Counter && existing.Type == StatisticsMetricType.Counter)
                {
                    return new StatisticsMetric(
                        metric.Name,
                        metric.Type,
                        existing.Value + metric.Value,
                        metric.Labels,
                        metric.Description,
                        metric.Unit);
                }

                // 对于其他类型，直接替换
                return metric;
            });

            // 添加到时间序列数据
            _timeSeriesData.Enqueue(metric);

            _logger.LogTrace("记录指标: {MetricName} = {Value} ({Type})", metric.Name, metric.Value, metric.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录指标失败: {MetricName}", metric.Name);
        }
    }

    /// <summary>
    /// 记录计数器
    /// </summary>
    public void RecordCounter(string name, double value, IReadOnlyDictionary<string, string>? labels = null)
    {
        var metric = StatisticsMetric.Counter(name, value, labels);
        RecordMetric(metric);
    }

    /// <summary>
    /// 记录计量器
    /// </summary>
    public void RecordGauge(string name, double value, IReadOnlyDictionary<string, string>? labels = null)
    {
        var metric = StatisticsMetric.Gauge(name, value, labels);
        RecordMetric(metric);
    }

    /// <summary>
    /// 记录直方图
    /// </summary>
    public void RecordHistogram(string name, double value, IReadOnlyDictionary<string, string>? labels = null)
    {
        var metric = StatisticsMetric.Histogram(name, value, labels);
        RecordMetric(metric);
    }

    /// <summary>
    /// 递增计数器
    /// </summary>
    public void IncrementCounter(string name, double increment = 1.0, IReadOnlyDictionary<string, string>? labels = null)
    {
        RecordCounter(name, increment, labels);
    }

    /// <summary>
    /// 设置计量器值
    /// </summary>
    public void SetGauge(string name, double value, IReadOnlyDictionary<string, string>? labels = null)
    {
        RecordGauge(name, value, labels);
    }

    /// <summary>
    /// 获取当前指标
    /// </summary>
    public IReadOnlyList<StatisticsMetric> GetCurrentMetrics()
    {
        if (_disposed)
        {
            return Array.Empty<StatisticsMetric>();
        }

        return _currentMetrics.Values.ToList();
    }

    /// <summary>
    /// 查询时间序列数据
    /// </summary>
    public async Task<IReadOnlyList<TimeSeries>> QueryTimeSeriesAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Array.Empty<TimeSeries>();
        }

        await Task.Yield(); // 异步操作

        try
        {
            var allData = _timeSeriesData.ToList();

            // 应用过滤器
            var filteredData = FilterMetrics(allData, query);

            // 按指标名称分组
            var groupedData = filteredData
                .GroupBy(m => m.Name)
                .ToDictionary(g => g.Key, g => g.ToList());

            var timeSeries = new List<TimeSeries>();

            foreach (var kvp in groupedData)
            {
                var metricName = kvp.Key;
                var metrics = kvp.Value;

                // 应用聚合间隔
                var dataPoints = query.AggregationInterval.HasValue
                    ? AggregateDataPoints(metrics, query.AggregationInterval.Value)
                    : metrics.Select(m => new TimeSeriesDataPoint(m.Timestamp, m.Value, m.Labels)).ToList();

                // 限制数据点数量
                if (query.MaxDataPoints.HasValue && dataPoints.Count > query.MaxDataPoints.Value)
                {
                    var step = dataPoints.Count / query.MaxDataPoints.Value;
                    dataPoints = dataPoints.Where((_, index) => index % step == 0).ToList();
                }

                timeSeries.Add(new TimeSeries(metricName, dataPoints));
            }

            _logger.LogDebug("查询时间序列数据完成: 指标数量: {MetricCount}, 总数据点: {DataPointCount}",
                timeSeries.Count, timeSeries.Sum(ts => ts.DataPoints.Count));

            return timeSeries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询时间序列数据失败");
            return Array.Empty<TimeSeries>();
        }
    }

    /// <summary>
    /// 获取指标摘要
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object>> GetSummaryAsync(TimeSpan? timeRange = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new Dictionary<string, object>();
        }

        await Task.Yield(); // 异步操作

        try
        {
            var summary = new Dictionary<string, object>();
            var cutoffTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : DateTime.MinValue;

            var relevantMetrics = _timeSeriesData
                .Where(m => m.Timestamp >= cutoffTime)
                .ToList();

            // 基本统计
            summary["TotalMetrics"] = relevantMetrics.Count;
            summary["UniqueMetricNames"] = relevantMetrics.Select(m => m.Name).Distinct().Count();
            summary["TimeRange"] = timeRange?.ToString() ?? "All Time";
            summary["EarliestTimestamp"] = relevantMetrics.Any() ? relevantMetrics.Min(m => m.Timestamp) : DateTime.MinValue;
            summary["LatestTimestamp"] = relevantMetrics.Any() ? relevantMetrics.Max(m => m.Timestamp) : DateTime.MinValue;

            // 按类型分组统计
            var typeGroups = relevantMetrics.GroupBy(m => m.Type).ToDictionary(g => g.Key.ToString(), g => g.Count());
            summary["MetricsByType"] = typeGroups;

            // 按指标名称统计
            var nameGroups = relevantMetrics
                .GroupBy(m => m.Name)
                .ToDictionary(g => g.Key, g => new
                {
                    Count = g.Count(),
                    LatestValue = g.OrderByDescending(m => m.Timestamp).First().Value,
                    MinValue = g.Min(m => m.Value),
                    MaxValue = g.Max(m => m.Value),
                    AverageValue = g.Average(m => m.Value)
                });
            summary["MetricsByName"] = nameGroups;

            // 当前活跃指标
            summary["CurrentActiveMetrics"] = _currentMetrics.Count;

            _logger.LogDebug("生成指标摘要完成: 指标数量: {MetricCount}", relevantMetrics.Count);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成指标摘要失败");
            return new Dictionary<string, object> { ["Error"] = ex.Message };
        }
    }

    /// <summary>
    /// 清理过期数据
    /// </summary>
    public async Task CleanupExpiredDataAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        try
        {
            var cutoffTime = DateTime.UtcNow - DataRetentionTime;
            var removedCount = 0;

            // 清理时间序列数据
            var tempQueue = new Queue<StatisticsMetric>();

            while (_timeSeriesData.TryDequeue(out var metric))
            {
                if (metric.Timestamp >= cutoffTime)
                {
                    tempQueue.Enqueue(metric);
                }
                else
                {
                    removedCount++;
                }
            }

            // 将未过期的数据放回队列
            while (tempQueue.Count > 0)
            {
                _timeSeriesData.Enqueue(tempQueue.Dequeue());
            }

            if (removedCount > 0)
            {
                _logger.LogDebug("清理过期统计数据: 移除 {RemovedCount} 条记录", removedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期数据失败");
        }
    }

    /// <summary>
    /// 启动收集器
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(StatisticsCollector));
        }

        if (_isRunning)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        _isRunning = true;
        _logger.LogInformation("统计信息收集器已启动: {CollectorName}", Name);
    }

    /// <summary>
    /// 停止收集器
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        _isRunning = false;
        _logger.LogInformation("统计信息收集器已停止: {CollectorName}", Name);
    }

    /// <summary>
    /// 生成指标键
    /// </summary>
    private static string GenerateMetricKey(string name, IReadOnlyDictionary<string, string> labels)
    {
        if (!labels.Any())
        {
            return name;
        }

        var labelString = string.Join(",", labels.OrderBy(l => l.Key).Select(l => $"{l.Key}={l.Value}"));
        return $"{name}{{{labelString}}}";
    }

    /// <summary>
    /// 过滤指标
    /// </summary>
    private static List<StatisticsMetric> FilterMetrics(List<StatisticsMetric> metrics, StatisticsQuery query)
    {
        var filtered = metrics.AsEnumerable();

        // 指标名称模式过滤
        if (!string.IsNullOrEmpty(query.MetricNamePattern))
        {
            filtered = filtered.Where(m => m.Name.Contains(query.MetricNamePattern, StringComparison.OrdinalIgnoreCase));
        }

        // 时间范围过滤
        if (query.StartTime.HasValue)
        {
            filtered = filtered.Where(m => m.Timestamp >= query.StartTime.Value);
        }

        if (query.EndTime.HasValue)
        {
            filtered = filtered.Where(m => m.Timestamp <= query.EndTime.Value);
        }

        // 标签过滤
        if (query.LabelFilters.Any())
        {
            filtered = filtered.Where(m =>
                query.LabelFilters.All(filter =>
                    m.Labels.TryGetValue(filter.Key, out var value) && value == filter.Value));
        }

        // 指标类型过滤
        if (query.MetricTypes != null && query.MetricTypes.Length > 0)
        {
            filtered = filtered.Where(m => query.MetricTypes.Contains(m.Type));
        }

        return filtered.ToList();
    }

    /// <summary>
    /// 聚合数据点
    /// </summary>
    private static List<TimeSeriesDataPoint> AggregateDataPoints(List<StatisticsMetric> metrics, TimeSpan interval)
    {
        if (!metrics.Any())
        {
            return new List<TimeSeriesDataPoint>();
        }

        var groups = metrics
            .GroupBy(m => new DateTime((m.Timestamp.Ticks / interval.Ticks) * interval.Ticks))
            .OrderBy(g => g.Key);

        var dataPoints = new List<TimeSeriesDataPoint>();

        foreach (var group in groups)
        {
            var timestamp = group.Key;
            var values = group.ToList();

            // 计算聚合值（这里使用平均值，可以根据指标类型选择不同的聚合方法）
            var aggregatedValue = values.Average(v => v.Value);

            // 合并标签
            var mergedLabels = values
                .SelectMany(v => v.Labels)
                .GroupBy(l => l.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);

            dataPoints.Add(new TimeSeriesDataPoint(timestamp, aggregatedValue, mergedLabels));
        }

        return dataPoints;
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

        _logger.LogInformation("正在关闭统计信息收集器: {CollectorName}", Name);

        _cleanupTimer?.Dispose();

        _currentMetrics.Clear();

        while (_timeSeriesData.TryDequeue(out _))
        {
            // 清空队列
        }

        _logger.LogInformation("统计信息收集器已关闭: {CollectorName}", Name);
    }
}
