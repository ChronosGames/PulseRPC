using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Basic;

/// <summary>
/// Echo延迟测试场景
/// 测量单次RPC调用的往返时间(RTT)
/// </summary>
public class EchoLatencyScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly List<double> _latencyMeasurements = [];
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public override string ScenarioName => "Echo Latency Test";
    public override string Description => "测量单次RPC调用的往返时间(RTT)";
    public override string Category => "Basic";

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
            Logger.LogInformation("开始Echo延迟测试，目标迭代次数: {Iterations}", config.Iterations);

            _latencyMeasurements.Clear();
            var iterations = config.Iterations;
            var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

            // 预热阶段
            await WarmupPhase(config, cancellationToken);

            // 主测试阶段
            var successfulCalls = 0;
            var failedCalls = 0;

            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("测试被取消，当前完成 {Current}/{Total} 次", i, iterations);
                    break;
                }

                var latency = await MeasureSingleEchoLatency(messageSize, cancellationToken);

                if (latency >= 0)
                {
                    _latencyMeasurements.Add(latency);
                    successfulCalls++;
                }
                else
                {
                    failedCalls++;
                }

                // 可选的测试间隔
                if (config.TestIntervalMs > 0)
                {
                    await Task.Delay(config.TestIntervalMs, cancellationToken);
                }

                // 每100次记录一次进度
                if ((i + 1) % 100 == 0)
                {
                    Logger.LogDebug("已完成 {Current}/{Total} 次延迟测试，成功: {Success}, 失败: {Failed}",
                        i + 1, iterations, successfulCalls, failedCalls);
                }
            }

            // 填充结果统计数据
            PopulateLatencyStatistics(result, successfulCalls, failedCalls);

            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("Echo延迟测试完成，平均延迟: {AvgLatency:F2}ms", result.Latency.AverageMs);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Echo延迟测试失败");
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

            await MeasureSingleEchoLatency(config.MessageSizeBytes, cancellationToken);
        }

        Logger.LogInformation("预热阶段完成");
    }

    private async Task<double> MeasureSingleEchoLatency(int messageSize, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        // 创建测试数据
        var testMessage = GenerateTestString(messageSize);
        var request = new EchoRequest
        {
            Message = testMessage,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // 测量实际RPC调用延迟
        _stopwatch.Restart();

        try
        {
            var response = await BenchmarkService.EchoAsync(request, cancellationToken);

            _stopwatch.Stop();

            // 验证响应
            if (response == null)
            {
                Logger.LogWarning("Echo响应为null");
                return -1; // 标记为失败
            }

            if (!response.Success)
            {
                Logger.LogWarning("Echo响应失败: {Error}", response.ErrorMessage);
                return -1; // 标记为失败
            }

            // 返回测量的延迟（毫秒）
            return _stopwatch.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            Logger.LogError(ex, "Echo RPC调用失败");
            return -1; // 标记为失败
        }
    }

    private void PopulateLatencyStatistics(BenchmarkResult result, int successfulCalls, int failedCalls)
    {
        if (_latencyMeasurements.Count == 0)
        {
            Logger.LogWarning("没有收集到延迟测量数据");
            return;
        }

        var sortedLatencies = _latencyMeasurements.OrderBy(x => x).ToArray();
        var count = sortedLatencies.Length;

        result.Latency.SampleCount = count;
        result.Latency.AverageMs = sortedLatencies.Average();
        result.Latency.MinMs = sortedLatencies.Min();
        result.Latency.MaxMs = sortedLatencies.Max();
        result.Latency.MedianMs = GetPercentile(sortedLatencies, 50);
        result.Latency.P50Ms = result.Latency.MedianMs;
        result.Latency.P90Ms = GetPercentile(sortedLatencies, 90);
        result.Latency.P95Ms = GetPercentile(sortedLatencies, 95);
        result.Latency.P99Ms = GetPercentile(sortedLatencies, 99);
        result.Latency.P999Ms = GetPercentile(sortedLatencies, 99.9);
        result.Latency.StandardDeviationMs = CalculateStandardDeviation(sortedLatencies);

        // 填充吞吐量数据
        result.Throughput.TotalOperations = count;
        result.Throughput.SuccessfulOperations = successfulCalls;
        result.Throughput.FailedOperations = failedCalls;

        var totalTimeSeconds = result.Duration.TotalSeconds;
        if (totalTimeSeconds > 0)
        {
            result.Throughput.OperationsPerSecond = count / totalTimeSeconds;
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
