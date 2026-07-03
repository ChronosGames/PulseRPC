using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;
using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// 稳定性测试场景
/// </summary>
public class StabilityScenario : ScenarioBase
{
    public StabilityScenario(ILogger<StabilityScenario> logger) : base(logger) { }

    public override string Name => "stability";
    public override string Description => "长时间运行测试，监控内存泄漏和连接稳定性";

    public override async Task<BenchmarkResult> RunAsync(IReadOnlyList<IBenchmarkHub> services, BenchmarkConfig config, CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            ScenarioName = Name,
            StartTime = DateTime.UtcNow
        };

        // 稳定性测试是串行的，使用第一个连接
        var service = services[0];

        try
        {
            Logger.LogInformation("开始稳定性测试，持续时间: {Duration}秒", config.DurationSeconds);

            // 使用蓄水池采样，保持固定内存占用（最多10万个样本）
            const int MaxLatencySamples = 100_000;
            var latencies = new double[MaxLatencySamples];
            int latencyCount = 0;
            var memorySamples = new List<(DateTime Time, long Memory)>();
            long successCount = 0;
            long failCount = 0;
            int connectionFailures = 0;

            var testDuration = TimeSpan.FromSeconds(config.DurationSeconds);
            var memorySampleInterval = TimeSpan.FromSeconds(30);
            var lastMemorySample = DateTime.UtcNow;

            var stopwatch = Stopwatch.StartNew();
            var latencyStopwatch = new Stopwatch();

            // 初始内存采样
            memorySamples.Add((DateTime.UtcNow, GC.GetTotalMemory(false)));

            while (stopwatch.Elapsed < testDuration && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var request = EchoRequest.Create(GenerateTestString(config.MessageSize));
                    latencyStopwatch.Restart();
                    var response = await service.EchoAsync(request);
                    latencyStopwatch.Stop();

                    if (response.Success)
                    {
                        // 蓄水池采样：保持固定大小的样本集合
                        var totalOps = successCount + 1;
                        if (latencyCount < MaxLatencySamples)
                        {
                            latencies[latencyCount++] = latencyStopwatch.Elapsed.TotalMilliseconds;
                        }
                        else
                        {
                            // 以概率 MaxLatencySamples/totalOps 替换随机位置
                            var replaceIndex = Random.Shared.NextInt64(totalOps);
                            if (replaceIndex < MaxLatencySamples)
                            {
                                latencies[replaceIndex] = latencyStopwatch.Elapsed.TotalMilliseconds;
                            }
                        }
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    failCount++;
                    connectionFailures++;
                }

                // 内存采样
                if (DateTime.UtcNow - lastMemorySample >= memorySampleInterval)
                {
                    memorySamples.Add((DateTime.UtcNow, GC.GetTotalMemory(false)));
                    lastMemorySample = DateTime.UtcNow;

                    Logger.LogInformation("稳定性测试进度: {Elapsed:F1}/{Total:F1}秒, 请求: {Total}, 成功: {Success}, 失败: {Failed}",
                        stopwatch.Elapsed.TotalSeconds, testDuration.TotalSeconds,
                        successCount + failCount, successCount, failCount);
                }
            }

            stopwatch.Stop();

            // 最后一次内存采样
            memorySamples.Add((DateTime.UtcNow, GC.GetTotalMemory(false)));

            // 填充延迟指标（使用实际采集的样本数）
            PopulateLatencyMetrics(result.Latency, latencies.AsSpan(0, latencyCount).ToArray());

            // 填充吞吐量指标
            result.Throughput.TotalOperations = successCount + failCount;
            result.Throughput.SuccessfulOperations = successCount;
            result.Throughput.FailedOperations = failCount;

            var totalSeconds = stopwatch.Elapsed.TotalSeconds;
            if (totalSeconds > 0)
            {
                result.Throughput.OperationsPerSecond = successCount / totalSeconds;
            }

            // 填充资源指标
            if (memorySamples.Count > 0)
            {
                result.Resources.AverageMemoryUsageBytes = (long)memorySamples.Average(s => s.Memory);
                result.Resources.PeakMemoryUsageBytes = memorySamples.Max(s => s.Memory);
                result.Resources.GcGen0Collections = GC.CollectionCount(0);
                result.Resources.GcGen1Collections = GC.CollectionCount(1);
                result.Resources.GcGen2Collections = GC.CollectionCount(2);
            }

            // 分析内存泄漏
            var (leakDetected, growthRate) = AnalyzeMemoryLeak(memorySamples);
            result.Stability.MemoryLeakDetected = leakDetected;
            result.Stability.MemoryGrowthRate = growthRate;
            result.Stability.ConnectionFailures = connectionFailures;
            result.Stability.MemorySampleCount = memorySamples.Count;

            result.EndTime = DateTime.UtcNow;
            result.IsSuccessful = !leakDetected && failCount < (successCount + failCount) * 0.01;

            Logger.LogInformation("稳定性测试完成。总请求: {Total}, 成功: {Success}, 失败: {Failed}, 内存泄漏: {Leak}",
                successCount + failCount, successCount, failCount, leakDetected);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "稳定性测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private static (bool LeakDetected, double GrowthRate) AnalyzeMemoryLeak(List<(DateTime Time, long Memory)> samples)
    {
        if (samples.Count < 3)
            return (false, 0);

        // 简单线性回归
        var n = samples.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        for (int i = 0; i < n; i++)
        {
            double x = i;
            double y = samples[i].Memory;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

        // 如果斜率大于 1MB/sample，认为存在内存泄漏
        var memoryLeakThreshold = 1024 * 1024;
        return (slope > memoryLeakThreshold, slope);
    }
}
