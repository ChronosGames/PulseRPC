using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Basic;

/// <summary>
/// 上行带宽测试场景
/// 测试客户端到服务端的数据传输带宽
/// </summary>
public class UploadBandwidthScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly List<double> _operationTimes = new();
    private readonly object _lock = new();
    private volatile int _completedOperations = 0;
    private volatile int _failedOperations = 0;
    private long _totalBytesSent = 0;

    public override string ScenarioName => "Upload Bandwidth";
    public override string Description => "测试客户端到服务端的数据传输带宽";
    public override string Category => ScenarioCategories.Throughput;

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
            Logger.LogInformation("开始上行带宽测试，目标迭代次数: {Iterations}, 并发连接数: {Connections}, 数据包大小: {Size}B",
                config.Iterations, config.ConcurrentConnections, config.MessageSizeBytes);

            _operationTimes.Clear();
            _completedOperations = 0;
            _failedOperations = 0;
            _totalBytesSent = 0;

            var iterations = config.Iterations;
            var concurrentConnections = Math.Max(1, config.ConcurrentConnections);
            var payloadSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

            // 预热阶段
            await WarmupPhase(config, cancellationToken);

            // 主测试阶段
            var testTasks = new List<Task>();
            var iterationsPerConnection = iterations / concurrentConnections;
            var remainingIterations = iterations % concurrentConnections;

            var totalStopwatch = Stopwatch.StartNew();

            for (int connectionId = 0; connectionId < concurrentConnections; connectionId++)
            {
                var iterationsForThisConnection = iterationsPerConnection;
                if (connectionId < remainingIterations)
                {
                    iterationsForThisConnection++;
                }

                if (iterationsForThisConnection > 0)
                {
                    var task = ExecuteConnectionTestAsync(connectionId, iterationsForThisConnection, payloadSize, cancellationToken);
                    testTasks.Add(task);
                }
            }

            await Task.WhenAll(testTasks);

            totalStopwatch.Stop();

            // 填充结果统计数据
            PopulateStatistics(result, totalStopwatch.Elapsed, payloadSize);

            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            var bandwidthMBps = result.Throughput.AverageBandwidthBps / (1024.0 * 1024.0);
            Logger.LogInformation("上行带宽测试完成，总操作数: {Total}, 成功: {Success}, 带宽: {Bandwidth:F2} MB/s, 平均延迟: {Latency:F2}ms",
                _completedOperations + _failedOperations, _completedOperations, bandwidthMBps, result.Latency.AverageMs);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "上行带宽测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task WarmupPhase(BenchmarkConfiguration config, CancellationToken cancellationToken)
    {
        var warmupIterations = Math.Min(config.WarmupIterations, 100);
        var payloadSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

        Logger.LogInformation("开始预热阶段，预热次数: {WarmupIterations}", warmupIterations);

        for (int i = 0; i < warmupIterations; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ExecuteSingleUploadOperation(payloadSize, i, cancellationToken);
        }

        Logger.LogInformation("预热阶段完成");
    }

    private async Task ExecuteConnectionTestAsync(int connectionId, int iterations, int payloadSize, CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogDebug("连接 {ConnectionId} 开始执行 {Iterations} 次上行操作", connectionId, iterations);

            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var operationTime = await ExecuteSingleUploadOperation(payloadSize, i, cancellationToken);

                    lock (_lock)
                    {
                        _operationTimes.Add(operationTime);
                        _completedOperations++;
                        _totalBytesSent += payloadSize;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("连接 {ConnectionId} 上行操作失败: {Error}", connectionId, ex.Message);
                    Interlocked.Increment(ref _failedOperations);
                }
            }

            Logger.LogDebug("连接 {ConnectionId} 完成所有上行操作", connectionId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "连接 {ConnectionId} 执行失败", connectionId);
        }
    }

    private async Task<double> ExecuteSingleUploadOperation(int payloadSize, int sequenceNumber, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        var stopwatch = Stopwatch.StartNew();

        var requestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(0, 1000);
        var request = UploadRequest.Create(requestId, sequenceNumber, payloadSize);

        var response = await BenchmarkService.UploadAsync(request, cancellationToken);

        stopwatch.Stop();

        if (!response.Success)
        {
            throw new InvalidOperationException($"上行请求失败: {response.ErrorMessage}");
        }

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private void PopulateStatistics(BenchmarkResult result, TimeSpan totalTime, int payloadSize)
    {
        var totalOperations = _completedOperations + _failedOperations;

        result.Throughput.TotalOperations = totalOperations;
        result.Throughput.SuccessfulOperations = _completedOperations;
        result.Throughput.FailedOperations = _failedOperations;
        result.Throughput.TotalBytesTransferred = _totalBytesSent;

        var totalTimeSeconds = totalTime.TotalSeconds;
        if (totalTimeSeconds > 0)
        {
            result.Throughput.OperationsPerSecond = _completedOperations / totalTimeSeconds;
            result.Throughput.AverageBandwidthBps = _totalBytesSent / totalTimeSeconds;
        }

        // 计算延迟统计
        if (_operationTimes.Count > 0)
        {
            lock (_lock)
            {
                var sortedTimes = _operationTimes.OrderBy(x => x).ToArray();

                result.Latency.SampleCount = sortedTimes.Length;
                result.Latency.AverageMs = sortedTimes.Average();
                result.Latency.MinMs = sortedTimes.Min();
                result.Latency.MaxMs = sortedTimes.Max();
                result.Latency.MedianMs = GetPercentile(sortedTimes, 50);
                result.Latency.P50Ms = result.Latency.MedianMs;
                result.Latency.P90Ms = GetPercentile(sortedTimes, 90);
                result.Latency.P95Ms = GetPercentile(sortedTimes, 95);
                result.Latency.P99Ms = GetPercentile(sortedTimes, 99);
                result.Latency.P999Ms = GetPercentile(sortedTimes, 99.9);
                result.Latency.StandardDeviationMs = CalculateStandardDeviation(sortedTimes);
            }
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

    public override BenchmarkConfiguration GetDefaultConfiguration()
    {
        return new BenchmarkConfiguration
        {
            Host = "localhost",
            TcpPort = 8080,
            Iterations = 10000,
            ConcurrentConnections = 10,
            MessageSizeBytes = 1024,  // 默认 1KB
            WarmupIterations = 100,
            TestIntervalMs = 0,
            EnableVerboseLogging = false,
            CollectResourceMetrics = true
        };
    }
}
