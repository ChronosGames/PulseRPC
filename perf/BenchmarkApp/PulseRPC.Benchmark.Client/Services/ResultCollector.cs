using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Benchmark.Client.Services;

/// <summary>
/// 结果收集器
/// 负责收集和管理测试结果数据
/// </summary>
public class ResultCollector
{
    private readonly ILogger<ResultCollector> _logger;
    private readonly ConcurrentBag<TestRequestResult> _rawResults;
    private readonly ConcurrentBag<TestMetric> _metrics;
    private readonly ConcurrentDictionary<string, object> _customData;
    private readonly Lock _lockObject = new();

    private volatile bool _isCollecting;
    private DateTime _collectionStartTime;
    private Timer? _snapshotTimer;

    /// <summary>
    /// 实时统计信息更新事件
    /// </summary>
    public event Action<LiveStatistics>? StatisticsUpdated;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ResultCollector(ILogger<ResultCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rawResults = new ConcurrentBag<TestRequestResult>();
        _metrics = new ConcurrentBag<TestMetric>();
        _customData = new ConcurrentDictionary<string, object>();
    }

    /// <summary>
    /// 开始收集结果
    /// </summary>
    /// <param name="snapshotIntervalMs">快照间隔（毫秒），0表示不生成快照</param>
    public void StartCollection(int snapshotIntervalMs = 1000)
    {
        lock (_lockObject)
        {
            if (_isCollecting)
            {
                StopCollection();
            }

            _isCollecting = true;
            _collectionStartTime = DateTime.UtcNow;

            // 清空之前的数据
            ClearAllData();

            // 启动快照定时器
            if (snapshotIntervalMs > 0)
            {
                _snapshotTimer = new Timer(GenerateSnapshot, null,
                    TimeSpan.FromMilliseconds(snapshotIntervalMs),
                    TimeSpan.FromMilliseconds(snapshotIntervalMs));
            }
        }

        _logger.LogInformation("开始收集测试结果");
    }

    /// <summary>
    /// 停止收集结果
    /// </summary>
    public void StopCollection()
    {
        lock (_lockObject)
        {
            if (!_isCollecting) return;

            _isCollecting = false;
            _snapshotTimer?.Dispose();
            _snapshotTimer = null;
        }

        _logger.LogInformation("停止收集测试结果，共收集 {Count} 个结果", _rawResults.Count);
    }

    /// <summary>
    /// 记录请求结果
    /// </summary>
    /// <param name="result">请求结果</param>
    public void RecordResult(TestRequestResult result)
    {
        if (!_isCollecting) return;

        result.Timestamp = DateTime.UtcNow;
        _rawResults.Add(result);

        // 记录延迟指标
        RecordMetric("latency", result.ResponseTimeMs, result.Timestamp);

        // 记录成功/失败指标
        RecordMetric(result.Success ? "success" : "failure", 1, result.Timestamp);

        // 可选：定期触发统计更新（避免过于频繁）
        if (_rawResults.Count % 100 == 0)
        {
            TriggerStatisticsUpdate();
        }
    }

    /// <summary>
    /// 记录自定义指标
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="value">指标值</param>
    /// <param name="timestamp">时间戳</param>
    /// <param name="tags">可选标签</param>
    public void RecordMetric(string name, double value, DateTime? timestamp = null, Dictionary<string, string>? tags = null)
    {
        if (!_isCollecting) return;

        var metric = new TestMetric
        {
            Name = name,
            Value = value,
            Timestamp = timestamp ?? DateTime.UtcNow,
            Tags = tags ?? new Dictionary<string, string>()
        };

        _metrics.Add(metric);
    }

    /// <summary>
    /// 设置自定义数据
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value">值</param>
    public void SetCustomData(string key, object value)
    {
        _customData.TryAdd(key, value);
    }

