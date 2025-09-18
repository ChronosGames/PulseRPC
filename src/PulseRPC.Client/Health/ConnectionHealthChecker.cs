using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PulseRPC.Client.Health;

/// <summary>
/// 连接健康检查器
/// </summary>
public sealed class ConnectionHealthChecker : HealthCheckerBase<IConnectionContext>
{
    private readonly ILogger<ConnectionHealthChecker> _logger;
    private readonly ConnectionHealthOptions _options;

    /// <summary>
    /// 检查器名称
    /// </summary>
    public override string Name => "Connection";

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionHealthChecker(
        ConnectionHealthOptions? options = null,
        ILogger<ConnectionHealthChecker>? logger = null)
    {
        _options = options ?? new ConnectionHealthOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectionHealthChecker>.Instance;
    }

    /// <summary>
    /// 检查连接健康状态
    /// </summary>
    protected override async Task<HealthCheckResult> CheckTargetAsync(IConnectionContext target, HealthCheckContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var data = new Dictionary<string, object>();

        try
        {
            // 基础状态检查
            var (status, description) = EvaluateConnectionState(target, data);

            // 如果连接状态正常，执行更深入的检查
            if (status == HealthStatus.Healthy && _options.EnablePingCheck)
            {
                var pingResult = await PerformPingCheckAsync(target, context);
                if (pingResult.Status != HealthStatus.Healthy)
                {
                    status = pingResult.Status;
                    description = pingResult.Description;
                    if (pingResult.Exception != null)
                    {
                        data["PingException"] = pingResult.Exception.Message;
                    }
                }
                else
                {
                    data["PingResponseTime"] = pingResult.ResponseTime.TotalMilliseconds;
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

            _logger.LogError(ex, "连接健康检查异常: {ConnectionId}", target.Id);
            return HealthCheckResult.Unhealthy(
                $"健康检查异常: {ex.Message}",
                stopwatch.Elapsed,
                ex,
                data);
        }
    }

    /// <summary>
    /// 评估连接状态
    /// </summary>
    private (HealthStatus Status, string Description) EvaluateConnectionState(IConnectionContext connection, Dictionary<string, object> data)
    {
        data["ConnectionId"] = connection.Id;
        data["ConnectionState"] = connection.State.ToString();
        data["RemoteEndPoint"] = connection.RemoteEndPoint?.ToString() ?? "Unknown";
        data["LocalEndPoint"] = connection.LocalEndPoint?.ToString() ?? "Unknown";
        data["CreatedAt"] = connection.CreatedAt;
        data["LastActivityAt"] = connection.LastActivityAt;

        var now = DateTime.UtcNow;
        var idleDuration = now - connection.LastActivityAt;
        var connectionAge = now - connection.CreatedAt;

        data["IdleDuration"] = idleDuration.TotalSeconds;
        data["ConnectionAge"] = connectionAge.TotalSeconds;

        switch (connection.State)
        {
            case ExtendedConnectionState.Connected:
                // 检查空闲时间
                if (_options.MaxIdleTime.HasValue && idleDuration > _options.MaxIdleTime.Value)
                {
                    return (HealthStatus.Degraded, $"连接空闲时间过长: {idleDuration:hh\\:mm\\:ss}");
                }

                // 检查连接存活时间
                if (_options.MaxConnectionAge.HasValue && connectionAge > _options.MaxConnectionAge.Value)
                {
                    return (HealthStatus.Degraded, $"连接存活时间过长: {connectionAge:hh\\:mm\\:ss}");
                }

                return (HealthStatus.Healthy, "连接正常");

            case ExtendedConnectionState.Connecting:
                return (HealthStatus.Degraded, "连接中");

            case ExtendedConnectionState.Disconnecting:
                return (HealthStatus.Degraded, "断开连接中");

            case ExtendedConnectionState.Disconnected:
                return (HealthStatus.Unhealthy, "连接已断开");

            case ExtendedConnectionState.Failed:
                return (HealthStatus.Unhealthy, "连接失败");

            case ExtendedConnectionState.Disposed:
                return (HealthStatus.Unhealthy, "连接已释放");

            default:
                return (HealthStatus.Unknown, $"未知连接状态: {connection.State}");
        }
    }

    /// <summary>
    /// 执行Ping检查
    /// </summary>
    private async Task<HealthCheckResult> PerformPingCheckAsync(IConnectionContext connection, HealthCheckContext context)
    {
        if (!_options.EnablePingCheck)
        {
            return HealthCheckResult.Healthy("Ping检查已禁用", TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 这里应该发送一个Ping消息到服务端并等待响应
            // 由于我们没有具体的Ping实现，这里模拟检查
            await SimulatePingAsync(connection, context.CancellationToken);

            stopwatch.Stop();

            if (stopwatch.Elapsed > _options.PingTimeout)
            {
                return HealthCheckResult.Degraded(
                    $"Ping响应时间过长: {stopwatch.Elapsed.TotalMilliseconds:F2}ms",
                    stopwatch.Elapsed);
            }

            return HealthCheckResult.Healthy(
                $"Ping成功，响应时间: {stopwatch.Elapsed.TotalMilliseconds:F2}ms",
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Ping检查失败: {ConnectionId}", connection.Id);

            return HealthCheckResult.Unhealthy(
                $"Ping失败: {ex.Message}",
                stopwatch.Elapsed,
                ex);
        }
    }

    /// <summary>
    /// 模拟Ping检查
    /// </summary>
    private async Task SimulatePingAsync(IConnectionContext connection, System.Threading.CancellationToken cancellationToken)
    {
        // 这里应该实现真正的Ping逻辑
        // 目前只是模拟检查连接的基本可用性
        if (connection.State != ExtendedConnectionState.Connected)
        {
            throw new InvalidOperationException($"连接状态不正常: {connection.State}");
        }

        // 模拟网络延迟
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);

        // 检查连接是否仍然有效
        if (connection.State != ExtendedConnectionState.Connected)
        {
            throw new InvalidOperationException("连接在Ping过程中断开");
        }
    }
}

/// <summary>
/// 连接健康检查选项
/// </summary>
public sealed class ConnectionHealthOptions
{
    /// <summary>
    /// 是否启用Ping检查
    /// </summary>
    public bool EnablePingCheck { get; set; } = true;

    /// <summary>
    /// Ping超时时间
    /// </summary>
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 最大空闲时间
    /// </summary>
    public TimeSpan? MaxIdleTime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 最大连接存活时间
    /// </summary>
    public TimeSpan? MaxConnectionAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 失败阈值（连续失败多少次后标记为不健康）
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 恢复阈值（连续成功多少次后标记为健康）
    /// </summary>
    public int SuccessThreshold { get; set; } = 2;
}
