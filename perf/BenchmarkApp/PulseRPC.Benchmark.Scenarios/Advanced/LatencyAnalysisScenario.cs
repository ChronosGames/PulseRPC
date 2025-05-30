using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Advanced;

/// <summary>
/// 高级延迟分析测试场景
/// 提供详细的延迟分析，包括延迟分布、异常值检测和趋势分析
/// </summary>
public class LatencyAnalysisScenario : BenchmarkClientBase
{
    private readonly List<LatencySample> _latencySamples = new();
    private readonly object _lock = new object();
    private readonly Stopwatch _globalTimer = Stopwatch.StartNew();

    public LatencyAnalysisScenario(ILoggerFactory loggerFactory) : base(loggerFactory)
    {
    }

    public override string ScenarioName => "Advanced Latency Analysis";
    public override string Description => "高级延迟分析，包括延迟分布、异常值检测和趋势分析";
    public override string Category => "Advanced";

    public override async Task<BenchmarkResult> ExecuteScenarioAsync(BenchmarkConfiguration config, CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            ScenarioName = ScenarioName,
            StartTime = DateTime.UtcNow,
            Configuration = config
        };

        try
        {
            Logger.LogInformation("开始高级延迟分析测试，目标样本数: {Iterations}, 分析消息大小: {MessageSize} bytes",
                config.Iterations, config.MessageSizeBytes);

            _latencySamples.Clear();
            _globalTimer.Restart();

            var iterations = config.Iterations;
            var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

            // 预热阶段
            await WarmupPhase(config, cancellationToken);

            // 主测试阶段 - 收集延迟样本
            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("延迟分析测试被取消，当前完成 {Current}/{Total} 次", i, iterations);
                    break;
                }

                var sample = await CollectLatencySample(i + 1, messageSize, cancellationToken);

                lock (_lock)
                {
                    _latencySamples.Add(sample);
                }

                // 可选的测试间隔
                if (config.TestIntervalMs > 0)
                {
                    await Task.Delay(config.TestIntervalMs, cancellationToken);
                }

