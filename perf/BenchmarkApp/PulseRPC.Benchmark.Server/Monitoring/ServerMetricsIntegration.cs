using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Server.Configuration;
using PulseRPC.Benchmark.Server.Extensions;

namespace PulseRPC.Benchmark.Server.Monitoring;

/// <summary>
/// 服务端指标集成
/// </summary>
public class ServerMetricsIntegration
{
    private readonly ILogger<ServerMetricsIntegration> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly ServerConfiguration _config;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _monitoringTask;

    public ServerMetricsIntegration(
        ILogger<ServerMetricsIntegration> logger,
        IMetricsCollector metricsCollector,
        ServerConfiguration config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 启动指标集成
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("启动服务端指标集成...");

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = MonitorServerMetricsAsync(_cancellationTokenSource.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 停止指标集成
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("停止服务端指标集成...");

        _cancellationTokenSource?.Cancel();

        if (_monitoringTask != null)
        {
            try
            {
                await _monitoringTask;
            }
            catch (OperationCanceledException)
            {
                // 正常的取消操作
            }
        }

        _cancellationTokenSource?.Dispose();
    }

    /// <summary>
    /// 监控服务器指标
    /// </summary>
    private async Task MonitorServerMetricsAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CollectServerMetricsAsync(cancellationToken);
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "收集服务器指标时发生错误");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    /// <summary>
    /// 收集服务器指标
    /// </summary>
    private async Task CollectServerMetricsAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var process = System.Diagnostics.Process.GetCurrentProcess();

        // CPU和内存指标
        var cpuUsage = GetCpuUsage();
        var memoryUsage = process.WorkingSet64;
        var gcMemory = GC.GetTotalMemory(false);

        await _metricsCollector.CollectAsync("server_performance", new
        {
            Timestamp = timestamp,
            CpuUsagePercent = cpuUsage,
            MemoryUsageBytes = memoryUsage,
            GcMemoryBytes = gcMemory,
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount
        }, cancellationToken);

        // 垃圾回收指标
        for (int gen = 0; gen <= GC.MaxGeneration; gen++)
        {
            await _metricsCollector.CollectAsync("gc_collections", new
            {
                Timestamp = timestamp,
                Generation = gen,
                CollectionCount = GC.CollectionCount(gen)
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 获取CPU使用率（简化版本）
    /// </summary>
    private double GetCpuUsage()
    {
        // 这里使用简化的实现，实际项目中可能需要更精确的计算
        var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.TotalProcessorTime.TotalMilliseconds / Environment.TickCount * 100;
    }
}
