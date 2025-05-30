using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Aggregators;

/// <summary>
/// 统计聚合器 - 专门计算各种统计指标
/// </summary>
public class StatisticalAggregator : IMetricsAggregator
{
    private readonly ILogger<StatisticalAggregator>? _logger;
    private readonly AggregatorConfiguration _configuration;
    private readonly ConcurrentDictionary<string, MetricAccumulator> _accumulators;
    private readonly Timer? _statisticsTimer;

    private PluginStatus _status = PluginStatus.NotInitialized;
    private long _totalAggregations = 0;
    private long _totalDataPoints = 0;
    private TimeSpan _totalAggregationTime = TimeSpan.Zero;
    private long _errorCount = 0;

    public StatisticalAggregator(
        AggregatorConfiguration? configuration = null,
        ILogger<StatisticalAggregator>? logger = null)
    {
        _configuration = configuration ?? new AggregatorConfiguration();
        _logger = logger;
        _accumulators = new ConcurrentDictionary<string, MetricAccumulator>();

        // 初始化统计计算定时器
        _statisticsTimer = new Timer(CalculatePeriodicStatistics, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    #region IMetricsPlugin Implementation

    public string Name => "StatisticalAggregator";
    public string Version => "1.0.0";
    public string Description => "Statistical aggregator for calculating mean, median, percentiles, and standard deviation";
    public string Author => "PulseRPC";
    public bool IsInitialized => _status >= PluginStatus.Initialized;
    public bool IsRunning => _status == PluginStatus.Running;

    public AggregatorConfiguration Configuration => _configuration;

    public event Action<PluginStatusChangedEventArgs>? StatusChanged;
    public event Action<PluginErrorEventArgs>? ErrorOccurred;
    public event Action<AggregationCompletedEventArgs>? AggregationCompleted;
    public event Action<WindowUpdatedEventArgs>? WindowUpdated;

    public Task<bool> ValidateConfigurationAsync(object? configuration)
    {
        if (configuration is not AggregatorConfiguration config)
            return Task.FromResult(false);

        // 验证百分位数配置
        if (config.Percentiles.Any(p => p < 0 || p > 100))
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("初始化统计聚合器");
            ChangeStatus(PluginStatus.Initialized);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "初始化失败");
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ChangeStatus(PluginStatus.Running);
        _logger?.LogInformation("统计聚合器已启动");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ChangeStatus(PluginStatus.Stopped);
        _logger?.LogInformation("统计聚合器已停止");
        return Task.CompletedTask;
    }

    public Task<PluginHealthStatus> GetHealthStatusAsync()
    {
        var accumulatorCount = _accumulators.Count;
        var memoryUsage = EstimateMemoryUsage();

        var isHealthy = _status == PluginStatus.Running &&
                       memoryUsage < _configuration.MaxMemoryUsage;

        var status = isHealthy
            ? PluginHealthStatus.Healthy($"统计聚合器运行正常，累计器数量: {accumulatorCount}")
            : PluginHealthStatus.Unhealthy("统计聚合器异常", $"状态: {_status}, 内存: {memoryUsage}bytes");

        status.Metrics["accumulator_count"] = accumulatorCount;
        status.Metrics["memory_usage"] = memoryUsage;
        status.Metrics["total_aggregations"] = _totalAggregations;
        status.Metrics["error_count"] = _errorCount;

        return Task.FromResult(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _statisticsTimer?.Dispose();
        ChangeStatus(PluginStatus.Disposed);
    }

    #endregion

    #region IMetricsAggregator Implementation

    public async Task<AggregationResult> AggregateMetricsAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new AggregationResult
        {
            AggregatorName = Name,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var metricsList = metrics.ToList();
            if (metricsList.Count == 0)
            {
                return result;
            }

            // 计算时间范围
            var timeRange = new TimeRange
            {
                StartTime = metricsList.Min(m => m.Timestamp),
                EndTime = metricsList.Max(m => m.Timestamp)
            };
            result.TimeWindow = timeRange;

            // 按指标名称分组处理
            var groupedMetrics = metricsList.GroupBy(m => m.MetricName).ToList();

            if (_configuration.EnableParallelProcessing && groupedMetrics.Count > 10)
            {
                // 并行处理多个指标组
                await ProcessMetricGroupsParallel(groupedMetrics, result, cancellationToken);
            }
            else
            {
                // 顺序处理
                await ProcessMetricGroupsSequential(groupedMetrics, result, cancellationToken);
            }

            result.ProcessedDataPoints = metricsList.Count;
            stopwatch.Stop();
            result.AggregationDuration = stopwatch.Elapsed;

            // 更新统计信息
            Interlocked.Increment(ref _totalAggregations);
            Interlocked.Add(ref _totalDataPoints, metricsList.Count);
            var currentTicks = _totalAggregationTime.Ticks;
            var newTicks = Interlocked.Add(ref currentTicks, stopwatch.Elapsed.Ticks);
            _totalAggregationTime = new TimeSpan(newTicks);

            // 触发事件
            AggregationCompleted?.Invoke(new AggregationCompletedEventArgs(result, Name));

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger?.LogError(ex, "统计聚合失败");
            OnError(ex, "统计聚合失败");
            throw;
        }
    }

    public async Task<AggregationResult> AggregateSnapshotsAsync(IEnumerable<JsonOptimizedMetricsSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        var allMetrics = new List<JsonOptimizedMetricsEvent>();

        foreach (var snapshot in snapshots)
        {
            allMetrics.AddRange(snapshot.Metrics.Values);
        }

        return await AggregateMetricsAsync(allMetrics, cancellationToken);
    }

    public Task<List<AggregationResult>> GetAggregatedResultsAsync(TimeRange? timeRange = null, IEnumerable<string>? metricNames = null)
    {
        var results = new List<AggregationResult>();

        try
        {
            var relevantAccumulators = _accumulators.Values;

            if (metricNames != null)
            {
                var nameSet = metricNames.ToHashSet();
                relevantAccumulators = relevantAccumulators.Where(a => nameSet.Contains(a.MetricName)).ToList();
            }

            foreach (var accumulator in relevantAccumulators)
            {
                var result = CreateAggregationResultFromAccumulator(accumulator, timeRange);
                if (result.AggregatedMetrics.Count > 0)
                {
                    results.Add(result);
                }
            }

            return Task.FromResult(results);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取聚合结果失败");
            return Task.FromResult(results);
        }
    }

    public Task<bool> ConfigureTimeWindowsAsync(TimeWindowConfiguration windowConfig)
    {
        // 统计聚合器主要关注统计配置而非时间窗口
        _logger?.LogInformation("统计聚合器配置更新完成");
        return Task.FromResult(true);
    }

    public Task ClearAggregatedDataAsync()
    {
        _accumulators.Clear();
        _totalAggregations = 0;
        _totalDataPoints = 0;
        _totalAggregationTime = TimeSpan.Zero;
        _errorCount = 0;

        _logger?.LogInformation("已清空所有统计数据");
        return Task.CompletedTask;
    }

    public Task<AggregatorStatistics> GetStatisticsAsync()
    {
        var stats = new AggregatorStatistics
        {
            TotalAggregations = _totalAggregations,
            TotalProcessedDataPoints = _totalDataPoints,
            AverageAggregationTime = _totalAggregations > 0
                ? TimeSpan.FromTicks(_totalAggregationTime.Ticks / _totalAggregations)
                : TimeSpan.Zero,
            ActiveWindows = _accumulators.Count,
            MemoryUsage = EstimateMemoryUsage(),
            LastAggregationTime = DateTime.UtcNow,
            ErrorCount = _errorCount
        };

        return Task.FromResult(stats);
    }

    #endregion

    #region Private Methods - Data Processing

    private async Task ProcessMetricGroupsParallel(IEnumerable<IGrouping<string, JsonOptimizedMetricsEvent>> groupedMetrics, AggregationResult result, CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _configuration.ParallelismDegree
        };

        await Parallel.ForEachAsync(groupedMetrics, parallelOptions, async (group, ct) =>
        {
            var aggregatedMetric = await ProcessMetricGroup(group.Key, group.ToList(), ct);
            lock (result.AggregatedMetrics)
            {
                result.AddAggregatedMetric(group.Key, aggregatedMetric);
            }
        });
    }