    /// <summary>
    /// 获取当前实时统计信息
    /// </summary>
    public LiveStatistics GetLiveStatistics()
    {
        var results = _rawResults.ToArray();
        if (results.Length == 0)
        {
            return new LiveStatistics();
        }

        var successfulResults = results.Where(r => r.Success).ToArray();
        var latencies = successfulResults.Select(r => r.ResponseTimeMs).ToArray();

        var elapsedTime = DateTime.UtcNow - _collectionStartTime;
        var qps = results.Length / Math.Max(elapsedTime.TotalSeconds, 1);

        return new LiveStatistics
        {
            TotalRequests = results.Length,
            SuccessfulRequests = successfulResults.Length,
            FailedRequests = results.Length - successfulResults.Length,
            SuccessRate = (double)successfulResults.Length / results.Length,
            CurrentQPS = qps,
            AverageLatencyMs = latencies.Length > 0 ? latencies.Average() : 0,
            MinLatencyMs = latencies.Length > 0 ? latencies.Min() : 0,
            MaxLatencyMs = latencies.Length > 0 ? latencies.Max() : 0,
            P50LatencyMs = CalculatePercentile(latencies, 50),
            P95LatencyMs = CalculatePercentile(latencies, 95),
            P99LatencyMs = CalculatePercentile(latencies, 99),
            ElapsedTime = elapsedTime
        };
    }

    /// <summary>
    /// 生成完整的测试结果报告
    /// </summary>
    public TestResultReport GenerateReport()
    {
        var results = _rawResults.ToArray();
        var metrics = _metrics.ToArray();
        var liveStats = GetLiveStatistics();

        var report = new TestResultReport
        {
            GeneratedAt = DateTime.UtcNow,
            CollectionStartTime = _collectionStartTime,
            CollectionEndTime = DateTime.UtcNow,
            TotalDuration = DateTime.UtcNow - _collectionStartTime,

            // 基础统计
            TotalRequests = results.Length,
            SuccessfulRequests = liveStats.SuccessfulRequests,
            FailedRequests = liveStats.FailedRequests,
            SuccessRate = liveStats.SuccessRate,

            // 性能指标
            AverageQPS = liveStats.CurrentQPS,
            AverageLatencyMs = liveStats.AverageLatencyMs,
            MinLatencyMs = liveStats.MinLatencyMs,
            MaxLatencyMs = liveStats.MaxLatencyMs,

            // 百分位数
            P50LatencyMs = liveStats.P50LatencyMs,
            P95LatencyMs = liveStats.P95LatencyMs,
            P99LatencyMs = liveStats.P99LatencyMs,
            P999LatencyMs = CalculatePercentile(results.Where(r => r.Success).Select(r => r.ResponseTimeMs).ToArray(), 99.9),

            // 详细数据
            RawResults = results.ToList(),
            Metrics = metrics.ToList(),
            CustomData = new Dictionary<string, object>(_customData),

            // 错误分析
            ErrorSummary = GenerateErrorSummary(results),

            // 时间分析
            TimelineAnalysis = GenerateTimelineAnalysis(results)
        };

        _logger.LogInformation("生成测试结果报告完成，总请求数: {TotalRequests}", report.TotalRequests);
        return report;
    }

    /// <summary>
    /// 导出结果到文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="format">格式（json|csv|xml）</param>
    public async Task ExportResultsAsync(string filePath, string format = "json")
    {
        var report = GenerateReport();

        switch (format.ToLower())
        {
            case "json":
                await ExportAsJsonAsync(report, filePath);
                break;
            case "csv":
                await ExportAsCsvAsync(report, filePath);
                break;
            case "xml":
                await ExportAsXmlAsync(report, filePath);
                break;
            default:
                throw new ArgumentException($"不支持的导出格式: {format}");
        }

        _logger.LogInformation("结果已导出到文件: {FilePath}", filePath);
    }

    /// <summary>
    /// 清空所有数据
    /// </summary>
    public void ClearAllData()
    {
        while (_rawResults.TryTake(out _)) { }
        while (_metrics.TryTake(out _)) { }
        _customData.Clear();
    }

