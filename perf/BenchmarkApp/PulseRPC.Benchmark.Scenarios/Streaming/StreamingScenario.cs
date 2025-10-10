using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Streaming;

/// <summary>
/// 流式测试场景
/// 测试双向流式传输的性能和持续消息流
/// </summary>
public class StreamingScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly List<double> _latencySamples = new();
    private long _messagesSent;
    private long _messagesReceived;
    private long _errors;

    public override string ScenarioName => "Streaming Performance Test";
    public override string Description => "测试双向流式传输的性能和持续消息流";
    public override string Category => ScenarioCategories.Streaming;

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
            Logger.LogInformation("开始流式测试，持续时间: {Duration}秒", config.DurationSeconds);

            _latencySamples.Clear();
            _messagesSent = 0;
            _messagesReceived = 0;
            _errors = 0;

            // 预热阶段
            if (config.WarmupSeconds > 0)
            {
                Logger.LogInformation("预热 {Warmup} 秒", config.WarmupSeconds);
                await WarmupPhase(config, cancellationToken);
            }

            // 主测试阶段
            var testDuration = TimeSpan.FromSeconds(config.DurationSeconds);
            var stopwatch = Stopwatch.StartNew();
            var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

            // 模拟流式测试 - 连续发送消息直到时间到达
            while (stopwatch.Elapsed < testDuration && !cancellationToken.IsCancellationRequested)
            {
                var latency = await SendStreamMessage(messageSize, cancellationToken);
                
                if (latency >= 0)
                {
                    _latencySamples.Add(latency);
                    _messagesSent++;
                    _messagesReceived++;
                }
                else
                {
                    _errors++;
                }

                // 进度报告
                if (_messagesSent % 100 == 0)
                {
                    Logger.LogDebug("已发送: {Sent}, 已接收: {Received}, 错误: {Errors}",
                        _messagesSent, _messagesReceived, _errors);
                }

                // 速率限制（如果配置了）
                if (config.TestIntervalMs > 0)
                {
                    await Task.Delay(config.TestIntervalMs, cancellationToken);
                }
            }

            stopwatch.Stop();

            // 计算指标
            PopulateMetrics(result);

            result.IsSuccessful = _errors == 0 && _messagesReceived > 0;
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("流式测试完成。已发送: {Sent}, 已接收: {Received}, 错误: {Errors}",
                _messagesSent, _messagesReceived, _errors);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "流式测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task WarmupPhase(BenchmarkConfiguration config, CancellationToken cancellationToken)
    {
        var warmupDuration = TimeSpan.FromSeconds(config.WarmupSeconds);
        var stopwatch = Stopwatch.StartNew();
        var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

        while (stopwatch.Elapsed < warmupDuration && !cancellationToken.IsCancellationRequested)
        {
            await SendStreamMessage(messageSize, cancellationToken);
            if (config.TestIntervalMs > 0)
            {
                await Task.Delay(config.TestIntervalMs, cancellationToken);
            }
        }

        Logger.LogInformation("预热阶段完成");
    }

    private async Task<double> SendStreamMessage(int messageSize, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        try
        {
            var request = new StreamTestRequest
            {
                StreamId = Guid.NewGuid().ToString(),
                StreamType = "Bidirectional",
                ChunkSize = messageSize,
                TotalChunks = 1,
                ChunkIndex = 0,
                IsLastChunk = true,
                Command = "Data"
            };

            var stopwatch = Stopwatch.StartNew();
            var response = await BenchmarkService.StreamTestAsync(request, cancellationToken);
            stopwatch.Stop();

            if (response != null && response.Success)
            {
                return stopwatch.Elapsed.TotalMilliseconds;
            }

            return -1;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "流式消息发送失败");
            return -1;
        }
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
        result.Throughput.TotalOperations = _messagesSent;
        result.Throughput.SuccessfulOperations = _messagesReceived;
        result.Throughput.FailedOperations = _errors;
        
        if (duration > 0)
        {
            result.Throughput.OperationsPerSecond = _messagesSent / duration;
            result.Throughput.TotalBytesTransferred = _messagesSent * result.Configuration.MessageSizeBytes;
            result.Throughput.AverageBandwidthBps = result.Throughput.TotalBytesTransferred / duration;
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
