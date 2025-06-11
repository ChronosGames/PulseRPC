using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Abstractions.Events;

namespace PulseServiceDiscovery.Abstractions;

/// <summary>
/// 健康检查器接口
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// 健康状态变化事件
    /// </summary>
    event Func<ServiceHealthChangedEvent, Task>? HealthChanged;

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
    Task<IReadOnlyDictionary<ServiceEndpoint, HealthStatus>> CheckHealthAsync(IReadOnlyList<ServiceEndpoint> endpoints, CancellationToken cancellationToken = default)
    {
        return CheckHealthBatchDefaultAsync(endpoints, cancellationToken);
    }

    /// <summary>
    /// 启动健康检查监控
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="interval">检查间隔</param>
    /// <param name="onHealthChanged">健康状态变化回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>监控任务</returns>
    Task StartMonitoringAsync(ServiceEndpoint endpoint, TimeSpan interval, Action<ServiceEndpoint, HealthStatus> onHealthChanged, CancellationToken cancellationToken = default)
    {
        return StartMonitoringDefaultAsync(endpoint, interval, onHealthChanged, cancellationToken);
    }

    /// <summary>
    /// 停止健康检查监控
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <returns>停止任务</returns>
    Task StopMonitoringAsync(ServiceEndpoint endpoint)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 默认批量检查实现
    /// </summary>
    private async Task<IReadOnlyDictionary<ServiceEndpoint, HealthStatus>> CheckHealthBatchDefaultAsync(IReadOnlyList<ServiceEndpoint> endpoints, CancellationToken cancellationToken)
    {
        var results = new Dictionary<ServiceEndpoint, HealthStatus>();
        
        foreach (var endpoint in endpoints)
        {
            try
            {
                var health = await CheckHealthAsync(endpoint, cancellationToken);
                results[endpoint] = health;
            }
            catch
            {
                results[endpoint] = HealthStatus.Unhealthy;
            }
        }
        
        return results;
    }

    /// <summary>
    /// 默认监控实现
    /// </summary>
    private Task StartMonitoringDefaultAsync(ServiceEndpoint endpoint, TimeSpan interval, Action<ServiceEndpoint, HealthStatus> onHealthChanged, CancellationToken cancellationToken)
    {
        // 默认实现：立即检查一次
        _ = Task.Run(async () =>
        {
            try
            {
                var health = await CheckHealthAsync(endpoint, cancellationToken);
                onHealthChanged?.Invoke(endpoint, health);
                
                // 注意：在接口默认实现中不能直接触发事件，这应该由具体实现类处理
                // 事件触发应该在具体的实现类中完成
            }
            catch
            {
                onHealthChanged?.Invoke(endpoint, HealthStatus.Unhealthy);
            }
        }, cancellationToken);
        
        return Task.CompletedTask;
    }
}
