using PulseRPC.Infrastructure;

namespace PulseRPC.HealthCheck;

/// <summary>
/// 健康检查接口
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// 开始监控服务健康状态
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="interval">检查间隔</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果流</returns>
    IAsyncEnumerable<HealthCheckResult> MonitorHealthAsync(
        ServiceEndpoint endpoint,
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<HealthCheckResult> CheckHealthAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量健康检查
    /// </summary>
    /// <param name="endpoints">服务端点列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果字典</returns>
    Task<Dictionary<string, HealthCheckResult>> CheckHealthBatchAsync(
        IEnumerable<ServiceEndpoint> endpoints,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动健康检查监控
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="interval">检查间隔</param>
    /// <param name="onHealthChanged">健康状态变化回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>监控任务</returns>
    Task StartMonitoringAsync(ServiceEndpoint endpoint, TimeSpan interval,
        Action<ServiceEndpoint, HealthStatus> onHealthChanged, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止健康检查监控
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <returns>停止任务</returns>
    Task StopMonitoringAsync(ServiceEndpoint endpoint);

    /// <summary>
    /// 健康状态变化事件
    /// </summary>
    event Func<ServiceHealthChangedEvent, Task>? HealthChanged;
}