                // 每500次记录一次进度和中间分析
                if ((i + 1) % 500 == 0)
                {
                    LogIntermediateAnalysis(i + 1);
                }
            }

            // 执行详细的延迟分析
            PerformAdvancedLatencyAnalysis(result);

            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("高级延迟分析完成，样本数: {SampleCount}, 平均延迟: {AvgLatency:F2}ms, 异常值: {Outliers}",
                _latencySamples.Count, result.Latency.AverageMs, result.GetCustomMetric<int>("OutlierCount", 0));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "高级延迟分析测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task WarmupPhase(BenchmarkConfiguration config, CancellationToken cancellationToken)
    {
        var warmupIterations = config.WarmupIterations;
        Logger.LogInformation("开始预热阶段，预热次数: {WarmupIterations}", warmupIterations);

        for (int i = 0; i < warmupIterations; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await CollectLatencySample(-1, config.MessageSizeBytes, cancellationToken); // 预热样本使用负数序号
        }

        Logger.LogInformation("预热阶段完成");
    }

    private async Task<LatencySample> CollectLatencySample(int sequenceNumber, int messageSize, CancellationToken cancellationToken)
    {
        var sample = new LatencySample
        {
            SequenceNumber = sequenceNumber,
            MessageSize = messageSize,
            StartTime = DateTime.UtcNow,
            GlobalTimestamp = _globalTimer.Elapsed
        };

        if (BenchmarkService == null)
        {
            sample.EndTime = DateTime.UtcNow;
            sample.Success = false;
            sample.ErrorMessage = "BenchmarkService 未初始化";
            return sample;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 创建测试数据
            var testMessage = GenerateTestString(messageSize);
            var request = new EchoRequest
            {
                Message = testMessage,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var response = await BenchmarkService.EchoAsync(request, cancellationToken);

            stopwatch.Stop();

            sample.EndTime = DateTime.UtcNow;
            sample.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;

            // 验证响应
            if (response == null)
            {
                sample.Success = false;
                sample.ErrorMessage = "响应为null";
                Logger.LogWarning("延迟样本 #{SequenceNumber} 响应为null", sequenceNumber);
            }
            else if (!response.Success)
            {
                sample.Success = false;
                sample.ErrorMessage = response.ErrorMessage ?? "服务响应失败";
                Logger.LogWarning("延迟样本 #{SequenceNumber} 服务响应失败: {Error}", sequenceNumber, response.ErrorMessage);
            }
            else
            {
                sample.Success = true;
                Logger.LogTrace("延迟样本 #{SequenceNumber}: {Latency:F3}ms", sequenceNumber, sample.LatencyMs);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            sample.EndTime = DateTime.UtcNow;
            sample.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;
            sample.Success = false;
            sample.ErrorMessage = ex.Message;

            Logger.LogDebug(ex, "延迟样本 #{SequenceNumber} RPC调用失败", sequenceNumber);
        }

        return sample;
    }

    private void LogIntermediateAnalysis(int completedSamples)
    {
        lock (_lock)
        {
            if (_latencySamples.Count < 50) return; // 需要足够的样本才能分析

            var recentSamples = _latencySamples.TakeLast(500).Where(s => s.Success).Select(s => s.LatencyMs).ToArray();
            if (recentSamples.Length == 0) return;

            var avgLatency = recentSamples.Average();
            var p95Latency = GetPercentile(recentSamples.OrderBy(x => x).ToArray(), 95);

            Logger.LogDebug("中间分析 - 已完成: {Completed}, 最近500次平均延迟: {AvgLatency:F2}ms, P95: {P95:F2}ms",
                completedSamples, avgLatency, p95Latency);
        }
    }

    private void PerformAdvancedLatencyAnalysis(BenchmarkResult result)
    {
        var validSamples = _latencySamples.Where(s => s.Success).ToArray();
        var failedSamples = _latencySamples.Where(s => !s.Success).ToArray();

        if (validSamples.Length == 0)
        {
            Logger.LogWarning("没有有效的延迟样本数据");
            return;
        }

        var latencies = validSamples.Select(s => s.LatencyMs).OrderBy(x => x).ToArray();

        // 基础统计信息
        result.Latency.SampleCount = latencies.Length;
        result.Latency.AverageMs = latencies.Average();
        result.Latency.MinMs = latencies.Min();
        result.Latency.MaxMs = latencies.Max();
        result.Latency.MedianMs = GetPercentile(latencies, 50);
        result.Latency.P50Ms = result.Latency.MedianMs;
        result.Latency.P90Ms = GetPercentile(latencies, 90);
        result.Latency.P95Ms = GetPercentile(latencies, 95);
        result.Latency.P99Ms = GetPercentile(latencies, 99);
        result.Latency.P999Ms = GetPercentile(latencies, 99.9);
        result.Latency.StandardDeviationMs = CalculateStandardDeviation(latencies);

        // 异常值检测（使用 IQR 方法）
        var q1 = GetPercentile(latencies, 25);
        var q3 = GetPercentile(latencies, 75);
        var iqr = q3 - q1;
        var lowerBound = q1 - 1.5 * iqr;
        var upperBound = q3 + 1.5 * iqr;

        var outliers = latencies.Where(l => l < lowerBound || l > upperBound).ToArray();
        var outlierPercentage = (double)outliers.Length / latencies.Length * 100;

        // 延迟分布分析
        var latencyHistogram = CreateLatencyHistogram(latencies);

        // 趋势分析
        var trendAnalysis = AnalyzeLatencyTrend(validSamples);

        // 吞吐量统计
        result.Throughput.TotalOperations = _latencySamples.Count;
        result.Throughput.SuccessfulOperations = validSamples.Length;
        result.Throughput.FailedOperations = failedSamples.Length;

        var totalTimeSeconds = result.Duration.TotalSeconds;
        if (totalTimeSeconds > 0)
        {
            result.Throughput.OperationsPerSecond = validSamples.Length / totalTimeSeconds;
        }

        // 自定义指标
        result.SetCustomMetric("Q1LatencyMs", q1);
        result.SetCustomMetric("Q3LatencyMs", q3);
        result.SetCustomMetric("IQRMs", iqr);
        result.SetCustomMetric("OutlierCount", outliers.Length);
        result.SetCustomMetric("OutlierPercentage", outlierPercentage);
        result.SetCustomMetric("LatencyRange", result.Latency.MaxMs - result.Latency.MinMs);
        result.SetCustomMetric("CoefficientOfVariation", result.Latency.StandardDeviationMs / result.Latency.AverageMs);

        // 延迟分布
        result.SetCustomMetric("LatencyHistogram", latencyHistogram);

        // 趋势分析结果
        result.SetCustomMetric("TrendSlope", trendAnalysis.Slope);
        result.SetCustomMetric("TrendRSquared", trendAnalysis.RSquared);
        result.SetCustomMetric("TrendDirection", trendAnalysis.Direction);

        Logger.LogInformation("延迟分析统计 - 样本: {Samples}, 异常值: {Outliers} ({OutlierPct:F2}%), 趋势: {Trend}",
            latencies.Length, outliers.Length, outlierPercentage, trendAnalysis.Direction);
    }

    private Dictionary<string, int> CreateLatencyHistogram(double[] latencies)
    {
        var histogram = new Dictionary<string, int>();
        var buckets = new[]
        {
            (0.0, 1.0, "0-1ms"),
            (1.0, 5.0, "1-5ms"),
            (5.0, 10.0, "5-10ms"),
            (10.0, 25.0, "10-25ms"),
            (25.0, 50.0, "25-50ms"),
            (50.0, 100.0, "50-100ms"),
            (100.0, 250.0, "100-250ms"),
            (250.0, double.MaxValue, "250ms+")
        };

        foreach (var (min, max, label) in buckets)
        {
            var count = latencies.Count(l => l >= min && l < max);
            histogram[label] = count;
        }

        return histogram;
    }

    private LatencyTrendAnalysis AnalyzeLatencyTrend(LatencySample[] samples)
    {
        if (samples.Length < 10)
        {
            return new LatencyTrendAnalysis { Direction = "Insufficient Data" };
        }

        // 使用时间戳作为X轴，延迟作为Y轴进行线性回归
        var xValues = samples.Select((s, i) => (double)i).ToArray();
        var yValues = samples.Select(s => s.LatencyMs).ToArray();

        var xMean = xValues.Average();
        var yMean = yValues.Average();

        var numerator = xValues.Zip(yValues, (x, y) => (x - xMean) * (y - yMean)).Sum();
        var denominator = xValues.Sum(x => Math.Pow(x - xMean, 2));

        if (denominator == 0)
        {
            return new LatencyTrendAnalysis { Direction = "No Trend" };
        }

        var slope = numerator / denominator;

        // 计算R平方值
        var yPredicted = xValues.Select(x => yMean + slope * (x - xMean)).ToArray();
        var ssRes = yValues.Zip(yPredicted, (actual, predicted) => Math.Pow(actual - predicted, 2)).Sum();
        var ssTot = yValues.Sum(y => Math.Pow(y - yMean, 2));
        var rSquared = ssTot > 0 ? 1 - (ssRes / ssTot) : 0;

        var direction = Math.Abs(slope) < 0.001 ? "Stable" : slope > 0 ? "Increasing" : "Decreasing";

        return new LatencyTrendAnalysis
        {
            Slope = slope,
            RSquared = rSquared,
            Direction = direction
        };
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

    /// <summary>
    /// 延迟样本数据
    /// </summary>
    private class LatencySample
    {
        public int SequenceNumber { get; set; }
        public int MessageSize { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan GlobalTimestamp { get; set; }
        public double LatencyMs { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 延迟趋势分析结果
    /// </summary>
    private class LatencyTrendAnalysis
    {
        public double Slope { get; set; }
        public double RSquared { get; set; }
        public string Direction { get; set; } = string.Empty;
    }
}
