using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Analyzers;

/// <summary>
/// 趋势分析器 - 专门分析指标趋势和变化点
/// </summary>
public class TrendAnalyzer : IMetricsAnalyzer
{
    private readonly ILogger<TrendAnalyzer>? _logger;
    private readonly AnalyzerConfiguration _configuration;
    private readonly ConcurrentDictionary<string, List<TrendDataPoint>> _metricHistory;
    private readonly ConcurrentDictionary<string, TrendData> _cachedTrends;

    private PluginStatus _status = PluginStatus.NotInitialized;
    private long _totalAnalyses = 0;
    private long _totalDataPoints = 0;
    private long _totalAnalysisTicks = 0;
    private long _totalAnomaliesDetected = 0;
    private long _totalInsightsGenerated = 0;
    private long _errorCount = 0;
    private long _cacheHits = 0;
    private long _cacheRequests = 0;

    public TrendAnalyzer(
        AnalyzerConfiguration? configuration = null,
        ILogger<TrendAnalyzer>? logger = null)
    {
        _configuration = configuration ?? new AnalyzerConfiguration();
        _logger = logger;
        _metricHistory = new ConcurrentDictionary<string, List<TrendDataPoint>>();
        _cachedTrends = new ConcurrentDictionary<string, TrendData>();
    }

    #region IMetricsPlugin Implementation

    public string Name => "TrendAnalyzer";
    public string Version => "1.0.0";
    public string Description => "Trend analyzer for detecting patterns and change points";
    public string Author => "PulseRPC";
    public bool IsInitialized => _status >= PluginStatus.Initialized;
    public bool IsRunning => _status == PluginStatus.Running;

    public AnalyzerConfiguration Configuration => _configuration;

    public event Action<PluginStatusChangedEventArgs>? StatusChanged;
    public event Action<PluginErrorEventArgs>? ErrorOccurred;
    public event Action<AnalysisCompletedEventArgs>? AnalysisCompleted;
    public event Action<AnomalyDetectedEventArgs>? AnomalyDetected;
    public event Action<InsightGeneratedEventArgs>? InsightGenerated;

    public Task<bool> ValidateConfigurationAsync(object? configuration)
    {
        if (configuration is not AnalyzerConfiguration config)
            return Task.FromResult(false);

        return Task.FromResult(config.TrendAnalysisWindowSize > 0 &&
                              config.InsightConfidenceThreshold is >= 0 and <= 1);
    }

