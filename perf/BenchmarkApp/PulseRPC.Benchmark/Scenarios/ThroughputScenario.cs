using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;
using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// 吞吐量测试场景
/// </summary>
public class ThroughputScenario : ScenarioBase
{
    public ThroughputScenario(ILogger<ThroughputScenario> logger) : base(logger) { }

    public override string Name => "throughput";
    public override string Description => "测量系统的吞吐量和处理能力";

    public override async Task<BenchmarkResult> RunAsync(IBenchmarkHub service, BenchmarkConfig config, CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            ScenarioName = Name,
            StartTime = DateTime.UtcNow
        };

        try
        {
            Logger.LogInformation("开始吞吐量测试，持续时间: {Duration}秒, 并发连接: {Connections}",
                config.DurationSeconds, config.Connections);

            var latencies = new List<double>();
            var latencyLock = new object();
            long successCount = 0;
            long failCount = 0;

            // 预热
            Logger.LogInformation("预热中...");
            for (int i = 0; i < config.WarmupIterations && !cancellationToken.IsCancellationRequested; i++)
            {
                var warmupRequest = EchoRequest.Create(GenerateTestString(config.MessageSize));
                await service.EchoAsync(warmupRequest);
            }

            // 主测试
            var testDuration = TimeSpan.FromSeconds(config.DurationSeconds);
            var tasks = new List<Task>();
            var totalStopwatch = Stopwatch.StartNew();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(testDuration);

            for (int c = 0; c < config.Connections; c++)
            {
                var task = Task.Run(async () =>
                {
                    var stopwatch = new Stopwatch();
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var request = EchoRequest.Create(GenerateTestString(config.MessageSize));
                            stopwatch.Restart();
                            var response = await service.EchoAsync(request);
                            stopwatch.Stop();

                            if (response.Success)
                            {
                                Interlocked.Increment(ref successCount);
                                lock (latencyLock)
                                {
                                    latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                                }
                            }
                            else
                            {
                                Interlocked.Increment(ref failCount);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch
                        {
                            Interlocked.Increment(ref failCount);
                        }
                    }
                }, cts.Token);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            totalStopwatch.Stop();

            // 填充结果
            PopulateLatencyMetrics(result.Latency, latencies.ToArray());

            result.Throughput.TotalOperations = successCount + failCount;
            result.Throughput.SuccessfulOperations = successCount;
            result.Throughput.FailedOperations = failCount;

            var totalSeconds = totalStopwatch.Elapsed.TotalSeconds;
            if (totalSeconds > 0)
            {
                result.Throughput.OperationsPerSecond = successCount / totalSeconds;
                result.Throughput.TotalBytesTransferred = successCount * config.MessageSize * 2; // 请求 + 响应
                result.Throughput.AverageBandwidthBps = result.Throughput.TotalBytesTransferred / totalSeconds;
            }

            result.EndTime = DateTime.UtcNow;
            result.IsSuccessful = true;

            Logger.LogInformation("吞吐量测试完成，OPS: {OPS:F2}, 成功: {Success}, 失败: {Failed}",
                result.Throughput.OperationsPerSecond, successCount, failCount);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "吞吐量测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }
}
