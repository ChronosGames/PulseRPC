using Microsoft.Extensions.Logging;
using PulseRPC.Client.ConnectionPool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PulseRPC.Client.Health;

/// <summary>
/// 连接池健康检查器
/// </summary>
public sealed class ConnectionPoolHealthChecker : HealthCheckerBase<IConnectionPool>
{
    private readonly ILogger<ConnectionPoolHealthChecker> _logger;
    private readonly ConnectionPoolHealthOptions _options;

    /// <summary>
    /// 检查器名称
    /// </summary>
    public override string Name => "ConnectionPool";

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionPoolHealthChecker(
        ConnectionPoolHealthOptions? options = null,
        ILogger<ConnectionPoolHealthChecker>? logger = null)
    {
        _options = options ?? new ConnectionPoolHealthOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectionPoolHealthChecker>.Instance;
    }

    /// <summary>
    /// 检查连接池健康状态
    /// </summary>
    protected override async Task<HealthCheckResult> CheckTargetAsync(IConnectionPool target, HealthCheckContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var data = new Dictionary<string, object>();

        try
        {
            // 获取连接池统计信息
            var statistics = target.GetStatistics();
            PopulateStatisticsData(statistics, data);

            // 评估连接池健康状态
            var (status, description) = EvaluatePoolHealth(statistics, data);

            // 如果启用了连接测试，执行连接获取测试
            if (status == HealthStatus.Healthy && _options.TestConnectionAcquisition)
            {
                var acquisitionResult = await TestConnectionAcquisitionAsync(target, context);
                if (acquisitionResult.Status != HealthStatus.Healthy)
                {
                    status = acquisitionResult.Status;
                    description = acquisitionResult.Description;
                    if (acquisitionResult.Exception != null)
                    {
                        data["AcquisitionException"] = acquisitionResult.Exception.Message;
                    }
                }
                else
                {
                    data["AcquisitionTime"] = acquisitionResult.ResponseTime.TotalMilliseconds;
                }
            }

            stopwatch.Stop();
            data["CheckDuration"] = stopwatch.Elapsed.TotalMilliseconds;

            return new HealthCheckResult(status, description, stopwatch.Elapsed, null, data);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            data["CheckDuration"] = stopwatch.Elapsed.TotalMilliseconds;

            _logger.LogError(ex, "连接池健康检查异常: {PoolName}", target.Name);
            return HealthCheckResult.Unhealthy(
                $"健康检查异常: {ex.Message}",
                stopwatch.Elapsed,
                ex,
                data);
        }
    }

    /// <summary>
    /// 填充统计数据
    /// </summary>
    private void PopulateStatisticsData(ConnectionPoolStatistics statistics, Dictionary<string, object> data)
    {
        data["PoolName"] = statistics.PoolName;
        data["PoolState"] = statistics.State.ToString();
        data["TotalConnections"] = statistics.TotalConnections;
        data["ActiveConnections"] = statistics.ActiveConnections;
        data["IdleConnections"] = statistics.IdleConnections;
        data["LeasedConnections"] = statistics.LeasedConnections;
        data["MinPoolSize"] = statistics.MinPoolSize;
        data["MaxPoolSize"] = statistics.MaxPoolSize;
        data["TotalAcquisitions"] = statistics.TotalAcquisitions;
        data["SuccessfulAcquisitions"] = statistics.SuccessfulAcquisitions;
        data["FailedAcquisitions"] = statistics.FailedAcquisitions;
        data["TotalCreations"] = statistics.TotalCreations;
        data["SuccessfulCreations"] = statistics.SuccessfulCreations;
        data["FailedCreations"] = statistics.FailedCreations;
        data["ConnectionsCreatedTotal"] = statistics.ConnectionsCreatedTotal;
        data["ConnectionsDestroyedTotal"] = statistics.ConnectionsDestroyedTotal;
        data["CreatedAt"] = statistics.CreatedAt;
        data["LastMaintenanceAt"] = statistics.LastMaintenanceAt ?? DateTime.Now;

        if (statistics.AverageAcquisitionTime.HasValue)
        {
            data["AverageAcquisitionTime"] = statistics.AverageAcquisitionTime.Value.TotalMilliseconds;
        }

        if (statistics.AverageConnectionLifetime.HasValue)
        {
            data["AverageConnectionLifetime"] = statistics.AverageConnectionLifetime.Value.TotalSeconds;
        }

        // 计算派生指标
        var acquisitionSuccessRate = statistics.TotalAcquisitions > 0
            ? (double)statistics.SuccessfulAcquisitions / statistics.TotalAcquisitions * 100
            : 100;
        data["AcquisitionSuccessRate"] = acquisitionSuccessRate;

        var creationSuccessRate = statistics.TotalCreations > 0
            ? (double)statistics.SuccessfulCreations / statistics.TotalCreations * 100
            : 100;
        data["CreationSuccessRate"] = creationSuccessRate;

        var utilizationRate = statistics.MaxPoolSize > 0
            ? (double)statistics.ActiveConnections / statistics.MaxPoolSize * 100
            : 0;
        data["UtilizationRate"] = utilizationRate;
    }