    public Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("初始化趋势分析器");
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
        _logger?.LogInformation("趋势分析器已启动");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ChangeStatus(PluginStatus.Stopped);
        _logger?.LogInformation("趋势分析器已停止");
        return Task.CompletedTask;
    }

    public Task<PluginHealthStatus> GetHealthStatusAsync()
    {
        var metricCount = _metricHistory.Count;
        var cacheSize = _cachedTrends.Count;

        var isHealthy = _status == PluginStatus.Running &&
                       cacheSize < _configuration.MaxCacheSize;

        var status = isHealthy
            ? PluginHealthStatus.Healthy($"趋势分析器运行正常，监控 {metricCount} 个指标")
            : PluginHealthStatus.Unhealthy("趋势分析器异常", $"状态: {_status}, 缓存: {cacheSize}");

        status.Metrics["monitored_metrics"] = metricCount;
        status.Metrics["cache_size"] = cacheSize;
        status.Metrics["total_analyses"] = _totalAnalyses;
        status.Metrics["cache_hit_ratio"] = _cacheRequests > 0 ? (double)_cacheHits / _cacheRequests : 0;

        return Task.FromResult(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        ChangeStatus(PluginStatus.Disposed);
    }

    #endregion

    #region IMetricsAnalyzer Implementation

    public async Task<AnalysisResult> AnalyzeMetricsAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new AnalysisResult
        {
            AnalyzerName = Name,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var metricsList = metrics.ToList();
            if (metricsList.Count == 0)
                return result;

            // 更新指标历史
            UpdateMetricHistory(metricsList);

            // 计算时间范围
            result.TimeRange = new TimeRange
            {
                StartTime = metricsList.Min(m => m.Timestamp),
                EndTime = metricsList.Max(m => m.Timestamp)
            };

            // 按指标分组分析
            var groupedMetrics = metricsList.GroupBy(m => m.MetricName);

            foreach (var group in groupedMetrics)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var trendData = await AnalyzeTrendForMetric(group.Key, group.ToList(), cancellationToken);
                result.AddTrendData(trendData);

                // 生成洞察
                if (_configuration.EnableInsightGeneration)
                {
                    var insights = await GenerateInsightsForTrend(trendData, cancellationToken);
                    foreach (var insight in insights)
                    {
                        result.AddInsight(insight);
                    }
                }
            }

            result.ProcessedDataPoints = metricsList.Count;
            stopwatch.Stop();
            result.AnalysisDuration = stopwatch.Elapsed;

            // 更新统计信息
            UpdateStatistics(metricsList.Count, stopwatch.Elapsed);

            // 触发事件
            AnalysisCompleted?.Invoke(new AnalysisCompletedEventArgs(result, Name));

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger?.LogError(ex, "趋势分析失败");
            OnError(ex, "趋势分析失败");
            throw;
        }
    }

    public async Task<AnalysisResult> AnalyzeAggregationResultsAsync(IEnumerable<AggregationResult> aggregationResults, CancellationToken cancellationToken = default)
    {
        var allMetrics = new List<JsonOptimizedMetricsEvent>();

        foreach (var aggregationResult in aggregationResults)
        {
            // 从聚合结果重构指标事件
            foreach (var metric in aggregationResult.AggregatedMetrics)
            {
                var metricEvent = new JsonOptimizedMetricsEvent
                {
                    MetricName = metric.Key,
                    Timestamp = aggregationResult.Timestamp,
                    Value = JsonSerializer.SerializeToElement(metric.Value.Average),
                    Unit = metric.Value.Unit,
                    Tags = metric.Value.Tags
                };
                allMetrics.Add(metricEvent);
            }
        }

        return await AnalyzeMetricsAsync(allMetrics, cancellationToken);
    }

    public async Task<AnomalyDetectionResult> DetectAnomaliesAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, AnomalyDetectionConfiguration? detectionConfig = null, CancellationToken cancellationToken = default)
    {
        var config = detectionConfig ?? _configuration.DefaultAnomalyDetection;
        var metricsList = metrics.ToList();

        var result = new AnomalyDetectionResult
        {
            DetectionTimestamp = DateTime.UtcNow,
            MetricName = metricsList.FirstOrDefault()?.MetricName ?? "Unknown",
            DetectionMethod = "TrendBased",
            DetectionParameters = new Dictionary<string, object>
            {
                ["method"] = config.Method.ToString(),
                ["sensitivity"] = config.Sensitivity,
                ["window_size"] = config.WindowSize
            }
        };

        try
        {
            // 基于趋势的异常检测
            var anomalies = await DetectTrendAnomalies(metricsList, config, cancellationToken);
            result.AnomalyPoints.AddRange(anomalies);
            result.AnomalyRatio = metricsList.Count > 0 ? (double)anomalies.Count / metricsList.Count : 0;

            Interlocked.Add(ref _totalAnomaliesDetected, anomalies.Count);

            // 触发事件
            if (anomalies.Count > 0)
            {
                AnomalyDetected?.Invoke(new AnomalyDetectedEventArgs(result, Name));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "异常检测失败");
            throw;
        }
    }

    public async Task<InsightResult> GenerateInsightsAsync(IEnumerable<AnalysisResult> analysisResults, CancellationToken cancellationToken = default)
    {
        var results = analysisResults.ToList();
        if (results.Count == 0)
            return new InsightResult();

        var insight = new InsightResult
        {
            Type = InsightType.PerformanceImprovement,
            Title = "趋势分析洞察",
            Description = "基于多个分析结果的综合洞察",
            Confidence = 0.8,
            TimeRange = new TimeRange
            {
                StartTime = results.Min(r => r.TimeRange.StartTime),
                EndTime = results.Max(r => r.TimeRange.EndTime)
            }
        };

        // 分析整体趋势
        var allTrends = results.SelectMany(r => r.TrendData).ToList();
        var improvingTrends = allTrends.Count(t => t.TrendType == TrendType.Increasing && t.TrendStrength > 0);
        var degradingTrends = allTrends.Count(t => t.TrendType == TrendType.Decreasing && t.TrendStrength < 0);

        if (improvingTrends > degradingTrends)
        {
            insight.Type = InsightType.PerformanceImprovement;
            insight.Description = $"检测到 {improvingTrends} 个指标呈改善趋势";
        }
        else if (degradingTrends > improvingTrends)
        {
            insight.Type = InsightType.PerformanceDegradation;
            insight.Description = $"检测到 {degradingTrends} 个指标呈下降趋势";
        }

        Interlocked.Increment(ref _totalInsightsGenerated);
        InsightGenerated?.Invoke(new InsightGeneratedEventArgs(insight, Name));

        return insight;
    }

    public async Task<TrendData> GetTrendDataAsync(string metricName, TimeRange timeRange, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _cacheRequests);

        var cacheKey = $"{metricName}_{timeRange.StartTime:yyyy-MM-dd-HH-mm}_{timeRange.EndTime:yyyy-MM-dd-HH-mm}";

        if (_cachedTrends.TryGetValue(cacheKey, out var cachedTrend))
        {
            Interlocked.Increment(ref _cacheHits);
            return cachedTrend;
        }

        // 从历史数据中获取指定时间范围的数据
        if (!_metricHistory.TryGetValue(metricName, out var history))
        {
            return new TrendData { MetricName = metricName, TimeRange = timeRange };
        }

        var relevantData = history.Where(p => timeRange.Contains(p.Timestamp)).ToList();
        var trendData = await CalculateTrend(metricName, relevantData, cancellationToken);

        // 缓存结果
        if (_cachedTrends.Count < _configuration.MaxCacheSize)
        {
            _cachedTrends.TryAdd(cacheKey, trendData);
        }

        return trendData;
    }

    public async Task<ComparisonResult> CompareMetricsAsync(IEnumerable<JsonOptimizedMetricsEvent> baselineMetrics, IEnumerable<JsonOptimizedMetricsEvent> currentMetrics, CancellationToken cancellationToken = default)
    {
        var baseline = baselineMetrics.ToList();
        var current = currentMetrics.ToList();

        if (baseline.Count == 0 || current.Count == 0)
            return new ComparisonResult();

        var metricName = baseline.FirstOrDefault()?.MetricName ?? current.FirstOrDefault()?.MetricName ?? "Unknown";

        var result = new ComparisonResult
        {
            MetricName = metricName,
            BaselineStatistics = await CalculateStatistics(baseline, cancellationToken),
            CurrentStatistics = await CalculateStatistics(current, cancellationToken)
        };

        // 计算变化百分比
        if (result.BaselineStatistics.Mean != 0)
        {
            result.ChangePercentage = ((result.CurrentStatistics.Mean - result.BaselineStatistics.Mean) / result.BaselineStatistics.Mean) * 100;
        }

        // 简化的显著性测试
        result.SignificanceTest = new SignificanceTestResult
        {
            TestMethod = "SimpleComparison",
            IsSignificant = Math.Abs(result.ChangePercentage) > 5, // 5%阈值
            PValue = Math.Abs(result.ChangePercentage) / 100
        };

        result.IsImprovement = result.ChangePercentage > 5;
        result.IsRegression = result.ChangePercentage < -5;

        return result;
    }

    public Task<AnalyzerStatistics> GetAnalyzerStatisticsAsync()
    {
        var stats = new AnalyzerStatistics
        {
            TotalAnalyses = _totalAnalyses,
            TotalProcessedDataPoints = _totalDataPoints,
            AverageAnalysisTime = _totalAnalyses > 0
                ? TimeSpan.FromTicks(_totalAnalysisTicks / _totalAnalyses)
                : TimeSpan.Zero,
            TotalAnomaliesDetected = _totalAnomaliesDetected,
            TotalInsightsGenerated = _totalInsightsGenerated,
            LastAnalysisTime = DateTime.UtcNow,
            ErrorCount = _errorCount,
            CacheHitRatio = _cacheRequests > 0 ? (double)_cacheHits / _cacheRequests : 0
        };

        return Task.FromResult(stats);
    }

    public Task ClearAnalysisDataAsync()
    {
        _metricHistory.Clear();
        _cachedTrends.Clear();
        _totalAnalyses = 0;
        _totalDataPoints = 0;
        _totalAnalysisTicks = 0;
        _totalAnomaliesDetected = 0;
        _totalInsightsGenerated = 0;
        _errorCount = 0;
        _cacheHits = 0;
        _cacheRequests = 0;

        _logger?.LogInformation("已清空所有分析数据");
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private void UpdateMetricHistory(List<JsonOptimizedMetricsEvent> metrics)
    {
        foreach (var metric in metrics)
        {
            var dataPoint = new TrendDataPoint
            {
                Timestamp = metric.Timestamp,
                Value = double.TryParse(metric.GetStringValue(), out var value) ? value : 0
            };

            _metricHistory.AddOrUpdate(
                metric.MetricName,
                new List<TrendDataPoint> { dataPoint },
                (key, existing) =>
                {
                    lock (existing)
                    {
                        existing.Add(dataPoint);
                        // 限制历史数据大小
                        if (existing.Count > _configuration.TrendAnalysisWindowSize * 2)
                        {
                            existing.RemoveRange(0, existing.Count - _configuration.TrendAnalysisWindowSize);
                        }
                        return existing;
                    }
                });
        }
    }

    private async Task<TrendData> AnalyzeTrendForMetric(string metricName, List<JsonOptimizedMetricsEvent> events, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var dataPoints = events.Select(e => new TrendDataPoint
            {
                Timestamp = e.Timestamp,
                Value = double.TryParse(e.GetStringValue(), out var value) ? value : 0
            }).OrderBy(p => p.Timestamp).ToList();

            return CalculateTrend(metricName, dataPoints, cancellationToken).Result;
        }, cancellationToken);
    }

    private async Task<TrendData> CalculateTrend(string metricName, List<TrendDataPoint> dataPoints, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var trendData = new TrendData
            {
                MetricName = metricName,
                DataPoints = dataPoints,
                TimeRange = dataPoints.Count > 0 ? new TimeRange
                {
                    StartTime = dataPoints.First().Timestamp,
                    EndTime = dataPoints.Last().Timestamp
                } : new TimeRange()
            };

            if (dataPoints.Count < 2)
                return trendData;

            // 线性回归计算
            var regression = CalculateLinearRegression(dataPoints);
            trendData.Slope = regression.slope;
            trendData.CorrelationCoefficient = regression.correlation;
            trendData.Confidence = Math.Abs(regression.correlation);

            // 确定趋势类型
            trendData.TrendType = DetermineTrendType(regression.slope, regression.correlation);
            trendData.TrendStrength = regression.slope;

            // 检测变化点
            trendData.ChangePoints = DetectChangePoints(dataPoints);

            return trendData;
        }, cancellationToken);
    }

    private (double slope, double correlation) CalculateLinearRegression(List<TrendDataPoint> dataPoints)
    {
        if (dataPoints.Count < 2)
            return (0, 0);

        var n = dataPoints.Count;
        var xSum = 0.0;
        var ySum = 0.0;
        var xySum = 0.0;
        var x2Sum = 0.0;
        var y2Sum = 0.0;

        for (int i = 0; i < n; i++)
        {
            var x = i; // 使用索引作为x值
            var y = dataPoints[i].Value;

            xSum += x;
            ySum += y;
            xySum += x * y;
            x2Sum += x * x;
            y2Sum += y * y;
        }

        var slope = (n * xySum - xSum * ySum) / (n * x2Sum - xSum * xSum);

        var numerator = n * xySum - xSum * ySum;
        var denominator = Math.Sqrt((n * x2Sum - xSum * xSum) * (n * y2Sum - ySum * ySum));
        var correlation = denominator != 0 ? numerator / denominator : 0;

        return (slope, correlation);
    }

    private TrendType DetermineTrendType(double slope, double correlation)
    {
        var absCorrelation = Math.Abs(correlation);

        if (absCorrelation < 0.3) return TrendType.None;
        if (slope > 0.1) return TrendType.Increasing;
        if (slope < -0.1) return TrendType.Decreasing;

        return TrendType.Oscillating;
    }

    private List<ChangePoint> DetectChangePoints(List<TrendDataPoint> dataPoints)
    {
        var changePoints = new List<ChangePoint>();

        if (dataPoints.Count < 10) return changePoints;

        // 简化的变化点检测：检测平均值的显著变化
        var windowSize = Math.Min(10, dataPoints.Count / 3);

        for (int i = windowSize; i < dataPoints.Count - windowSize; i++)
        {
            var beforeWindow = dataPoints.Skip(i - windowSize).Take(windowSize).Select(p => p.Value).ToList();
            var afterWindow = dataPoints.Skip(i).Take(windowSize).Select(p => p.Value).ToList();

            var beforeMean = beforeWindow.Average();
            var afterMean = afterWindow.Average();
            var beforeStd = CalculateStandardDeviation(beforeWindow);

            if (beforeStd > 0)
            {
                var changeRatio = Math.Abs(afterMean - beforeMean) / beforeStd;

                if (changeRatio > 2.0) // 2个标准差阈值
                {
                    changePoints.Add(new ChangePoint
                    {
                        Timestamp = dataPoints[i].Timestamp,
                        Type = ChangePointType.MeanShift,
                        Magnitude = changeRatio,
                        Confidence = Math.Min(0.95, changeRatio / 3.0)
                    });
                }
            }
        }

        return changePoints;
    }

    private double CalculateStandardDeviation(List<double> values)
    {
        if (values.Count < 2) return 0;

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1);
        return Math.Sqrt(variance);
    }

    private async Task<List<AnomalyPoint>> DetectTrendAnomalies(List<JsonOptimizedMetricsEvent> metrics, AnomalyDetectionConfiguration config, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var anomalies = new List<AnomalyPoint>();
            var values = metrics.Select(m => double.TryParse(m.GetStringValue(), out var v) ? v : 0).ToList();

            if (values.Count < config.WindowSize)
                return anomalies;

            var mean = values.Average();
            var std = CalculateStandardDeviation(values);
            var threshold = config.StandardDeviationMultiple * std;

            for (int i = 0; i < metrics.Count; i++)
            {
                var value = values[i];
                var deviation = Math.Abs(value - mean);

                if (deviation > threshold)
                {
                    anomalies.Add(new AnomalyPoint
                    {
                        Timestamp = metrics[i].Timestamp,
                        Value = value,
                        ExpectedValue = mean,
                        AnomalyScore = deviation / std,
                        Type = AnomalyType.Point,
                        Severity = deviation > threshold * 2 ? AnomalySeverity.High : AnomalySeverity.Medium
                    });
                }
            }

            return anomalies;
        }, cancellationToken);
    }

    private async Task<List<InsightResult>> GenerateInsightsForTrend(TrendData trendData, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var insights = new List<InsightResult>();

            if (trendData.Confidence < _configuration.InsightConfidenceThreshold)
                return insights;

            // 趋势洞察
            if (trendData.TrendType == TrendType.Increasing && trendData.TrendStrength > 0.5)
            {
                insights.Add(new InsightResult
                {
                    Type = InsightType.PerformanceImprovement,
                    Title = $"{trendData.MetricName} 呈改善趋势",
                    Description = $"该指标在监控期间呈现稳定上升趋势，斜率为 {trendData.Slope:F3}",
                    Confidence = trendData.Confidence,
                    Importance = InsightImportance.Medium,
                    RelatedMetrics = new List<string> { trendData.MetricName }
                });
            }
            else if (trendData.TrendType == TrendType.Decreasing && trendData.TrendStrength < -0.5)
            {
                insights.Add(new InsightResult
                {
                    Type = InsightType.PerformanceDegradation,
                    Title = $"{trendData.MetricName} 呈下降趋势",
                    Description = $"该指标在监控期间呈现下降趋势，可能需要关注",
                    Confidence = trendData.Confidence,
                    Importance = InsightImportance.High,
                    RelatedMetrics = new List<string> { trendData.MetricName },
                    RecommendedActions = new List<string> { "检查系统配置", "分析资源使用情况" }
                });
            }

            // 变化点洞察
            if (trendData.ChangePoints.Count > 0)
            {
                var significantChanges = trendData.ChangePoints.Where(cp => cp.Confidence > 0.8).ToList();
                if (significantChanges.Count > 0)
                {
                    insights.Add(new InsightResult
                    {
                        Type = InsightType.AnomalousPattern,
                        Title = $"{trendData.MetricName} 检测到显著变化点",
                        Description = $"在监控期间检测到 {significantChanges.Count} 个显著变化点",
                        Confidence = significantChanges.Average(cp => cp.Confidence),
                        Importance = InsightImportance.High,
                        RelatedMetrics = new List<string> { trendData.MetricName }
                    });
                }
            }

            return insights;
        }, cancellationToken);
    }

    private async Task<StatisticalSummary> CalculateStatistics(List<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var values = metrics.Select(m => double.TryParse(m.GetStringValue(), out var v) ? v : 0).Where(v => !double.IsNaN(v)).ToList();

            if (values.Count == 0)
                return new StatisticalSummary();

            values.Sort();

            return new StatisticalSummary
            {
                Count = values.Count,
                Mean = values.Average(),
                Median = values[values.Count / 2],
                StandardDeviation = CalculateStandardDeviation(values),
                Min = values.Min(),
                Max = values.Max(),
                Percentiles = new Dictionary<int, double>
                {
                    [25] = values[(int)(values.Count * 0.25)],
                    [75] = values[(int)(values.Count * 0.75)],
                    [90] = values[(int)(values.Count * 0.90)],
                    [95] = values[(int)(values.Count * 0.95)]
                }
            };
        }, cancellationToken);
    }

    private void UpdateStatistics(int dataPointCount, TimeSpan analysisDuration)
    {
        Interlocked.Increment(ref _totalAnalyses);
        Interlocked.Add(ref _totalDataPoints, dataPointCount);
        Interlocked.Add(ref _totalAnalysisTicks, analysisDuration.Ticks);
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
