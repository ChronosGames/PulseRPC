using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Stability;

/// <summary>
/// 稳定性测试场景
/// 长时间运行测试，监控内存泄漏和连接稳定性
/// </summary>
public class StabilityScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly List<MemorySample> _memorySamples = new();
    private readonly List<double> _latencySamples = new();
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private int _connectionFailures;
    private int _reconnections;

    public override string ScenarioName => "Stability Test";
    public override string Description => "长时间运行测试，监控内存泄漏和连接稳定性";
    public override string Category => ScenarioCategories.Stability;

    public override async Task<BenchmarkResult> ExecuteScenarioAsync(
        BenchmarkConfiguration config,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            ScenarioName = ScenarioName,
            StartTime = DateTime.UtcNow,
            Configuration = config
        };

        try
        {
            Logger.LogInformation("开始稳定性测试，持续时间: {Duration}秒", config.DurationSeconds);

            _memorySamples.Clear();
            _latencySamples.Clear();
            _totalRequests = 0;
            _successfulRequests = 0;
            _failedRequests = 0;
            _connectionFailures = 0;
            _reconnections = 0;

            var testDuration = TimeSpan.FromSeconds(config.DurationSeconds);
            var startTime = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            // 内存采样间隔（每30秒）
            var memorySampleInterval = TimeSpan.FromSeconds(30);
            var lastMemorySample = DateTime.UtcNow;

            // 主测试循环
            while (stopwatch.Elapsed < testDuration && !cancellationToken.IsCancellationRequested)
            {
                // 执行一个请求
                var latency = await ExecuteSingleRequest(config, cancellationToken);
                
                Interlocked.Increment(ref _totalRequests);
                
                if (latency >= 0)
                {
                    lock (_latencySamples)
                    {
                        _latencySamples.Add(latency);
                    }
                    Interlocked.Increment(ref _successfulRequests);
                }
                else
                {
                    Interlocked.Increment(ref _failedRequests);
                }

                // 内存采样
                if (DateTime.UtcNow - lastMemorySample >= memorySampleInterval)
                {
                    CollectMemorySample();
                    lastMemorySample = DateTime.UtcNow;
                }

                // 进度报告
                if (_totalRequests % 1000 == 0)
                {
                    Logger.LogInformation("稳定性测试进度: {Elapsed:F1}/{Total:F1}秒, 请求: {Total}, 成功: {Success}, 失败: {Failed}",
                        stopwatch.Elapsed.TotalSeconds, testDuration.TotalSeconds,
                        _totalRequests, _successfulRequests, _failedRequests);
                }

                // 速率限制（如果配置了）
                if (config.TestIntervalMs > 0)
                {
                    await Task.Delay(config.TestIntervalMs, cancellationToken);
                }
            }

            stopwatch.Stop();

            // 最后一次内存采样
            CollectMemorySample();

            // 计算指标
            PopulateMetrics(result);

            // 分析内存泄漏
            var memoryLeakDetected = AnalyzeMemoryLeak();
            result.CustomMetrics["MemoryLeakDetected"] = memoryLeakDetected;
            result.CustomMetrics["MemorySamples"] = _memorySamples.Count;
            result.CustomMetrics["ConnectionFailures"] = _connectionFailures;
            result.CustomMetrics["Reconnections"] = _reconnections;

            result.IsSuccessful = !memoryLeakDetected && _failedRequests < (_totalRequests * 0.01); // 允许 1% 的失败率
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("稳定性测试完成。总请求: {Total}, 成功: {Success}, 失败: {Failed}, 内存泄漏: {Leak}",
                _totalRequests, _successfulRequests, _failedRequests, memoryLeakDetected);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "稳定性测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task<double> ExecuteSingleRequest(BenchmarkConfiguration config, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        try
        {
            var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;
            var payload = GenerateTestString(messageSize);
            
            var request = new EchoRequest
            {
                Message = payload,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var stopwatch = Stopwatch.StartNew();
            var response = await BenchmarkService.EchoAsync(request, cancellationToken);
            stopwatch.Stop();

            if (response != null && response.Success)
            {
                return stopwatch.Elapsed.TotalMilliseconds;
            }

            return -1;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "请求失败");
            Interlocked.Increment(ref _connectionFailures);
            return -1;
        }
    }

    private void CollectMemorySample()
    {
        var sample = new MemorySample
        {
            Timestamp = DateTime.UtcNow,
            WorkingSetBytes = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };

        lock (_memorySamples)
        {
            _memorySamples.Add(sample);
        }

        Logger.LogDebug("内存采样: {WorkingSet:N0} bytes, GC Gen0/1/2: {Gen0}/{Gen1}/{Gen2}",
            sample.WorkingSetBytes, sample.Gen0Collections, sample.Gen1Collections, sample.Gen2Collections);
    }

    private bool AnalyzeMemoryLeak()
    {
        if (_memorySamples.Count < 3)
            return false;

        // 简单的线性回归分析内存趋势
        var samples = _memorySamples.ToArray();
        var n = samples.Length;
        
        // 计算斜率
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        
        for (int i = 0; i < n; i++)
        {
            double x = i;
            double y = samples[i].WorkingSetBytes;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        
        // 如果斜率大于 1MB/sample，认为存在内存泄漏
        var memoryGrowthPerSample = slope;
        var memoryLeakThreshold = 1024 * 1024; // 1MB per sample

        Logger.LogInformation("内存增长速率: {Rate:N0} bytes/sample", memoryGrowthPerSample);

        return memoryGrowthPerSample > memoryLeakThreshold;
    }

    private void PopulateMetrics(BenchmarkResult result)
    {
        // 延迟指标
        if (_latencySamples.Count > 0)
        {
            var sortedLatencies = _latencySamples.OrderBy(x => x).ToArray();
            result.Latency.SampleCount = sortedLatencies.Length;
            result.Latency.AverageMs = sortedLatencies.Average();
            result.Latency.MinMs = sortedLatencies[0];
            result.Latency.MaxMs = sortedLatencies[^1];
            result.Latency.MedianMs = GetPercentile(sortedLatencies, 50);
            result.Latency.P50Ms = result.Latency.MedianMs;
            result.Latency.P90Ms = GetPercentile(sortedLatencies, 90);
            result.Latency.P95Ms = GetPercentile(sortedLatencies, 95);
            result.Latency.P99Ms = GetPercentile(sortedLatencies, 99);
            result.Latency.P999Ms = GetPercentile(sortedLatencies, 99.9);
            result.Latency.StandardDeviationMs = CalculateStandardDeviation(sortedLatencies);
        }

        // 吞吐量指标
        var duration = result.Duration.TotalSeconds;
        result.Throughput.TotalOperations = _totalRequests;
        result.Throughput.SuccessfulOperations = _successfulRequests;
        result.Throughput.FailedOperations = _failedRequests;
        
        if (duration > 0)
        {
            result.Throughput.OperationsPerSecond = _totalRequests / duration;
        }

        // 资源指标
        if (_memorySamples.Count > 0)
        {
            result.Resources.AverageMemoryUsageBytes = (long)_memorySamples.Average(s => s.WorkingSetBytes);
            result.Resources.PeakMemoryUsageBytes = _memorySamples.Max(s => s.WorkingSetBytes);
        }
    }

    private static double GetPercentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;

        var index = (percentile / 100.0) * (sortedValues.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
            return sortedValues[lower];

        var weight = index - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }

    private static double CalculateStandardDeviation(double[] values)
    {
        if (values.Length == 0) return 0;

        var mean = values.Average();
        var sumOfSquaredDifferences = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquaredDifferences / values.Length);
    }
}

/// <summary>
/// 内存采样数据
/// </summary>
public class MemorySample
{
    public DateTime Timestamp { get; set; }
    public long WorkingSetBytes { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
}
