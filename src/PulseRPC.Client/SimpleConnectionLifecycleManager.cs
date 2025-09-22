using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client.ConnectionPool;
using PulseRPC.Messaging;

namespace PulseRPC.Client;

/// <summary>
/// 简单连接生命周期管理器实现 - Stage 1 基础版本
/// </summary>
public sealed class SimpleConnectionLifecycleManager : IConnectionLifecycleManager
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<SimpleConnectionLifecycleManager> _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    public SimpleConnectionLifecycleManager(
        IConnectionManager connectionManager,
        ILogger<SimpleConnectionLifecycleManager>? logger = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? NullLogger<SimpleConnectionLifecycleManager>.Instance;
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public async Task<IReadOnlyList<HealthCheckResult>> PerformHealthChecksAsync(CancellationToken cancellationToken = default)
    {
        var connections = _connectionManager.GetAllConnections();
        var results = new List<HealthCheckResult>();

        _logger.LogDebug("开始执行健康检查，连接数: {ConnectionCount}", connections.Count);

        var healthCheckTasks = connections.Select(async connection =>
        {
            var result = new HealthCheckResult
            {
                ConnectionId = connection.Id,
                CheckedAt = DateTime.UtcNow
            };

            var startTime = DateTime.UtcNow;
            try
            {
                // 简单的健康检查 - 检查连接状态
                var health = DetermineHealthFromState(connection.State);
                result.Health = health;
                result.ResponseTime = DateTime.UtcNow - startTime;
                result.Message = $"连接状态: {connection.State}";

                _logger.LogDebug("连接 {ConnectionId} 健康检查完成: {Health}", connection.Id, health);
            }
            catch (Exception ex)
            {
                result.Health = ConnectionHealth.Unhealthy;
                result.ResponseTime = DateTime.UtcNow - startTime;
                result.Exception = ex;
                result.Message = $"健康检查异常: {ex.Message}";

                _logger.LogError(ex, "连接 {ConnectionId} 健康检查失败", connection.Id);
            }

            return result;
        });

        var healthCheckResults = await Task.WhenAll(healthCheckTasks);
        results.AddRange(healthCheckResults);

        var healthyCount = results.Count(r => r.Health == ConnectionHealth.Healthy);
        var degradedCount = results.Count(r => r.Health == ConnectionHealth.Degraded);
        var unhealthyCount = results.Count(r => r.Health == ConnectionHealth.Unhealthy);

        _logger.LogInformation("健康检查完成 - 健康: {Healthy}, 降级: {Degraded}, 不健康: {Unhealthy}",
            healthyCount, degradedCount, unhealthyCount);

        return results.AsReadOnly();
    }

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    public async Task<int> CleanupIdleConnectionsAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        var connections = _connectionManager.GetAllConnections();
        var idleConnections = new List<IClientChannel>();
        var cutoffTime = DateTime.UtcNow - idleTimeout;

        _logger.LogDebug("开始清理空闲连接，超时阈值: {IdleTimeout}", idleTimeout);

        // 查找空闲连接
        foreach (var connection in connections)
        {
            if (connection.Statistics?.LastActiveAt < cutoffTime &&
                (connection.State == ExtendedConnectionState.Idle ||
                 connection.State == ExtendedConnectionState.Connected))
            {
                idleConnections.Add(connection);
            }
        }

        if (idleConnections.Count == 0)
        {
            _logger.LogDebug("没有找到需要清理的空闲连接");
            return 0;
        }

        _logger.LogInformation("发现 {IdleConnectionCount} 个空闲连接需要清理", idleConnections.Count);

        // 批量断开空闲连接
        var cleanupTasks = idleConnections.Select(async connection =>
        {
            try
            {
                await _connectionManager.DisconnectAsync(connection.Id, cancellationToken);
                _logger.LogDebug("成功清理空闲连接: {ConnectionId}", connection.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理空闲连接失败: {ConnectionId}", connection.Id);
                return false;
            }
        });

        var cleanupResults = await Task.WhenAll(cleanupTasks);
        var cleanupCount = cleanupResults.Count(success => success);

        _logger.LogInformation("空闲连接清理完成，成功清理: {CleanupCount}/{TotalIdle}",
            cleanupCount, idleConnections.Count);

        return cleanupCount;
    }

    /// <summary>
    /// 执行连接维护
    /// </summary>
    public async Task<ConnectionMaintenanceResult> PerformMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ConnectionMaintenanceResult
        {
            StartTime = startTime
        };

        try
        {
            _logger.LogDebug("开始执行连接维护");

            // 执行健康检查
            var healthResults = await PerformHealthChecksAsync(cancellationToken);
            result.HealthCheckResults = healthResults;

            // 清理空闲连接（30分钟超时）
            var cleanupCount = await CleanupIdleConnectionsAsync(TimeSpan.FromMinutes(30), cancellationToken);
            result.IdleConnectionsCleanedUp = cleanupCount;

            // 统计连接状态
            var connections = _connectionManager.GetAllConnections();
            result.TotalConnections = connections.Count;
            result.HealthyConnections = healthResults.Count(r => r.Health == ConnectionHealth.Healthy);
            result.DegradedConnections = healthResults.Count(r => r.Health == ConnectionHealth.Degraded);
            result.UnhealthyConnections = healthResults.Count(r => r.Health == ConnectionHealth.Unhealthy);

            result.Success = true;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - startTime;

            _logger.LogInformation("连接维护完成 - 耗时: {Duration}, 总连接: {Total}, 健康: {Healthy}, 清理: {Cleanup}",
                result.Duration, result.TotalConnections, result.HealthyConnections, cleanupCount);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - startTime;

            _logger.LogError(ex, "连接维护失败");
        }

        return result;
    }

    /// <summary>
    /// 根据连接状态确定健康状态
    /// </summary>
    private static ConnectionHealth DetermineHealthFromState(ExtendedConnectionState state)
    {
        return state switch
        {
            ExtendedConnectionState.Connected => ConnectionHealth.Healthy,
            ExtendedConnectionState.Active => ConnectionHealth.Healthy,
            ExtendedConnectionState.Idle => ConnectionHealth.Healthy,
            ExtendedConnectionState.Connecting => ConnectionHealth.Degraded,
            ExtendedConnectionState.Reconnecting => ConnectionHealth.Degraded,
            ExtendedConnectionState.Disconnecting => ConnectionHealth.Degraded,
            ExtendedConnectionState.Failed => ConnectionHealth.Unhealthy,
            ExtendedConnectionState.Disconnected => ConnectionHealth.Unhealthy,
            ExtendedConnectionState.Disposed => ConnectionHealth.Unhealthy,
            _ => ConnectionHealth.Unknown
        };
    }
}

/// <summary>
/// 连接维护结果
/// </summary>
public sealed class ConnectionMaintenanceResult
{
    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 持续时间
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 健康连接数
    /// </summary>
    public int HealthyConnections { get; set; }

    /// <summary>
    /// 降级连接数
    /// </summary>
    public int DegradedConnections { get; set; }

    /// <summary>
    /// 不健康连接数
    /// </summary>
    public int UnhealthyConnections { get; set; }

    /// <summary>
    /// 清理的空闲连接数
    /// </summary>
    public int IdleConnectionsCleanedUp { get; set; }

    /// <summary>
    /// 健康检查结果
    /// </summary>
    public IReadOnlyList<HealthCheckResult> HealthCheckResults { get; set; } = Array.Empty<HealthCheckResult>();
}