    /// <summary>
    /// 评估连接池健康状态
    /// </summary>
    private (HealthStatus Status, string Description) EvaluatePoolHealth(ConnectionPoolStatistics statistics, Dictionary<string, object> data)
    {
        // 检查连接池状态
        switch (statistics.State)
        {
            case ConnectionPoolState.Initializing:
                return (HealthStatus.Degraded, "连接池初始化中");

            case ConnectionPoolState.Disposed:
                return (HealthStatus.Unhealthy, "连接池已释放");

            case ConnectionPoolState.Running:
                break; // 继续检查

            default:
                return (HealthStatus.Unknown, $"未知连接池状态: {statistics.State}");
        }

        var issues = new List<string>();

        // 检查连接获取成功率
        var acquisitionSuccessRate = (double)data["AcquisitionSuccessRate"];
        if (acquisitionSuccessRate < _options.MinAcquisitionSuccessRate)
        {
            issues.Add($"连接获取成功率过低: {acquisitionSuccessRate:F1}%");
        }

        // 检查连接创建成功率
        var creationSuccessRate = (double)data["CreationSuccessRate"];
        if (creationSuccessRate < _options.MinCreationSuccessRate)
        {
            issues.Add($"连接创建成功率过低: {creationSuccessRate:F1}%");
        }

        // 检查连接池利用率
        var utilizationRate = (double)data["UtilizationRate"];
        if (utilizationRate > _options.MaxUtilizationRate)
        {
            issues.Add($"连接池利用率过高: {utilizationRate:F1}%");
        }

        // 检查平均获取时间
        if (statistics.AverageAcquisitionTime.HasValue &&
            statistics.AverageAcquisitionTime.Value > _options.MaxAverageAcquisitionTime)
        {
            issues.Add($"平均获取时间过长: {statistics.AverageAcquisitionTime.Value.TotalMilliseconds:F2}ms");
        }

        // 检查空闲连接数
        if (statistics.IdleConnections == 0 && statistics.ActiveConnections == statistics.MaxPoolSize)
        {
            issues.Add("连接池已满，无空闲连接");
        }

        // 检查最近维护时间
        if (statistics.LastMaintenanceAt.HasValue)
        {
            var timeSinceLastMaintenance = DateTime.UtcNow - statistics.LastMaintenanceAt.Value;
            if (timeSinceLastMaintenance > _options.MaxTimeSinceLastMaintenance)
            {
                issues.Add($"维护间隔过长: {timeSinceLastMaintenance:hh\\:mm\\:ss}");
            }
        }

        if (issues.Count == 0)
        {
            return (HealthStatus.Healthy, $"连接池健康 (利用率: {utilizationRate:F1}%, 获取成功率: {acquisitionSuccessRate:F1}%)");
        }

        // 根据问题严重程度确定状态
        var hasHighSeverityIssues = acquisitionSuccessRate < _options.MinAcquisitionSuccessRate * 0.5 ||
                                   creationSuccessRate < _options.MinCreationSuccessRate * 0.5 ||
                                   utilizationRate > _options.MaxUtilizationRate * 1.2;

        var status = hasHighSeverityIssues ? HealthStatus.Unhealthy : HealthStatus.Degraded;
        var description = string.Join("; ", issues);

        return (status, description);
    }

    /// <summary>
    /// 测试连接获取
    /// </summary>
    private async Task<HealthCheckResult> TestConnectionAcquisitionAsync(IConnectionPool pool, HealthCheckContext context)
    {
        if (!_options.TestConnectionAcquisition)
        {
            return HealthCheckResult.Healthy("连接获取测试已禁用", TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        IConnectionLease? lease = null;

        try
        {
            // 尝试获取一个连接
            lease = await pool.AcquireAsync(context.CancellationToken);
            stopwatch.Stop();

            if (lease == null)
            {
                return HealthCheckResult.Unhealthy(
                    "无法获取连接",
                    stopwatch.Elapsed);
            }

            if (stopwatch.Elapsed > _options.AcquisitionTestTimeout)
            {
                return HealthCheckResult.Degraded(
                    $"连接获取时间过长: {stopwatch.Elapsed.TotalMilliseconds:F2}ms",
                    stopwatch.Elapsed);
            }

            return HealthCheckResult.Healthy(
                $"连接获取测试成功，耗时: {stopwatch.Elapsed.TotalMilliseconds:F2}ms",
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "连接获取测试失败: {PoolName}", pool.Name);

            return HealthCheckResult.Unhealthy(
                $"连接获取测试失败: {ex.Message}",
                stopwatch.Elapsed,
                ex);
        }
        finally
        {
            // 确保释放租约
            if (lease != null)
            {
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "释放连接租约失败: {PoolName}", pool.Name);
                }
            }
        }
    }
}

/// <summary>
/// 连接池健康检查选项
/// </summary>
public sealed class ConnectionPoolHealthOptions
{
    /// <summary>
    /// 是否测试连接获取
    /// </summary>
    public bool TestConnectionAcquisition { get; set; } = true;

    /// <summary>
    /// 连接获取测试超时时间
    /// </summary>
    public TimeSpan AcquisitionTestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 最小连接获取成功率（百分比）
    /// </summary>
    public double MinAcquisitionSuccessRate { get; set; } = 95.0;

    /// <summary>
    /// 最小连接创建成功率（百分比）
    /// </summary>
    public double MinCreationSuccessRate { get; set; } = 90.0;

    /// <summary>
    /// 最大连接池利用率（百分比）
    /// </summary>
    public double MaxUtilizationRate { get; set; } = 90.0;

    /// <summary>
    /// 最大平均获取时间
    /// </summary>
    public TimeSpan MaxAverageAcquisitionTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大维护间隔时间
    /// </summary>
    public TimeSpan MaxTimeSinceLastMaintenance { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(2);
}
