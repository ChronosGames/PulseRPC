using PulseServiceDiscovery.Abstractions.Models;

namespace PulseServiceDiscovery.Abstractions;

/// <summary>
/// 健康检查器接口
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// 检查服务端点健康状态
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康状态</returns>
    Task<HealthStatus> CheckHealthAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量检查服务端点健康状态
    /// </summary>
    /// <param name="endpoints">服务端点列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康状态结果</returns>
    Task<IReadOnlyDictionary<ServiceEndpoint, HealthStatus>> CheckHealthAsync(IReadOnlyList<ServiceEndpoint> endpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动健康检查监控
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="interval">检查间隔</param>
    /// <param name="onHealthChanged">健康状态变化回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>监控任务</returns>
    Task StartMonitoringAsync(ServiceEndpoint endpoint, TimeSpan interval, Action<ServiceEndpoint, HealthStatus> onHealthChanged, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止健康检查监控
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <returns>停止任务</returns>
    Task StopMonitoringAsync(ServiceEndpoint endpoint);
}
