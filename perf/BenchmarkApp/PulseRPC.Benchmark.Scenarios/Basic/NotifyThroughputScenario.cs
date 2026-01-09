using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Basic;

/// <summary>
/// Notify 吞吐量测试场景
/// 测试无返回值的单向消息（Fire-and-Forget）吞吐量
/// </summary>
public class NotifyThroughputScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private volatile int _completedOperations = 0;
    private volatile int _failedOperations = 0;

    public override string ScenarioName => "Notify Throughput";
    public override string Description => "测试无返回值的单向消息（Fire-and-Forget）吞吐量";
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
            Logger.LogInformation("开始 Notify 吞吐量测试，目标迭代次数: {Iterations}, 并发连接数: {Connections}",
                config.Iterations, config.ConcurrentConnections);

            _completedOperations = 0;
            _failedOperations = 0;

            var iterations = config.Iterations;
            var concurrentConnections = Math.Max(1, config.ConcurrentConnections);
            var payloadSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 64;

            // 预热阶段
            await WarmupPhase(config, cancellationToken);

            // 主测试阶段 - 使用并发执行
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

            // 等待所有连接完成测试
            await Task.WhenAll(testTasks);

            totalStopwatch.Stop();

            // 填充结果统计数据
            PopulateStatistics(result, totalStopwatch.Elapsed, payloadSize);

            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("Notify 吞吐量测试完成，总操作数: {Total}, 成功: {Success}, 失败: {Failed}, OPS: {Ops:N0}",
                _completedOperations + _failedOperations, _completedOperations, _failedOperations,
                result.Throughput.OperationsPerSecond);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Notify 吞吐量测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task WarmupPhase(BenchmarkConfiguration config, CancellationToken cancellationToken)
    {
        var warmupIterations = Math.Min(config.WarmupIterations, 1000);
        Logger.LogInformation("开始预热阶段，预热次数: {WarmupIterations}", warmupIterations);

        var payloadSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 64;

        for (int i = 0; i < warmupIterations; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ExecuteSingleNotifyOperation(payloadSize, i, cancellationToken);
        }

        Logger.LogInformation("预热阶段完成");
    }

    private async Task ExecuteConnectionTestAsync(int connectionId, int iterations, int payloadSize, CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogDebug("连接 {ConnectionId} 开始执行 {Iterations} 次 Notify 操作", connectionId, iterations);

            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("连接 {ConnectionId} 被取消，当前完成 {Current}/{Total} 次", connectionId, i, iterations);
                    break;
                }

                try
                {
                    await ExecuteSingleNotifyOperation(payloadSize, i, cancellationToken);
                    Interlocked.Increment(ref _completedOperations);
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("连接 {ConnectionId} Notify 操作失败: {Error}", connectionId, ex.Message);
                    Interlocked.Increment(ref _failedOperations);
                }
            }

            Logger.LogDebug("连接 {ConnectionId} 完成所有 Notify 操作", connectionId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "连接 {ConnectionId} 执行失败", connectionId);
        }
    }

    private async ValueTask ExecuteSingleNotifyOperation(int payloadSize, int sequenceNumber, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        var requestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(0, 1000);
        var request = NotifyRequest.Create(requestId, sequenceNumber, payloadSize);

        await BenchmarkService.NotifyAsync(request);
    }

    private void PopulateStatistics(BenchmarkResult result, TimeSpan totalTime, int payloadSize)
    {
        var totalOperations = _completedOperations + _failedOperations;

        result.Throughput.TotalOperations = totalOperations;
        result.Throughput.SuccessfulOperations = _completedOperations;
        result.Throughput.FailedOperations = _failedOperations;

        var totalTimeSeconds = totalTime.TotalSeconds;
        if (totalTimeSeconds > 0)
        {
            result.Throughput.OperationsPerSecond = _completedOperations / totalTimeSeconds;
        }

        // 计算带宽（仅上行，因为 Notify 没有响应）
        var totalBytes = (long)_completedOperations * payloadSize;
        result.Throughput.TotalBytesTransferred = totalBytes;

        if (totalTimeSeconds > 0)
        {
            result.Throughput.AverageBandwidthBps = totalBytes / totalTimeSeconds;
        }

        // Notify 没有延迟统计（Fire-and-Forget）
        result.Latency.SampleCount = 0;
        result.Latency.AverageMs = 0;
    }

    public override BenchmarkConfiguration GetDefaultConfiguration()
    {
        return new BenchmarkConfiguration
        {
            Host = "localhost",
            TcpPort = 8080,
            Iterations = 100000,
            ConcurrentConnections = 10,
            MessageSizeBytes = 64,
            WarmupIterations = 1000,
            TestIntervalMs = 0,
            EnableVerboseLogging = false,
            CollectResourceMetrics = true
        };
    }
}