    private async Task ProcessMetricGroupsSequential(IEnumerable<IGrouping<string, JsonOptimizedMetricsEvent>> groupedMetrics, AggregationResult result, CancellationToken cancellationToken)
    {
        foreach (var group in groupedMetrics)
        {
            var aggregatedMetric = await ProcessMetricGroup(group.Key, group.ToList(), cancellationToken);
            result.AddAggregatedMetric(group.Key, aggregatedMetric);

            if (cancellationToken.IsCancellationRequested)
                break;
        }
    }

    private async Task<AggregatedMetric> ProcessMetricGroup(string metricName, List<JsonOptimizedMetricsEvent> events, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            // 获取或创建累计器
            var accumulator = _accumulators.GetOrAdd(metricName, _ => new MetricAccumulator(metricName));

            // 提取数值
            var values = ExtractNumericValues(events);
            if (values.Count == 0)
            {
                return new AggregatedMetric { Name = metricName };
            }

            // 更新累计器
            accumulator.AddValues(values);

            // 计算统计指标
            return CalculateStatistics(accumulator, events.First());
        }, cancellationToken);
    }

    private List<double> ExtractNumericValues(List<JsonOptimizedMetricsEvent> events)
    {
        var values = new List<double>();

        foreach (var eventItem in events)
        {
            var stringValue = eventItem.GetStringValue();
            if (double.TryParse(stringValue, out var value) && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private AggregatedMetric CalculateStatistics(MetricAccumulator accumulator, JsonOptimizedMetricsEvent sampleEvent)
    {
        var values = accumulator.GetSortedValues();

        var aggregated = new AggregatedMetric
        {
            Name = accumulator.MetricName,
            Count = values.Count,
            Unit = sampleEvent.Unit ?? "",
            Tags = new Dictionary<string, string>(sampleEvent.Tags)
        };

        if (values.Count == 0)
            return aggregated;

        // 基本统计量
        aggregated.Min = values.First();
        aggregated.Max = values.Last();
        aggregated.Sum = accumulator.Sum;
        aggregated.Average = accumulator.Mean;

        // 中位数
        aggregated.Median = CalculatePercentile(values, 50);

        // 标准差
        aggregated.StandardDeviation = accumulator.StandardDeviation;

        // 百分位数
        if (_configuration.EnableStatistics)
        {
            foreach (var percentile in _configuration.Percentiles)
            {
                aggregated.Percentiles[percentile] = CalculatePercentile(values, percentile);
            }
        }

        return aggregated;
    }

    private double CalculatePercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (percentile <= 0) return sortedValues[0];
        if (percentile >= 100) return sortedValues[^1];

        var index = (percentile / 100.0) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var weight = index - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }

    private AggregationResult CreateAggregationResultFromAccumulator(MetricAccumulator accumulator, TimeRange? timeRange)
    {
        var result = new AggregationResult
        {
            AggregatorName = Name,
            Timestamp = DateTime.UtcNow,
            TimeWindow = timeRange ?? new TimeRange { StartTime = DateTime.MinValue, EndTime = DateTime.MaxValue }
        };

        // 创建虚拟事件用于统计计算
        var sampleEvent = new JsonOptimizedMetricsEvent
        {
            MetricName = accumulator.MetricName,
            Unit = "",
            Tags = new Dictionary<string, string>()
        };

        var aggregatedMetric = CalculateStatistics(accumulator, sampleEvent);
        result.AddAggregatedMetric(accumulator.MetricName, aggregatedMetric);

        return result;
    }

    #endregion

    #region Private Methods - Maintenance

    private void CalculatePeriodicStatistics(object? state)
    {
        if (!IsRunning) return;

        try
        {
            var accumulatorCount = _accumulators.Count;
            var totalValues = _accumulators.Values.Sum(a => a.Count);

            _logger?.LogDebug("定期统计: {AccumulatorCount} 个累计器, 总计 {TotalValues} 个数值",
                accumulatorCount, totalValues);

            // 清理超过内存限制的旧数据
            if (EstimateMemoryUsage() > _configuration.MaxMemoryUsage)
            {
                CleanupOldAccumulators();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "定期统计计算失败");
        }
    }

    private void CleanupOldAccumulators()
    {
        try
        {
            var sortedAccumulators = _accumulators.Values
                .OrderBy(a => a.LastUpdated)
                .ToList();

            var removeCount = Math.Max(1, sortedAccumulators.Count / 10); // 移除最旧的10%

            for (int i = 0; i < removeCount; i++)
            {
                var accumulator = sortedAccumulators[i];
                if (_accumulators.TryRemove(accumulator.MetricName, out _))
                {
                    _logger?.LogDebug("清理旧累计器: {MetricName}", accumulator.MetricName);
                }
            }

            _logger?.LogInformation("清理了 {Count} 个旧累计器", removeCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "清理累计器失败");
        }
    }

    private long EstimateMemoryUsage()
    {
        long totalSize = 0;

        foreach (var accumulator in _accumulators.Values)
        {
            totalSize += accumulator.EstimateMemoryUsage();
        }

        return totalSize;
    }

    private void ChangeStatus(PluginStatus newStatus)
    {
        var oldStatus = _status;
        _status = newStatus;

        if (oldStatus != newStatus)
        {
            StatusChanged?.Invoke(new PluginStatusChangedEventArgs(Name, oldStatus, newStatus));
        }
    }

    private void OnError(Exception exception, string context)
    {
        ErrorOccurred?.Invoke(new PluginErrorEventArgs(Name, exception, ErrorLevel.Error, context));
    }

    #endregion
}

/// <summary>
/// 指标累计器 - 支持增量统计计算
/// </summary>
internal class MetricAccumulator
{
    private readonly List<double> _values;
    private readonly object _lock = new();
    private bool _isSorted = true;

    public string MetricName { get; }
    public int Count { get; private set; }
    public double Sum { get; private set; }
    public double Mean { get; private set; }
    public double M2 { get; private set; } // 用于计算方差的累计量
    public DateTime LastUpdated { get; private set; }

    public MetricAccumulator(string metricName)
    {
        MetricName = metricName;
        _values = new List<double>();
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// 添加新数值（支持增量统计计算）
    /// </summary>
    public void AddValues(IEnumerable<double> values)
    {
        lock (_lock)
        {
            foreach (var value in values)
            {
                AddValue(value);
            }
            LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 添加单个数值
    /// </summary>
    private void AddValue(double value)
    {
        _values.Add(value);
        _isSorted = false;

        // 增量计算均值和方差 (Welford's method)
        Count++;
        var delta = value - Mean;
        Mean += delta / Count;
        var delta2 = value - Mean;
        M2 += delta * delta2;
        Sum += value;
    }

    /// <summary>
    /// 获取排序后的数值列表
    /// </summary>
    public List<double> GetSortedValues()
    {
        lock (_lock)
        {
            if (!_isSorted)
            {
                _values.Sort();
                _isSorted = true;
            }
            return new List<double>(_values);
        }
    }

    /// <summary>
    /// 计算标准差
    /// </summary>
    public double StandardDeviation
    {
        get
        {
            if (Count < 2) return 0;
            return Math.Sqrt(M2 / (Count - 1));
        }
    }

    /// <summary>
    /// 计算方差
    /// </summary>
    public double Variance
    {
        get
        {
            if (Count < 2) return 0;
            return M2 / (Count - 1);
        }
    }

    /// <summary>
    /// 估算内存使用量
    /// </summary>
    public long EstimateMemoryUsage()
    {
        return Count * sizeof(double) + MetricName.Length * sizeof(char);
    }
}
