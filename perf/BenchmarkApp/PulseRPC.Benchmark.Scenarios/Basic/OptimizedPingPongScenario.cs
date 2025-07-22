using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Basic;

/// <summary>
/// 高性能优化的Ping-Pong测试场景
/// 专门用于测量RPC框架的真实延迟，移除了所有不必要的开销
/// </summary>
public class OptimizedPingPongScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly List<double> _pingLatencies = new();
    private IOptimizedBenchmarkService? _optimizedService;

    public override string ScenarioName => "Optimized Ping-Pong Test";
    public override string Description => "高性能优化的Ping-Pong延迟测试，专注于测量纯RPC开销";
    public override string Category => "Performance";

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
            _pingLatencies.Clear();
            var iterations = config.Iterations;

            // 快速预热 - 最少次数
            await QuickWarmup(Math.Min(5, iterations / 10), cancellationToken);

            // 主测试阶段 - 零开销循环
            var startTicks = Stopwatch.GetTimestamp();
            
            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var latency = await ExecuteOptimizedPing(i + 1, cancellationToken);
                _pingLatencies.Add(latency);
            }

            var endTicks = Stopwatch.GetTimestamp();
            var totalTimeMs = (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;

            // 填充统计数据
            PopulateOptimizedStatistics(result, totalTimeMs);
            
            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            // 只在完成后记录一次日志
            Logger.LogInformation("优化Ping-Pong测试完成: {Iterations}次, 平均延迟: {AvgLatency:F3}ms", 
                iterations, result.Latency.AverageMs);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "优化Ping-Pong测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// 快速预热 - 最小化开销
    /// </summary>
    private async Task QuickWarmup(int warmupCount, CancellationToken cancellationToken)
    {
        for (int i = 0; i < warmupCount; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await ExecuteOptimizedPing(i + 1, cancellationToken);
        }
    }

    /// <summary>
    /// 执行优化的Ping调用 - 最小化测量开销
    /// </summary>
    private async Task<double> ExecuteOptimizedPing(int sequenceNumber, CancellationToken cancellationToken)
    {
        // 使用最高精度计时
        var startTicks = Stopwatch.GetTimestamp();

        try
        {
            // 使用简化的消息结构
            var request = new OptimizedPingRequest(sequenceNumber, sequenceNumber);
            var response = await _optimizedService!.OptimizedPingAsync(request, cancellationToken);
            
            var endTicks = Stopwatch.GetTimestamp();
            
            // 简单验证
            if (response?.Success != true)
                return 9999.0;
                
            // 直接计算延迟，避免额外的操作
            return (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
        }
        catch
        {
            return 9999.0; // 快速错误标记
        }
    }

    /// <summary>
    /// 优化的统计数据填充
    /// </summary>
    private void PopulateOptimizedStatistics(BenchmarkResult result, double totalTimeMs)
    {
        if (_pingLatencies.Count == 0) return;

        // 过滤有效数据
        var validLatencies = _pingLatencies.Where(l => l < 9999.0).ToArray();
        if (validLatencies.Length == 0) return;

        Array.Sort(validLatencies); // 排序用于百分位数计算

        result.Latency.SampleCount = validLatencies.Length;
        result.Latency.AverageMs = validLatencies.Average();
        result.Latency.MinMs = validLatencies[0];
        result.Latency.MaxMs = validLatencies[^1];
        result.Latency.MedianMs = GetPercentile(validLatencies, 0.5);
        result.Latency.P50Ms = result.Latency.MedianMs;
        result.Latency.P90Ms = GetPercentile(validLatencies, 0.9);
        result.Latency.P95Ms = GetPercentile(validLatencies, 0.95);
        result.Latency.P99Ms = GetPercentile(validLatencies, 0.99);
        result.Latency.P999Ms = GetPercentile(validLatencies, 0.999);
        result.Latency.StandardDeviationMs = CalculateStandardDeviation(validLatencies);

        // 吞吐量统计
        result.Throughput.TotalOperations = _pingLatencies.Count;
        result.Throughput.SuccessfulOperations = validLatencies.Length;
        result.Throughput.FailedOperations = _pingLatencies.Count - validLatencies.Length;
        
        if (totalTimeMs > 0)
        {
            result.Throughput.OperationsPerSecond = validLatencies.Length / (totalTimeMs / 1000.0);
        }

        // 简化指标
        result.SetCustomMetric("ValidSamples", validLatencies.Length);
        result.SetCustomMetric("ErrorRate", (double)(_pingLatencies.Count - validLatencies.Length) / _pingLatencies.Count * 100);
    }

    /// <summary>
    /// 快速百分位数计算
    /// </summary>
    private static double GetPercentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;

        var index = percentile * (sortedValues.Length - 1);
        var lower = (int)index;
        var upper = Math.Min(lower + 1, sortedValues.Length - 1);
        var weight = index - lower;

        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }

    /// <summary>
    /// 快速标准差计算
    /// </summary>
    private static double CalculateStandardDeviation(double[] values)
    {
        if (values.Length <= 1) return 0;

        var mean = values.Average();
        var sumOfSquares = values.Sum(x => (x - mean) * (x - mean));
        return Math.Sqrt(sumOfSquares / values.Length);
    }

    /// <summary>
    /// 初始化优化服务
    /// </summary>
    public void SetOptimizedService(IOptimizedBenchmarkService service)
    {
        _optimizedService = service ?? throw new ArgumentNullException(nameof(service));
    }
}