using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Basic;

/// <summary>
/// Ping-Pong测试场景
/// 连续发送Ping请求，测量网络延迟和抖动
/// </summary>
public class PingPongScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly List<double> _pingLatencies = new();
    private readonly List<double> _intervalTimes = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private DateTime _lastPingTime;

    public override string ScenarioName => "Ping-Pong Test";
    public override string Description => "连续发送Ping请求，测量网络延迟和抖动";
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
            Logger.LogInformation("开始Ping-Pong测试，目标Ping次数: {Iterations}, 间隔: {IntervalMs}ms",
                config.Iterations, config.TestIntervalMs);

            _pingLatencies.Clear();
            _intervalTimes.Clear();

            var iterations = config.Iterations;
            var pingInterval = Math.Max(0, config.TestIntervalMs);
            var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 64; // Ping通常使用较小的包

            // 预热阶段
            await WarmupPhase(config, cancellationToken);

            // 主测试阶段 - 连续Ping测试
            _lastPingTime = DateTime.UtcNow;

            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("Ping-Pong测试被取消，当前完成 {Current}/{Total} 次", i, iterations);
                    break;
                }

                var currentPingTime = DateTime.UtcNow;

                // 记录间隔时间（从第二次Ping开始）
                if (i > 0)
                {
                    var intervalMs = (currentPingTime - _lastPingTime).TotalMilliseconds;
                    _intervalTimes.Add(intervalMs);
                }

                // 执行单次Ping
                var pingLatency = await ExecuteSinglePing(i + 1, messageSize, cancellationToken);
                _pingLatencies.Add(pingLatency);

                _lastPingTime = currentPingTime;

                // 等待指定间隔
                if (pingInterval > 0 && i < iterations - 1) // 最后一次不需要等待
                {
                    await Task.Delay(pingInterval, cancellationToken);
                }

                // 每100次记录一次进度
                if ((i + 1) % 100 == 0)
                {
                    var avgLatency = _pingLatencies.TakeLast(100).Average();
                    Logger.LogDebug("已完成 {Current}/{Total} 次Ping，最近100次平均延迟: {AvgLatency:F2}ms",
                        i + 1, iterations, avgLatency);
                }
            }

            // 填充结果统计数据
            PopulatePingPongStatistics(result);

            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("Ping-Pong测试完成，平均延迟: {AvgLatency:F2}ms, 抖动: {Jitter:F2}ms",
                result.Latency.AverageMs, result.Latency.StandardDeviationMs);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ping-Pong测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task WarmupPhase(BenchmarkConfiguration config, CancellationToken cancellationToken)
    {
        var warmupIterations = Math.Min(config.WarmupIterations, 20); // Ping测试预热不需要太多次
        Logger.LogInformation("开始预热阶段，预热次数: {WarmupIterations}", warmupIterations);

        for (int i = 0; i < warmupIterations; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ExecuteSinglePing(i + 1, config.MessageSizeBytes, cancellationToken);

            // 预热阶段也遵循间隔设置
            if (config.TestIntervalMs > 0 && i < warmupIterations - 1)
            {
                await Task.Delay(config.TestIntervalMs, cancellationToken);
            }
        }

        Logger.LogInformation("预热阶段完成");
    }

    private async Task<double> ExecuteSinglePing(int sequenceNumber, int messageSize, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        _stopwatch.Restart();

        // 创建Ping请求
        var request = new PingRequest
        {
            RequestId = sequenceNumber,
            ClientId = "PingPongClient",
            SequenceNumber = sequenceNumber,
            PayloadSize = 32  // 默认32字节负载
        };

        try
        {
            var response = await BenchmarkService.PingAsync(request, cancellationToken);

            _stopwatch.Stop();

            var latency = _stopwatch.Elapsed.TotalMilliseconds;

            // 验证响应
            if (response == null)
            {
                Logger.LogWarning("Ping #{SequenceNumber} 响应为null", sequenceNumber);
                return 9999.0; // 标记为失败
            }

            if (!response.Success)
            {
                Logger.LogWarning("Ping #{SequenceNumber} 响应失败: {Error}", sequenceNumber, response.ErrorMessage);
                return 9999.0; // 标记为失败
            }

            Logger.LogTrace("Ping #{SequenceNumber}: {Latency:F2}ms", sequenceNumber, latency);

            return latency;
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            Logger.LogWarning(ex, "Ping #{SequenceNumber} RPC调用失败: {Error}", sequenceNumber, ex.Message);

            // 返回一个较大的延迟值表示失败，但不抛出异常以继续测试
            return 9999.0;
        }
    }

    private void PopulatePingPongStatistics(BenchmarkResult result)
    {
        if (_pingLatencies.Count == 0)
        {
            Logger.LogWarning("没有收集到Ping延迟数据");
            return;
        }

        // 过滤掉失败的Ping（延迟 >= 9999ms）
        var validLatencies = _pingLatencies.Where(l => l < 9999.0).ToArray();
        var failedPings = _pingLatencies.Count - validLatencies.Length;

        if (validLatencies.Length > 0)
        {
            var sortedLatencies = validLatencies.OrderBy(x => x).ToArray();

            result.Latency.SampleCount = sortedLatencies.Length;
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
        }

        // 填充吞吐量数据
        result.Throughput.TotalOperations = _pingLatencies.Count;
        result.Throughput.SuccessfulOperations = validLatencies.Length;
        result.Throughput.FailedOperations = failedPings;

        var totalTimeSeconds = result.Duration.TotalSeconds;
        if (totalTimeSeconds > 0)
        {
            result.Throughput.OperationsPerSecond = validLatencies.Length / totalTimeSeconds;
        }

        // 计算间隔时间统计（抖动分析）
        if (_intervalTimes.Count > 0)
        {
            var avgInterval = _intervalTimes.Average();
            var intervalStdDev = CalculateStandardDeviation(_intervalTimes.ToArray());

            result.SetCustomMetric("AverageIntervalMs", avgInterval);
            result.SetCustomMetric("IntervalStandardDeviationMs", intervalStdDev);
            result.SetCustomMetric("IntervalJitterMs", intervalStdDev); // 抖动定义为间隔的标准差
        }

        // 记录其他有用的指标
        result.SetCustomMetric("PacketLossPercentage", (double)failedPings / _pingLatencies.Count * 100);
        result.SetCustomMetric("TotalPingsSent", _pingLatencies.Count);
        result.SetCustomMetric("SuccessfulPings", validLatencies.Length);
        result.SetCustomMetric("FailedPings", failedPings);
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
