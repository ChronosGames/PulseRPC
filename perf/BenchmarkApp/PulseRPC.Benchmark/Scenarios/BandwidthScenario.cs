using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;
using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// 带宽测试场景（支持上行和下行）
/// </summary>
public class BandwidthScenario : ScenarioBase
{
    private readonly bool _isUpload;

    public BandwidthScenario(ILogger<BandwidthScenario> logger, bool isUpload) : base(logger)
    {
        _isUpload = isUpload;
    }

    public override string Name => _isUpload ? "upload" : "download";
    public override string Description => _isUpload
        ? "测试客户端到服务端的数据传输带宽"
        : "测试服务端到客户端的数据传输带宽";

    public override async Task<BenchmarkResult> RunAsync(IReadOnlyList<IBenchmarkHub> services, BenchmarkConfig config, CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            ScenarioName = Name,
            StartTime = DateTime.UtcNow
        };

        try
        {
            var connectionCount = services.Count;
            Logger.LogInformation("开始{Direction}带宽测试，迭代次数: {Iterations}, 数据包大小: {Size}B, 独立连接数: {Connections}",
                _isUpload ? "上行" : "下行", config.Iterations, config.MessageSize, connectionCount);

            var latencies = new List<double>();
            var latencyLock = new object();
            long successCount = 0;
            long failCount = 0;
            long totalBytes = 0;

            // 预热（每个连接都预热）
            Logger.LogInformation("预热中（{Count}个连接）...", connectionCount);
            var warmupCount = Math.Min(config.WarmupIterations, 50) / connectionCount;
            var warmupTasks = services.Select(async service =>
            {
                for (int i = 0; i < warmupCount && !cancellationToken.IsCancellationRequested; i++)
                {
                    if (_isUpload)
                    {
                        var request = UploadRequest.Create(i, config.MessageSize);
                        await service.UploadAsync(request);
                    }
                    else
                    {
                        var request = DownloadRequest.Create(i, config.MessageSize);
                        await service.DownloadAsync(request);
                    }
                }
            });
            await Task.WhenAll(warmupTasks);

            // 主测试
            var totalStopwatch = Stopwatch.StartNew();
            var tasks = new List<Task>();
            var iterationsPerConnection = config.Iterations / connectionCount;

            // 每个独立连接运行一个任务
            for (int c = 0; c < connectionCount; c++)
            {
                var service = services[c];  // 每个任务使用独立的连接
                var task = Task.Run(async () =>
                {
                    var stopwatch = new Stopwatch();
                    for (int i = 0; i < iterationsPerConnection && !cancellationToken.IsCancellationRequested; i++)
                    {
                        try
                        {
                            stopwatch.Restart();
                            int bytesTransferred;

                            if (_isUpload)
                            {
                                var request = UploadRequest.Create(i, config.MessageSize);
                                var response = await service.UploadAsync(request);
                                stopwatch.Stop();

                                if (response.Success)
                                {
                                    bytesTransferred = config.MessageSize;
                                    Interlocked.Increment(ref successCount);
                                    Interlocked.Add(ref totalBytes, bytesTransferred);
                                    lock (latencyLock) latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                                }
                                else
                                {
                                    Interlocked.Increment(ref failCount);
                                }
                            }
                            else
                            {
                                var request = DownloadRequest.Create(i, config.MessageSize);
                                var response = await service.DownloadAsync(request);
                                stopwatch.Stop();

                                if (response.Success)
                                {
                                    bytesTransferred = response.Payload?.Length ?? 0;
                                    Interlocked.Increment(ref successCount);
                                    Interlocked.Add(ref totalBytes, bytesTransferred);
                                    lock (latencyLock) latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                                }
                                else
                                {
                                    Interlocked.Increment(ref failCount);
                                }
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
                }, cancellationToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            totalStopwatch.Stop();

            // 填充结果
            PopulateLatencyMetrics(result.Latency, latencies.ToArray());

            result.Throughput.TotalOperations = successCount + failCount;
            result.Throughput.SuccessfulOperations = successCount;
            result.Throughput.FailedOperations = failCount;
            result.Throughput.TotalBytesTransferred = totalBytes;

            var totalSeconds = totalStopwatch.Elapsed.TotalSeconds;
            if (totalSeconds > 0)
            {
                result.Throughput.OperationsPerSecond = successCount / totalSeconds;
                result.Throughput.AverageBandwidthBps = totalBytes / totalSeconds;
            }

            result.EndTime = DateTime.UtcNow;
            result.IsSuccessful = true;

            var bandwidthMBps = result.Throughput.AverageBandwidthBps / (1024.0 * 1024.0);
            Logger.LogInformation("{Direction}带宽测试完成，带宽: {Bandwidth:F2} MB/s, 平均延迟: {Latency:F2}ms",
                _isUpload ? "上行" : "下行", bandwidthMBps, result.Latency.AverageMs);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "带宽测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }
}
