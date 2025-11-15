using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.Infrastructure.Health;

/// <summary>
/// PulseRPC 服务器健康检查提供者
/// 检查外部监听、内部监听、消息队列长度等关键指标
/// </summary>
public class ServerHealthCheckProvider : IHealthCheckProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServerHealthCheckProvider> _logger;

    // 健康检查阈值
    private const int MaxConnectionsWarningThreshold = 5000;
    private const int MaxConnectionsCriticalThreshold = 9000;
    private const double MaxDroppedMessageRatePercent = 5.0; // 丢弃消息率 > 5% 则不健康
    private const double MaxAverageLatencyMs = 500.0; // 平均延迟 > 500ms 则警告

    public ServerHealthCheckProvider(
        IServiceProvider serviceProvider,
        ILogger<ServerHealthCheckProvider> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var result = HealthCheckResult.Healthy();
        var overallHealthy = true;

        try
        {
            // 检查 External 服务器（如果存在）
            var externalServer = _serviceProvider.GetKeyedService<INamedPulseServer>("External");
            if (externalServer != null)
            {
                var externalHealthy = await CheckServerHealthAsync(externalServer, "External", result, cancellationToken);
                overallHealthy = overallHealthy && externalHealthy;
            }
            else
            {
                result.AddDetail("External_Status", "NotConfigured");
            }

            // 检查 Internal 服务器（如果存在）
            var internalServer = _serviceProvider.GetKeyedService<INamedPulseServer>("Internal");
            if (internalServer != null)
            {
                var internalHealthy = await CheckServerHealthAsync(internalServer, "Internal", result, cancellationToken);
                overallHealthy = overallHealthy && internalHealthy;
            }
            else
            {
                result.AddDetail("Internal_Status", "NotConfigured");
            }

            // 如果没有任何服务器配置，则不健康
            if (externalServer == null && internalServer == null)
            {
                result.IsHealthy = false;
                result.Status = "NoServerConfigured";
                _logger.LogWarning("健康检查失败: 没有配置任何 PulseRPC 服务器");
                return result;
            }

            // 设置整体健康状态
            result.IsHealthy = overallHealthy;
            result.Status = overallHealthy ? "Healthy" : "Unhealthy";

            if (!overallHealthy)
            {
                _logger.LogWarning("健康检查不通过: {Details}",
                    System.Text.Json.JsonSerializer.Serialize(result.Details));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "健康检查过程中发生异常");
            result.IsHealthy = false;
            result.Status = "HealthCheckException";
            result.AddDetail("Exception", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// 检查单个服务器的健康状态
    /// </summary>
    private async Task<bool> CheckServerHealthAsync(
        INamedPulseServer server,
        string serverName,
        HealthCheckResult result,
        CancellationToken cancellationToken)
    {
        var healthy = true;
        var prefix = $"{serverName}_";

        try
        {
            // 1. 检查服务器状态
            var state = server.State;
            var isRunning = server.IsRunning;

            result.AddDetail($"{prefix}State", state.ToString());
            result.AddDetail($"{prefix}IsRunning", isRunning);

            if (!isRunning)
            {
                result.AddDetail($"{prefix}Status", "NotRunning");
                _logger.LogWarning("{ServerName} 服务器未运行: State={State}", serverName, state);
                return false;
            }

            // 2. 检查传输层状态
            var transports = server.GetTransports();
            var transportHealthy = true;
            var transportDetails = new List<string>();

            foreach (var (name, transport) in transports)
            {
                var isListening = transport.IsListening;
                transportDetails.Add($"{name}:{(isListening ? "Listening" : "NotListening")}@{transport.Port}");

                if (!isListening)
                {
                    transportHealthy = false;
                    _logger.LogWarning("{ServerName} 传输层 {TransportName} 未监听: Port={Port}",
                        serverName, name, transport.Port);
                }
            }

            result.AddDetail($"{prefix}Transports", string.Join(", ", transportDetails));
            result.AddDetail($"{prefix}TransportHealthy", transportHealthy);

            if (!transportHealthy)
            {
                healthy = false;
            }

            // 3. 检查连接数
            var activeConnections = server.ActiveConnectionCount;
            result.AddDetail($"{prefix}ActiveConnections", activeConnections);

            if (activeConnections >= MaxConnectionsCriticalThreshold)
            {
                result.AddDetail($"{prefix}ConnectionStatus", "Critical");
                _logger.LogWarning("{ServerName} 连接数过高: {Count}/{Threshold}",
                    serverName, activeConnections, MaxConnectionsCriticalThreshold);
                healthy = false;
            }
            else if (activeConnections >= MaxConnectionsWarningThreshold)
            {
                result.AddDetail($"{prefix}ConnectionStatus", "Warning");
                _logger.LogDebug("{ServerName} 连接数警告: {Count}/{Threshold}",
                    serverName, activeConnections, MaxConnectionsWarningThreshold);
            }
            else
            {
                result.AddDetail($"{prefix}ConnectionStatus", "Normal");
            }

            // 4. 检查性能指标
            var metrics = server.GetPerformanceMetrics();
            result.AddDetail($"{prefix}TotalMessagesProcessed", metrics.TotalMessagesProcessed);
            result.AddDetail($"{prefix}TotalMessagesDropped", metrics.TotalMessagesDropped);
            result.AddDetail($"{prefix}AverageLatencyMs", Math.Round(metrics.AverageLatencyMs, 2));
            result.AddDetail($"{prefix}ThroughputMsgsPerSec", Math.Round(metrics.ThroughputMsgsPerSec, 2));

            // 检查消息丢弃率
            if (metrics.TotalMessagesProcessed > 0)
            {
                var droppedRate = (double)metrics.TotalMessagesDropped / metrics.TotalMessagesProcessed * 100.0;
                result.AddDetail($"{prefix}DroppedMessageRatePercent", Math.Round(droppedRate, 2));

                if (droppedRate > MaxDroppedMessageRatePercent)
                {
                    result.AddDetail($"{prefix}MessageStatus", "HighDropRate");
                    _logger.LogWarning("{ServerName} 消息丢弃率过高: {Rate}%", serverName, droppedRate);
                    healthy = false;
                }
                else
                {
                    result.AddDetail($"{prefix}MessageStatus", "Normal");
                }
            }

            // 检查平均延迟
            if (metrics.AverageLatencyMs > MaxAverageLatencyMs)
            {
                result.AddDetail($"{prefix}LatencyStatus", "High");
                _logger.LogDebug("{ServerName} 平均延迟较高: {Latency}ms", serverName, metrics.AverageLatencyMs);
                // 延迟高仅作为警告，不影响健康状态
            }
            else
            {
                result.AddDetail($"{prefix}LatencyStatus", "Normal");
            }

            // 5. 总体状态
            result.AddDetail($"{prefix}OverallStatus", healthy ? "Healthy" : "Unhealthy");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查 {ServerName} 服务器健康状态时发生异常", serverName);
            result.AddDetail($"{prefix}Status", "Exception");
            result.AddDetail($"{prefix}Exception", ex.Message);
            healthy = false;
        }

        return healthy;
    }
}