    /// <summary>
    /// 生成快照
    /// </summary>
    private void GenerateSnapshot(object? state)
    {
        try
        {
            TriggerStatisticsUpdate();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "生成统计快照时发生错误");
        }
    }

    /// <summary>
    /// 触发统计信息更新
    /// </summary>
    private void TriggerStatisticsUpdate()
    {
        var stats = GetLiveStatistics();
        StatisticsUpdated?.Invoke(stats);
    }

    /// <summary>
    /// 计算百分位数
    /// </summary>
    private double CalculatePercentile(double[] values, double percentile)
    {
        if (values.Length == 0) return 0;

        Array.Sort(values);
        var index = (percentile / 100.0) * (values.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
            return values[lower];

        var weight = index - lower;
        return values[lower] * (1 - weight) + values[upper] * weight;
    }

    /// <summary>
    /// 生成错误摘要
    /// </summary>
    private Dictionary<string, int> GenerateErrorSummary(TestRequestResult[] results)
    {
        var errorSummary = new Dictionary<string, int>();

        foreach (var result in results.Where(r => !r.Success))
        {
            var errorKey = string.IsNullOrEmpty(result.ErrorMessage) ? "Unknown Error" : result.ErrorMessage;
            errorSummary.TryGetValue(errorKey, out var count);
            errorSummary[errorKey] = count + 1;
        }

        return errorSummary;
    }

    /// <summary>
    /// 生成时间线分析
    /// </summary>
    private TimelineAnalysis GenerateTimelineAnalysis(TestRequestResult[] results)
    {
        if (results.Length == 0)
        {
            return new TimelineAnalysis();
        }

        var timeGroups = results
            .GroupBy(r => new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, r.Timestamp.Hour, r.Timestamp.Minute, 0))
            .OrderBy(g => g.Key)
            .ToArray();

        return new TimelineAnalysis
        {
            TimeWindows = timeGroups.Select(g => new TimeWindow
            {
                StartTime = g.Key,
                EndTime = g.Key.AddMinutes(1),
                RequestCount = g.Count(),
                SuccessCount = g.Count(r => r.Success),
                AverageLatencyMs = g.Where(r => r.Success).Select(r => r.ResponseTimeMs).DefaultIfEmpty(0).Average()
            }).ToList()
        };
    }

    /// <summary>
    /// 导出为JSON格式
    /// </summary>
    private async Task ExportAsJsonAsync(TestResultReport report, string filePath)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// 导出为CSV格式
    /// </summary>
    private async Task ExportAsCsvAsync(TestResultReport report, string filePath)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,RequestId,Success,ResponseTimeMs,ErrorMessage");

        foreach (var result in report.RawResults)
        {
            csv.AppendLine($"{result.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{result.RequestId},{result.Success},{result.ResponseTimeMs:F2},{result.ErrorMessage ?? ""}");
        }

        await File.WriteAllTextAsync(filePath, csv.ToString());
    }

    /// <summary>
    /// 导出为XML格式
    /// </summary>
    private async Task ExportAsXmlAsync(TestResultReport report, string filePath)
    {
        // 简化的XML导出
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<TestReport>
    <Summary>
        <TotalRequests>{report.TotalRequests}</TotalRequests>
        <SuccessfulRequests>{report.SuccessfulRequests}</SuccessfulRequests>
        <FailedRequests>{report.FailedRequests}</FailedRequests>
        <SuccessRate>{report.SuccessRate:F4}</SuccessRate>
        <AverageQPS>{report.AverageQPS:F2}</AverageQPS>
        <AverageLatency>{report.AverageLatencyMs:F2}</AverageLatency>
    </Summary>
</TestReport>";

        await File.WriteAllTextAsync(filePath, xml);
    }
}

/// <summary>
/// 测试请求结果
/// </summary>
public class TestRequestResult
{
    public string RequestId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    public double ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

/// <summary>
/// 测试指标
/// </summary>
public class TestMetric
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}

/// <summary>
/// 实时统计信息
/// </summary>
public class LiveStatistics
{
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double SuccessRate { get; set; }
    public double CurrentQPS { get; set; }
    public double AverageLatencyMs { get; set; }
    public double MinLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; init; }
    public TimeSpan ElapsedTime { get; set; }
}

/// <summary>
/// 测试结果报告
/// </summary>
public class TestResultReport
{
    public DateTime GeneratedAt { get; set; }
    public DateTime CollectionStartTime { get; set; }
    public DateTime CollectionEndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double SuccessRate { get; set; }

    public double AverageQPS { get; set; }
    public double AverageLatencyMs { get; set; }
    public double MinLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }

    public double P50LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double P999LatencyMs { get; set; }

    public List<TestRequestResult> RawResults { get; set; } = new();
    public List<TestMetric> Metrics { get; set; } = new();
    public Dictionary<string, object> CustomData { get; set; } = new();
    public Dictionary<string, int> ErrorSummary { get; set; } = new();
    public TimelineAnalysis TimelineAnalysis { get; set; } = new();
}

/// <summary>
/// 时间线分析
/// </summary>
public class TimelineAnalysis
{
    public List<TimeWindow> TimeWindows { get; set; } = new();
}

/// <summary>
/// 时间窗口
/// </summary>
public class TimeWindow
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int RequestCount { get; set; }
    public int SuccessCount { get; set; }
    public double AverageLatencyMs { get; set; }
}
