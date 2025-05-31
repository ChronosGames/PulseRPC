using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Basic;

/// <summary>
/// 吞吐量测试场景
/// 测量系统的吞吐量和处理能力
/// </summary>
public class ThroughputScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly List<double> _operationTimes = new();
    private readonly object _lock = new object();
    private volatile int _completedOperations = 0;
    private volatile int _failedOperations = 0;

    public override string ScenarioName => "Throughput Test";
    public override string Description => "测量系统的吞吐量和处理能力";
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
            Logger.LogInformation("开始吞吐量测试，目标迭代次数: {Iterations}, 并发连接数: {Connections}",
                config.Iterations, config.ConcurrentConnections);

            _operationTimes.Clear();
            _completedOperations = 0;
            _failedOperations = 0;

            var iterations = config.Iterations;
            var concurrentConnections = Math.Max(1, config.ConcurrentConnections);
            var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

            // 预热阶段
            await WarmupPhase(config, cancellationToken);

            // 主测试阶段 - 使用并发执行
            var testTasks = new List<Task>();
            var iterationsPerConnection = iterations / concurrentConnections;
            var remainingIterations = iterations % concurrentConnections;

            for (int connectionId = 0; connectionId < concurrentConnections; connectionId++)
            {
                var iterationsForThisConnection = iterationsPerConnection;
                if (connectionId < remainingIterations)
                {
                    iterationsForThisConnection++;
                }

                if (iterationsForThisConnection > 0)
                {
                    var task = ExecuteConnectionTestAsync(connectionId, iterationsForThisConnection, messageSize, cancellationToken);
                    testTasks.Add(task);
                }
            }

            // 等待所有连接完成测试
            await Task.WhenAll(testTasks);

            // 填充结果统计数据
            PopulateThroughputStatistics(result);

            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("吞吐量测试完成，总操作数: {Total}, 成功: {Success}, 失败: {Failed}, 平均 OPS: {AvgOps:F2}",
                _completedOperations + _failedOperations, _completedOperations, _failedOperations, result.Throughput.OperationsPerSecond);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "吞吐量测试失败");
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

            await ExecuteSingleThroughputOperation(config.MessageSizeBytes, cancellationToken);
        }

        Logger.LogInformation("预热阶段完成");
    }

    private async Task ExecuteConnectionTestAsync(int connectionId, int iterations, int messageSize, CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogDebug("连接 {ConnectionId} 开始执行 {Iterations} 次操作", connectionId, iterations);

            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("连接 {ConnectionId} 被取消，当前完成 {Current}/{Total} 次", connectionId, i, iterations);
                    break;
                }

                try
                {
                    var operationTime = await ExecuteSingleThroughputOperation(messageSize, cancellationToken);

                    lock (_lock)
                    {
                        _operationTimes.Add(operationTime);
                        _completedOperations++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("连接 {ConnectionId} 操作失败: {Error}", connectionId, ex.Message);
                    Interlocked.Increment(ref _failedOperations);
                }

                // 每1000次记录一次进度
                if ((i + 1) % 1000 == 0)
                {
                    Logger.LogDebug("连接 {ConnectionId} 已完成 {Current}/{Total} 次操作", connectionId, i + 1, iterations);
                }
            }

            Logger.LogDebug("连接 {ConnectionId} 完成所有操作", connectionId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "连接 {ConnectionId} 执行失败", connectionId);
        }
    }

    private async Task<double> ExecuteSingleThroughputOperation(int messageSize, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 创建测试数据
            var testMessage = GenerateTestString(messageSize);
            var requestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(0, 1000);
            var request = new ThroughputTestRequest
            {
                RequestId = requestId,
                ClientId = "ThroughputClient",
                BatchNumber = 0,
                BatchSize = 1,
                MessageSize = messageSize,
                DurationSeconds = 30  // 默认30秒
            };

            var response = await BenchmarkService.ThroughputTestAsync(request, cancellationToken);

            stopwatch.Stop();

            // 验证响应
            if (response == null)
            {
                Logger.LogWarning("ThroughputTest响应为null");
                throw new InvalidOperationException("服务响应为null");
            }

            if (!response.Success)
            {
                Logger.LogWarning("ThroughputTest响应失败: {Error}", response.ErrorMessage);
                throw new InvalidOperationException($"服务响应失败: {response.ErrorMessage}");
            }

            // 返回操作时间（毫秒）
            return stopwatch.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "ThroughputTest RPC调用失败");
            throw; // 重新抛出异常，让调用方处理
        }
    }

    private void PopulateThroughputStatistics(BenchmarkResult result)
    {
        var totalOperations = _completedOperations + _failedOperations;

        result.Throughput.TotalOperations = totalOperations;
        result.Throughput.SuccessfulOperations = _completedOperations;
        result.Throughput.FailedOperations = _failedOperations;

        var totalTimeSeconds = result.Duration.TotalSeconds;
        if (totalTimeSeconds > 0)
        {
            result.Throughput.OperationsPerSecond = _completedOperations / totalTimeSeconds;
        }

        // 计算带宽（假设每个操作传输的数据量）
        var bytesPerOperation = result.Configuration.MessageSizeBytes * 2; // 请求 + 响应
        var totalBytes = _completedOperations * bytesPerOperation;
        result.Throughput.TotalBytesTransferred = totalBytes;

        if (totalTimeSeconds > 0)
        {
            result.Throughput.AverageBandwidthBps = totalBytes / totalTimeSeconds;
        }

        // 如果有操作时间数据，计算延迟统计
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
}

