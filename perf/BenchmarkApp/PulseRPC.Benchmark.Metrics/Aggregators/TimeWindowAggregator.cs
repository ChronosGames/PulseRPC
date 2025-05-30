using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Aggregators;

/// <summary>
/// 时间窗口聚合器 - 支持滑动窗口、固定窗口和会话窗口
/// </summary>
public class TimeWindowAggregator : IMetricsAggregator
{
    private readonly ILogger<TimeWindowAggregator>? _logger;
    private readonly AggregatorConfiguration _configuration;
    private readonly ConcurrentDictionary<string, TimeWindow> _activeWindows;
    private readonly Timer? _cleanupTimer;
    private readonly object _windowLock = new();

    private PluginStatus _status = PluginStatus.NotInitialized;
    private long _totalAggregations = 0;
    private long _totalDataPoints = 0;
    private TimeSpan _totalAggregationTime = TimeSpan.Zero;
    private long _errorCount = 0;

    public TimeWindowAggregator(
        AggregatorConfiguration? configuration = null,
        ILogger<TimeWindowAggregator>? logger = null)
    {
        _configuration = configuration ?? new AggregatorConfiguration();
        _logger = logger;
        _activeWindows = new ConcurrentDictionary<string, TimeWindow>();

        // 初始化清理定时器
        if (_configuration.DefaultWindowConfig.AutoCleanup)
        {
            _cleanupTimer = new Timer(CleanupExpiredWindows, null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
    }

    #region IMetricsPlugin Implementation

    public string Name => "TimeWindowAggregator";
    public string Version => "1.0.0";
    public string Description => "Time window aggregator supporting sliding, fixed and session windows";
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

        // 验证时间窗口配置
        if (config.DefaultWindowConfig.WindowSize <= TimeSpan.Zero)
            return Task.FromResult(false);

        if (config.DefaultWindowConfig.SlideInterval <= TimeSpan.Zero)
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("初始化时间窗口聚合器");
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
        _logger?.LogInformation("时间窗口聚合器已启动");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ChangeStatus(PluginStatus.Stopped);
        _logger?.LogInformation("时间窗口聚合器已停止");
        return Task.CompletedTask;
    }

    public Task<PluginHealthStatus> GetHealthStatusAsync()
    {
        var activeWindowCount = _activeWindows.Count;
        var memoryUsage = EstimateMemoryUsage();

        var isHealthy = _status == PluginStatus.Running &&
                       memoryUsage < _configuration.MaxMemoryUsage &&
                       activeWindowCount < _configuration.DefaultWindowConfig.MaxWindows;

        var status = isHealthy
            ? PluginHealthStatus.Healthy($"聚合器运行正常，活跃窗口: {activeWindowCount}")
            : PluginHealthStatus.Unhealthy("聚合器异常", $"状态: {_status}, 内存: {memoryUsage}bytes, 窗口: {activeWindowCount}");

        status.Metrics["active_windows"] = activeWindowCount;
        status.Metrics["memory_usage"] = memoryUsage;
        status.Metrics["total_aggregations"] = _totalAggregations;
        status.Metrics["error_count"] = _errorCount;

        return Task.FromResult(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cleanupTimer?.Dispose();
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

            // 按时间范围分组指标
            var timeRange = CalculateTimeRange(metricsList);
            result.TimeWindow = timeRange;

            // 获取或创建适当的窗口
            var windows = GetOrCreateWindows(timeRange);

            // 将指标分配到相应的窗口
            await DistributeMetricsToWindows(metricsList, windows, cancellationToken);

            // 聚合每个窗口的数据
            await AggregateWindowData(result, windows, cancellationToken);

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
            _logger?.LogError(ex, "聚合指标失败");
            OnError(ex, "聚合指标失败");
            throw;
        }
    }

    public async Task<AggregationResult> AggregateSnapshotsAsync(IEnumerable<JsonOptimizedMetricsSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new AggregationResult
        {
            AggregatorName = Name,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var snapshotsList = snapshots.ToList();
            var allMetrics = new List<JsonOptimizedMetricsEvent>();

            // 从快照中提取所有指标
            foreach (var snapshot in snapshotsList)
            {
                allMetrics.AddRange(snapshot.Metrics.Values);
            }

            if (allMetrics.Count > 0)
            {
                result = await AggregateMetricsAsync(allMetrics, cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger?.LogError(ex, "聚合快照失败");
            OnError(ex, "聚合快照失败");
            throw;
        }
    }

    public Task<List<AggregationResult>> GetAggregatedResultsAsync(TimeRange? timeRange = null, IEnumerable<string>? metricNames = null)
    {
        var results = new List<AggregationResult>();

        try
        {
            var relevantWindows = _activeWindows.Values
                .Where(w => timeRange == null || w.TimeRange.Overlaps(timeRange))
                .ToList();

            foreach (var window in relevantWindows)
            {
                var result = CreateAggregationResult(window, metricNames);
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
        try
        {
            // 更新配置
            _configuration.DefaultWindowConfig = windowConfig;

            // 清理现有窗口（如果配置发生重大变化）
            if (windowConfig.WindowType != _configuration.DefaultWindowConfig.WindowType)
            {
                _activeWindows.Clear();
            }

            _logger?.LogInformation("时间窗口配置已更新: {WindowType}, 大小: {WindowSize}, 滑动: {SlideInterval}",
                windowConfig.WindowType, windowConfig.WindowSize, windowConfig.SlideInterval);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "配置时间窗口失败");
            OnError(ex, "配置时间窗口失败");
            return Task.FromResult(false);
        }
    }

    public Task ClearAggregatedDataAsync()
    {
        lock (_windowLock)
        {
            _activeWindows.Clear();
            _totalAggregations = 0;
            _totalDataPoints = 0;
            _totalAggregationTime = TimeSpan.Zero;
            _errorCount = 0;
        }

        _logger?.LogInformation("已清空所有聚合数据");
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
            ActiveWindows = _activeWindows.Count,
            MemoryUsage = EstimateMemoryUsage(),
            LastAggregationTime = DateTime.UtcNow,
            ErrorCount = _errorCount
        };

        return Task.FromResult(stats);
    }

    #endregion

    #region Private Methods - Window Management

    private TimeRange CalculateTimeRange(List<JsonOptimizedMetricsEvent> metrics)
    {
        var timestamps = metrics.Select(m => m.Timestamp).ToList();
        return new TimeRange
        {
            StartTime = timestamps.Min(),
            EndTime = timestamps.Max()
        };
    }

    private List<TimeWindow> GetOrCreateWindows(TimeRange timeRange)
    {
        var windows = new List<TimeWindow>();
        var config = _configuration.DefaultWindowConfig;

        switch (config.WindowType)
        {
            case WindowType.Fixed:
                windows.AddRange(CreateFixedWindows(timeRange, config));
                break;

            case WindowType.Sliding:
                windows.AddRange(CreateSlidingWindows(timeRange, config));
                break;

            case WindowType.Session:
                windows.Add(CreateSessionWindow(timeRange, config));
                break;
        }

        return windows;
    }

    private IEnumerable<TimeWindow> CreateFixedWindows(TimeRange timeRange, TimeWindowConfiguration config)
    {
        var windowStart = AlignToWindowBoundary(timeRange.StartTime, config.WindowSize);
        var windows = new List<TimeWindow>();

        while (windowStart < timeRange.EndTime)
        {
            var windowEnd = windowStart.Add(config.WindowSize);
            var windowId = $"fixed_{windowStart:yyyyMMdd_HHmmss}";

            var window = GetOrCreateWindow(windowId, new TimeRange
            {
                StartTime = windowStart,
                EndTime = windowEnd
            });

            windows.Add(window);
            windowStart = windowEnd;
        }

        return windows;
    }

    private IEnumerable<TimeWindow> CreateSlidingWindows(TimeRange timeRange, TimeWindowConfiguration config)
    {
        var windowStart = timeRange.StartTime;
        var windows = new List<TimeWindow>();

        while (windowStart < timeRange.EndTime)
        {
            var windowEnd = windowStart.Add(config.WindowSize);
            var windowId = $"sliding_{windowStart:yyyyMMdd_HHmmss}";

            var window = GetOrCreateWindow(windowId, new TimeRange
            {
                StartTime = windowStart,
                EndTime = windowEnd
            });

            windows.Add(window);
            windowStart = windowStart.Add(config.SlideInterval);
        }

        return windows;
    }

    private TimeWindow CreateSessionWindow(TimeRange timeRange, TimeWindowConfiguration config)
    {
        var windowId = $"session_{timeRange.StartTime:yyyyMMdd_HHmmss}";
        return GetOrCreateWindow(windowId, timeRange);
    }

    private TimeWindow GetOrCreateWindow(string windowId, TimeRange timeRange)
    {
        return _activeWindows.GetOrAdd(windowId, _ =>
        {
            var window = new TimeWindow
            {
                Id = windowId,
                TimeRange = timeRange,
                CreatedAt = DateTime.UtcNow,
                Metrics = new ConcurrentDictionary<string, List<JsonOptimizedMetricsEvent>>()
            };

            WindowUpdated?.Invoke(new WindowUpdatedEventArgs(timeRange, WindowUpdateType.Created, Name));
            _logger?.LogDebug("创建新窗口: {WindowId}, 范围: {StartTime} - {EndTime}",
                windowId, timeRange.StartTime, timeRange.EndTime);

            return window;
        });
    }

    private DateTime AlignToWindowBoundary(DateTime timestamp, TimeSpan windowSize)
    {
        var ticks = timestamp.Ticks;
        var windowTicks = windowSize.Ticks;
        var alignedTicks = (ticks / windowTicks) * windowTicks;
        return new DateTime(alignedTicks);
    }

    #endregion

    #region Private Methods - Data Processing

    private async Task DistributeMetricsToWindows(List<JsonOptimizedMetricsEvent> metrics, List<TimeWindow> windows, CancellationToken cancellationToken)
    {
        if (_configuration.EnableParallelProcessing && metrics.Count > 1000)
        {
            // 并行处理大量数据
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _configuration.ParallelismDegree
            };

            await Parallel.ForEachAsync(metrics, parallelOptions, async (metric, ct) =>
            {
                await Task.Run(() =>
                {
                    foreach (var window in windows)
                    {
                        if (window.TimeRange.Contains(metric.Timestamp))
                        {
                            AddMetricToWindow(window, metric);
                        }
                    }
                }, ct);
            });
        }
        else
        {
            // 顺序处理小量数据
            foreach (var metric in metrics)
            {
                foreach (var window in windows)
                {
                    if (window.TimeRange.Contains(metric.Timestamp))
                    {
                        AddMetricToWindow(window, metric);
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }
    }

    private void AddMetricToWindow(TimeWindow window, JsonOptimizedMetricsEvent metric)
    {
        window.Metrics.AddOrUpdate(
            metric.MetricName,
            new List<JsonOptimizedMetricsEvent> { metric },
            (key, existing) =>
            {
                lock (existing)
                {
                    existing.Add(metric);
                    return existing;
                }
            });
    }

    private async Task AggregateWindowData(AggregationResult result, List<TimeWindow> windows, CancellationToken cancellationToken)
    {
        var allAggregatedMetrics = new Dictionary<string, List<AggregatedMetric>>();

        foreach (var window in windows)
        {
            foreach (var metricGroup in window.Metrics)
            {
                var metricName = metricGroup.Key;
                var metricEvents = metricGroup.Value;

                if (metricEvents.Count == 0) continue;

                var aggregatedMetric = await CalculateAggregatedMetric(metricName, metricEvents, cancellationToken);

                if (!allAggregatedMetrics.ContainsKey(metricName))
                {
                    allAggregatedMetrics[metricName] = new List<AggregatedMetric>();
                }
                allAggregatedMetrics[metricName].Add(aggregatedMetric);
            }
        }

        // 合并同名指标的聚合结果
        foreach (var metricGroup in allAggregatedMetrics)
        {
            var combinedMetric = CombineAggregatedMetrics(metricGroup.Key, metricGroup.Value);
            result.AddAggregatedMetric(metricGroup.Key, combinedMetric);
        }
    }

    private async Task<AggregatedMetric> CalculateAggregatedMetric(string metricName, List<JsonOptimizedMetricsEvent> events, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var values = new List<double>();
            var firstEvent = events.First();

            foreach (var eventItem in events)
            {
                if (double.TryParse(eventItem.GetStringValue(), out var value))
                {
                    values.Add(value);
                }
            }

            if (values.Count == 0)
            {
                return new AggregatedMetric { Name = metricName };
            }

            values.Sort();

            var aggregated = new AggregatedMetric
            {
                Name = metricName,
                Count = values.Count,
                Min = values.Min(),
                Max = values.Max(),
                Sum = values.Sum(),
                Average = values.Average(),
                Unit = firstEvent.Unit ?? "",
                Tags = new Dictionary<string, string>(firstEvent.Tags)
            };

            // 计算中位数
            aggregated.Median = CalculatePercentile(values, 50);

            // 计算标准差
            var variance = values.Sum(v => Math.Pow(v - aggregated.Average, 2)) / values.Count;
            aggregated.StandardDeviation = Math.Sqrt(variance);

            // 计算百分位数
            if (_configuration.EnableStatistics)
            {
                foreach (var percentile in _configuration.Percentiles)
                {
                    aggregated.Percentiles[percentile] = CalculatePercentile(values, percentile);
                }
            }

            return aggregated;
        }, cancellationToken);
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

    private AggregatedMetric CombineAggregatedMetrics(string metricName, List<AggregatedMetric> metrics)
    {
        if (metrics.Count == 1)
            return metrics[0];

        var combined = new AggregatedMetric
        {
            Name = metricName,
            Count = metrics.Sum(m => m.Count),
            Sum = metrics.Sum(m => m.Sum),
            Min = metrics.Min(m => m.Min),
            Max = metrics.Max(m => m.Max),
            Unit = metrics.First().Unit,
            Tags = new Dictionary<string, string>(metrics.First().Tags)
        };

        // 重新计算平均值
        combined.Average = combined.Count > 0 ? combined.Sum / combined.Count : 0;

        // 合并百分位数（简化处理，取平均值）
        var allPercentileKeys = metrics.SelectMany(m => m.Percentiles.Keys).Distinct();
        foreach (var percentile in allPercentileKeys)
        {
            var values = metrics.Where(m => m.Percentiles.ContainsKey(percentile))
                              .Select(m => m.Percentiles[percentile]);
            combined.Percentiles[percentile] = values.Average();
        }

        return combined;
    }

    private AggregationResult CreateAggregationResult(TimeWindow window, IEnumerable<string>? metricNames)
    {
        var result = new AggregationResult
        {
            AggregatorName = Name,
            Timestamp = DateTime.UtcNow,
            TimeWindow = window.TimeRange
        };

        var relevantMetrics = metricNames == null
            ? window.Metrics.ToList()
            : window.Metrics.Where(kvp => metricNames.Contains(kvp.Key)).ToList();

        foreach (var metricGroup in relevantMetrics)
        {
            if (metricGroup.Value.Count > 0)
            {
                var aggregated = CalculateAggregatedMetric(metricGroup.Key, metricGroup.Value, CancellationToken.None).Result;
                result.AddAggregatedMetric(metricGroup.Key, aggregated);
            }
        }

        return result;
    }

    #endregion

    #region Private Methods - Maintenance

    private void CleanupExpiredWindows(object? state)
    {
        if (!IsRunning) return;

        try
        {
            var expiredWindowIds = new List<string>();
            var expirationThreshold = DateTime.UtcNow.Subtract(_configuration.DefaultWindowConfig.ExpirationTime);

            foreach (var window in _activeWindows)
            {
                if (window.Value.CreatedAt < expirationThreshold)
                {
                    expiredWindowIds.Add(window.Key);
                }
            }

            foreach (var windowId in expiredWindowIds)
            {
                if (_activeWindows.TryRemove(windowId, out var window))
                {
                    WindowUpdated?.Invoke(new WindowUpdatedEventArgs(window.TimeRange, WindowUpdateType.Expired, Name));
                    _logger?.LogDebug("清理过期窗口: {WindowId}", windowId);
                }
            }

            if (expiredWindowIds.Count > 0)
            {
                _logger?.LogInformation("清理了 {Count} 个过期窗口", expiredWindowIds.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "清理过期窗口失败");
        }
    }

    private long EstimateMemoryUsage()
    {
        long totalSize = 0;

        foreach (var window in _activeWindows.Values)
        {
            foreach (var metricGroup in window.Metrics)
            {
                totalSize += metricGroup.Value.Count * 1024; // 估算每个指标事件约1KB
            }
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
/// 时间窗口
/// </summary>
internal class TimeWindow
{
    /// <summary>
    /// 窗口ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 时间范围
    /// </summary>
    public TimeRange TimeRange { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 窗口中的指标数据
    /// </summary>
    public ConcurrentDictionary<string, List<JsonOptimizedMetricsEvent>> Metrics { get; set; } = new();
}
