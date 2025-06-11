using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Abstractions.Events;

namespace PulseServiceDiscovery.Abstractions;

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// 发现单个服务端点
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端点，如果未找到则返回null</returns>
    Task<ServiceEndpoint?> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发现所有服务端点
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端点列表</returns>
    Task<IReadOnlyList<ServiceEndpoint>> DiscoverAllAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务是否可用
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务是否可用</returns>
    Task<bool> IsAvailableAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听服务变化事件
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务事件流</returns>
    IAsyncEnumerable<ServiceEvent> WatchAsync(string serviceName, CancellationToken cancellationToken = default);
}
