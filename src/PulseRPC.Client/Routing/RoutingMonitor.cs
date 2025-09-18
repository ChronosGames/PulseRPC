using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace PulseRPC.Client.Routing;

/// <summary>
/// 路由监控器
/// </summary>
public sealed class RoutingMonitor : IDisposable
{
    private readonly ILogger<RoutingMonitor> _logger;
    private readonly ConcurrentQueue<RoutingTrace> _traces = new();
    private readonly RoutingMetrics _metrics = new();
    private readonly Timer _metricsTimer;
    private readonly object _metricsLock = new();
    private volatile bool _disposed;

    /// <summary>
    /// 最大跟踪记录数
    /// </summary>
    public int MaxTraces { get; set; } = 1000;

    /// <summary>
    /// 是否启用详细跟踪
    /// </summary>
    public bool EnableVerboseTracing { get; set; } = false;

    /// <summary>
    /// 是否启用性能分析
    /// </summary>
    public bool EnableProfiling { get; set; } = true;

    /// <summary>
    /// 构造函数
    /// </summary>
    public RoutingMonitor(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<RoutingMonitor>() ?? NullLogger<RoutingMonitor>.Instance;

        // 每10秒更新一次指标
        _metricsTimer = new Timer(UpdateMetrics, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// 记录路由跟踪
    /// </summary>
    public void TraceRouting(RoutingEventArgs e)
    {
        if (_disposed)
            return;

        var trace = new RoutingTrace
        {
            TraceId = Guid.NewGuid().ToString("N"),
            Key = e.Key,
            Timestamp = e.Timestamp,
            Context = e.Context,
            Result = e.Result,
            Details = EnableVerboseTracing ? CreateVerboseDetails(e) : null
        };

        _traces.Enqueue(trace);

        // 限制跟踪记录数量
        while (_traces.Count > MaxTraces && _traces.TryDequeue(out _))
        {
            // 移除旧的跟踪记录
        }

        // 更新指标
        UpdateRoutingMetrics(e.Result);

        if (EnableVerboseTracing)
        {
            _logger.LogDebug("路由跟踪: {TraceId} - {Key} -> {Result}",
                trace.TraceId, e.Key, e.Result.IsSuccess ? "成功" : "失败");
        }
    }

    /// <summary>
    /// 获取路由跟踪记录
    /// </summary>
    public IReadOnlyList<RoutingTrace> GetTraces(int count = 100)
    {
        if (_disposed)
            return Array.Empty<RoutingTrace>();

        return _traces.TakeLast(count).ToList();
    }

    /// <summary>
    /// 获取路由指标
    /// </summary>
    public RoutingMetrics GetMetrics()
    {
        if (_disposed)
            return new RoutingMetrics();

        lock (_metricsLock)
        {
            return new RoutingMetrics
            {
                TotalRequests = _metrics.TotalRequests,
                SuccessfulRequests = _metrics.SuccessfulRequests,
                FailedRequests = _metrics.FailedRequests,
                AverageLatency = _metrics.AverageLatency,
                P50Latency = _metrics.P50Latency,
                P95Latency = _metrics.P95Latency,
                P99Latency = _metrics.P99Latency,
                RequestsPerSecond = _metrics.RequestsPerSecond,
                ErrorRate = _metrics.ErrorRate,
                RuleMatchCounts = new Dictionary<string, long>(_metrics.RuleMatchCounts),
                ConnectionUsageCounts = new Dictionary<string, long>(_metrics.ConnectionUsageCounts),
                LatencyDistribution = new Dictionary<string, long>(_metrics.LatencyDistribution),
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 生成路由报告
    /// </summary>
    public RoutingReport GenerateReport(TimeSpan? timeWindow = null)
    {
        if (_disposed)
            return new RoutingReport();

        var window = timeWindow ?? TimeSpan.FromHours(1);
        var cutoff = DateTime.UtcNow - window;

        var relevantTraces = _traces.Where(t => t.Timestamp >= cutoff).ToList();

        var report = new RoutingReport
        {
            GeneratedAt = DateTime.UtcNow,
            TimeWindow = window,
            TotalRoutes = relevantTraces.Count,
            SuccessfulRoutes = relevantTraces.Count(t => t.Result.IsSuccess),
            FailedRoutes = relevantTraces.Count(t => !t.Result.IsSuccess),
            AverageLatency = relevantTraces.Any() ?
                TimeSpan.FromMilliseconds(relevantTraces.Average(t => t.Result.RoutingTime.TotalMilliseconds)) :
                TimeSpan.Zero,
            MaxLatency = relevantTraces.Any() ?
                relevantTraces.Max(t => t.Result.RoutingTime) :
                TimeSpan.Zero
        };

        // 规则匹配统计
        report.RuleMatchStatistics = relevantTraces
            .Where(t => t.Result.MatchedRule != null)
            .GroupBy(t => t.Result.MatchedRule!.Id)
            .ToDictionary(g => g.Key, g => g.Count());

        // 连接使用统计
        report.ConnectionUsageStatistics = relevantTraces
            .Where(t => t.Result.SelectedConnection != null)
            .GroupBy(t => t.Result.SelectedConnection!.Id)
            .ToDictionary(g => g.Key, g => g.Count());

        // 错误统计
        report.ErrorStatistics = relevantTraces
            .Where(t => !t.Result.IsSuccess)
            .GroupBy(t => t.Result.ErrorMessage ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        // 延迟分布
        report.LatencyDistribution = CreateLatencyDistribution(relevantTraces);

        // 时间序列数据
        report.TimeSeriesData = CreateTimeSeriesData(relevantTraces, window);

        return report;
    }

    /// <summary>
    /// 清空跟踪记录
    /// </summary>
    public void ClearTraces()
    {
        if (_disposed)
            return;

        while (_traces.TryDequeue(out _))
        {
            // 清空队列
        }

        _logger.LogInformation("已清空路由跟踪记录");
    }

    /// <summary>
    /// 重置指标
    /// </summary>
    public void ResetMetrics()
    {
        if (_disposed)
            return;

        lock (_metricsLock)
        {
            _metrics.TotalRequests = 0;
            _metrics.SuccessfulRequests = 0;
            _metrics.FailedRequests = 0;
            _metrics.AverageLatency = TimeSpan.Zero;
            _metrics.P50Latency = TimeSpan.Zero;
            _metrics.P95Latency = TimeSpan.Zero;
            _metrics.P99Latency = TimeSpan.Zero;
            _metrics.RequestsPerSecond = 0;
            _metrics.ErrorRate = 0;
            _metrics.RuleMatchCounts.Clear();
            _metrics.ConnectionUsageCounts.Clear();
            _metrics.LatencyDistribution.Clear();
        }

        _logger.LogInformation("已重置路由指标");
    }

    /// <summary>
    /// 创建详细信息
    /// </summary>
    private Dictionary<string, object> CreateVerboseDetails(RoutingEventArgs e)
    {
        var details = new Dictionary<string, object>();

        if (e.Context != null)
        {
            details["Context"] = new
            {
                e.Context.Key,
                e.Context.ServiceName,
                e.Context.MethodName,
                e.Context.ClientId,
                e.Context.SessionId,
                e.Context.UserId,
                e.Context.UserType,
                e.Context.Region,
                e.Context.Zone,
                e.Context.Version,
                Tags = e.Context.Tags.ToDictionary(kv => kv.Key, kv => kv.Value),
                Metadata = e.Context.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "null")
            };
        }

        details["Result"] = new
        {
            e.Result.IsSuccess,
            e.Result.CandidateCount,
            e.Result.RoutingTime,
            e.Result.ErrorMessage,
            e.Result.Reason,
            MatchedRuleId = e.Result.MatchedRule?.Id,
            MatchedRuleName = e.Result.MatchedRule?.Name,
            SelectedConnectionId = e.Result.SelectedConnection?.Id
        };

        return details;
    }

    /// <summary>
    /// 更新路由指标
    /// </summary>
    private void UpdateRoutingMetrics(RoutingResult result)
    {
        lock (_metricsLock)
        {
            _metrics.TotalRequests++;

            if (result.IsSuccess)
            {
                _metrics.SuccessfulRequests++;

                if (result.MatchedRule != null)
                {
                    _metrics.RuleMatchCounts.TryGetValue(result.MatchedRule.Id, out var ruleCount);
                    _metrics.RuleMatchCounts[result.MatchedRule.Id] = ruleCount + 1;
                }

                if (result.SelectedConnection != null)
                {
                    _metrics.ConnectionUsageCounts.TryGetValue(result.SelectedConnection.Id, out var connCount);
                    _metrics.ConnectionUsageCounts[result.SelectedConnection.Id] = connCount + 1;
                }
            }
            else
            {
                _metrics.FailedRequests++;
            }

            // 更新延迟分布
            var latencyMs = (int)result.RoutingTime.TotalMilliseconds;
            var bucket = GetLatencyBucket(latencyMs);
            _metrics.LatencyDistribution.TryGetValue(bucket, out var count);
            _metrics.LatencyDistribution[bucket] = count + 1;

            // 计算错误率
            _metrics.ErrorRate = _metrics.TotalRequests > 0
                ? (double)_metrics.FailedRequests / _metrics.TotalRequests * 100
                : 0;
        }
    }

    /// <summary>
    /// 获取延迟桶
    /// </summary>
    private string GetLatencyBucket(int latencyMs)
    {
        return latencyMs switch
        {
            < 1 => "<1ms",
            < 5 => "1-5ms",
            < 10 => "5-10ms",
            < 50 => "10-50ms",
            < 100 => "50-100ms",
            < 500 => "100-500ms",
            < 1000 => "500ms-1s",
            _ => ">1s"
        };
    }

    /// <summary>
    /// 创建延迟分布
    /// </summary>
    private Dictionary<string, int> CreateLatencyDistribution(IReadOnlyList<RoutingTrace> traces)
    {
        var distribution = new Dictionary<string, int>();

        foreach (var trace in traces)
        {
            var bucket = GetLatencyBucket((int)trace.Result.RoutingTime.TotalMilliseconds);
            distribution.TryGetValue(bucket, out var count);
            distribution[bucket] = count + 1;
        }

        return distribution;
    }

    /// <summary>
    /// 创建时间序列数据
    /// </summary>
    private List<TimeSeriesPoint> CreateTimeSeriesData(IReadOnlyList<RoutingTrace> traces, TimeSpan window)
    {
        var points = new List<TimeSeriesPoint>();
        var interval = TimeSpan.FromMinutes(1); // 1分钟间隔
        var now = DateTime.UtcNow;
        var start = now - window;

        for (var time = start; time <= now; time = time.Add(interval))
        {
            var endTime = time.Add(interval);
            var intervalTraces = traces.Where(t => t.Timestamp >= time && t.Timestamp < endTime).ToList();

            points.Add(new TimeSeriesPoint
            {
                Timestamp = time,
                TotalRequests = intervalTraces.Count,
                SuccessfulRequests = intervalTraces.Count(t => t.Result.IsSuccess),
                FailedRequests = intervalTraces.Count(t => !t.Result.IsSuccess),
                AverageLatency = intervalTraces.Any()
                    ? TimeSpan.FromMilliseconds(intervalTraces.Average(t => t.Result.RoutingTime.TotalMilliseconds))
                    : TimeSpan.Zero
            });
        }

        return points;
    }

    /// <summary>
    /// 更新指标（定期调用）
    /// </summary>
    private void UpdateMetrics(object? state)
    {
        if (_disposed)
            return;

        try
        {
            lock (_metricsLock)
            {
                // 计算每秒请求数（基于最近一分钟的数据）
                var oneMinuteAgo = DateTime.UtcNow - TimeSpan.FromMinutes(1);
                var recentTraces = _traces.Where(t => t.Timestamp >= oneMinuteAgo).ToList();
                _metrics.RequestsPerSecond = recentTraces.Count / 60.0;

                // 计算延迟百分位数
                if (recentTraces.Any())
                {
                    var latencies = recentTraces.Select(t => t.Result.RoutingTime.TotalMilliseconds).OrderBy(x => x).ToArray();
                    _metrics.AverageLatency = TimeSpan.FromMilliseconds(latencies.Average());
                    _metrics.P50Latency = TimeSpan.FromMilliseconds(latencies[(int)(latencies.Length * 0.5)]);
                    _metrics.P95Latency = TimeSpan.FromMilliseconds(latencies[(int)(latencies.Length * 0.95)]);
                    _metrics.P99Latency = TimeSpan.FromMilliseconds(latencies[(int)(latencies.Length * 0.99)]);
                }

                _metrics.LastUpdated = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新路由指标时发生错误");
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _metricsTimer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放路由监控器资源时发生错误");
        }

        _logger.LogInformation("路由监控器已释放");
    }
}

/// <summary>
/// 路由跟踪记录
/// </summary>
public sealed class RoutingTrace
{
    /// <summary>
    /// 跟踪ID
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// 路由键
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 路由上下文
    /// </summary>
    public RoutingContext? Context { get; set; }

    /// <summary>
    /// 路由结果
    /// </summary>
    public RoutingResult Result { get; set; } = null!;

    /// <summary>
    /// 详细信息
    /// </summary>
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>
/// 路由指标
/// </summary>
public sealed class RoutingMetrics
{
    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// 平均延迟
    /// </summary>
    public TimeSpan AverageLatency { get; set; }

    /// <summary>
    /// P50延迟
    /// </summary>
    public TimeSpan P50Latency { get; set; }

    /// <summary>
    /// P95延迟
    /// </summary>
    public TimeSpan P95Latency { get; set; }

    /// <summary>
    /// P99延迟
    /// </summary>
    public TimeSpan P99Latency { get; set; }

    /// <summary>
    /// 每秒请求数
    /// </summary>
    public double RequestsPerSecond { get; set; }

    /// <summary>
    /// 错误率（百分比）
    /// </summary>
    public double ErrorRate { get; set; }

    /// <summary>
    /// 规则匹配计数
    /// </summary>
    public Dictionary<string, long> RuleMatchCounts { get; set; } = new();

    /// <summary>
    /// 连接使用计数
    /// </summary>
    public Dictionary<string, long> ConnectionUsageCounts { get; set; } = new();

    /// <summary>
    /// 延迟分布
    /// </summary>
    public Dictionary<string, long> LatencyDistribution { get; set; } = new();

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 路由报告
/// </summary>
public sealed class RoutingReport
{
    /// <summary>
    /// 生成时间
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// 时间窗口
    /// </summary>
    public TimeSpan TimeWindow { get; set; }

    /// <summary>
    /// 总路由数
    /// </summary>
    public int TotalRoutes { get; set; }

    /// <summary>
    /// 成功路由数
    /// </summary>
    public int SuccessfulRoutes { get; set; }

    /// <summary>
    /// 失败路由数
    /// </summary>
    public int FailedRoutes { get; set; }

    /// <summary>
    /// 平均延迟
    /// </summary>
    public TimeSpan AverageLatency { get; set; }

    /// <summary>
    /// 最大延迟
    /// </summary>
    public TimeSpan MaxLatency { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalRoutes > 0 ? (double)SuccessfulRoutes / TotalRoutes * 100 : 100;

    /// <summary>
    /// 规则匹配统计
    /// </summary>
    public Dictionary<string, int> RuleMatchStatistics { get; set; } = new();

    /// <summary>
    /// 连接使用统计
    /// </summary>
    public Dictionary<string, int> ConnectionUsageStatistics { get; set; } = new();

    /// <summary>
    /// 错误统计
    /// </summary>
    public Dictionary<string, int> ErrorStatistics { get; set; } = new();

    /// <summary>
    /// 延迟分布
    /// </summary>
    public Dictionary<string, int> LatencyDistribution { get; set; } = new();

    /// <summary>
    /// 时间序列数据
    /// </summary>
    public List<TimeSeriesPoint> TimeSeriesData { get; set; } = new();
}

/// <summary>
/// 时间序列点
/// </summary>
public sealed class TimeSeriesPoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public int SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public int FailedRequests { get; set; }

    /// <summary>
    /// 平均延迟
    /// </summary>
    public TimeSpan AverageLatency { get; set; }
}
