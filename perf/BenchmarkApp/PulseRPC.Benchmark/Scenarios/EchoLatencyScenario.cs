using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;
using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// Echo延迟测试场景
/// </summary>
public class EchoLatencyScenario : ScenarioBase
{
    public EchoLatencyScenario(ILogger<EchoLatencyScenario> logger) : base(logger) { }

    public override string Name => "latency";
    public override string Description => "测量单次RPC调用的往返时间(RTT)";

    public override async Task<BenchmarkResult> RunAsync(IBenchmarkHub service, BenchmarkConfig config, CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            ScenarioName = Name,
            StartTime = DateTime.UtcNow
        };

        try
        {
            Logger.LogInformation("开始Echo延迟测试，迭代次数: {Iterations}", config.Iterations);

            var latencies = new List<double>();
            var successCount = 0;
            var failCount = 0;

            // 预热
            Logger.LogInformation("预热中...");
            for (int i = 0; i < config.WarmupIterations && !cancellationToken.IsCancellationRequested; i++)
            {
                var warmupRequest = EchoRequest.Create(GenerateTestString(config.MessageSize));
                await service.EchoAsync(warmupRequest);
            }

            // 主测试
            var stopwatch = new Stopwatch();
            for (int i = 0; i < config.Iterations && !cancellationToken.IsCancellationRequested; i++)
            {
                var request = EchoRequest.Create(GenerateTestString(config.MessageSize));

                stopwatch.Restart();
                var response = await service.EchoAsync(request);
                stopwatch.Stop();

                if (response.Success)
                {
                    latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                    successCount++;
                }
                else
                {
                    failCount++;
                }

                if ((i + 1) % 1000 == 0)
                {
                    Logger.LogDebug("进度: {Current}/{Total}", i + 1, config.Iterations);
                }
            }

            // 填充结果
            PopulateLatencyMetrics(result.Latency, latencies.ToArray());

            result.Throughput.TotalOperations = successCount + failCount;
            result.Throughput.SuccessfulOperations = successCount;
            result.Throughput.FailedOperations = failCount;

            result.EndTime = DateTime.UtcNow;
            if (result.Duration.TotalSeconds > 0)
            {
                result.Throughput.OperationsPerSecond = successCount / result.Duration.TotalSeconds;
            }

            result.IsSuccessful = true;
            Logger.LogInformation("延迟测试完成，平均延迟: {AvgLatency:F2}ms", result.Latency.AverageMs);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "延迟测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }
}
