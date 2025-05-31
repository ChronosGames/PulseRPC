using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Collectors;
using PulseRPC.Benchmark.Server.Configuration;
using PulseRPC.Benchmark.Server.Extensions;

namespace PulseRPC.Benchmark.Server.Health;

/// <summary>
/// 健康检查服务
/// </summary>
public class HealthCheckService(
    ILogger<HealthCheckService> logger,
    RealTimeMetricsCollector metricsCollector,
    ServerConfiguration config)
{
    private readonly ILogger<HealthCheckService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly RealTimeMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    private readonly ServerConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _healthCheckTask;

    /// <summary>
    /// 启动健康检查
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("启动健康检查服务...");

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _healthCheckTask = PerformHealthChecksAsync(_cancellationTokenSource.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 停止健康检查
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("停止健康检查服务...");

        _cancellationTokenSource?.Cancel();

        if (_healthCheckTask != null)
        {
            try
            {
                await _healthCheckTask;
            }
            catch (OperationCanceledException)
            {
                // 正常的取消操作
            }
        }

        _cancellationTokenSource?.Dispose();
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_config.HealthCheckIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckHealthAsync(cancellationToken);
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行健康检查时发生错误");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    /// <summary>
    /// 检查健康状态
    /// </summary>
    private async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var healthStatus = "Healthy";
        var issues = new List<string>();

        // 检查内存使用
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var memoryUsageMB = process.WorkingSet64 / (1024 * 1024);
        var maxMemoryMB = 1024; // 1GB限制

        if (memoryUsageMB > maxMemoryMB)
        {
            healthStatus = "Unhealthy";
            issues.Add($"内存使用过高: {memoryUsageMB}MB > {maxMemoryMB}MB");
        }

        // 检查线程数
        var threadCount = process.Threads.Count;
        var maxThreads = 500;

        if (threadCount > maxThreads)
        {
            healthStatus = "Unhealthy";
            issues.Add($"线程数过多: {threadCount} > {maxThreads}");
        }

        // 记录健康检查结果
        await _metricsCollector.CollectAsync("health_check", new
        {
            Timestamp = timestamp,
            Status = healthStatus,
            MemoryUsageMB = memoryUsageMB,
            ThreadCount = threadCount,
            Issues = issues.Count > 0 ? string.Join(", ", issues) : null
        }, cancellationToken);

        if (healthStatus != "Healthy")
        {
            _logger.LogWarning("健康检查发现问题: {Issues}", string.Join(", ", issues));
        }
    }
}
