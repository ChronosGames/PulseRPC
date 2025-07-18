using PulseRPC.Cluster;
using PulseRPC.HealthCheck;

namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// 服务注册事件
    /// </summary>
    event Func<ServiceRegisteredEvent, Task>? ServiceRegistered;

    /// <summary>
    /// 服务注销事件
    /// </summary>
    event Func<ServiceUnregisteredEvent, Task>? ServiceUnregistered;

    /// <summary>
    /// 服务健康状态变更事件
    /// </summary>
    event Func<ServiceHealthChangedEvent, Task>? ServiceHealthChanged;

    /// <summary>
    /// 注册服务
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销服务
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新服务健康状态
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="status">健康状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UpdateHealthAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已注册的服务列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定服务名称的所有服务
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<ServiceEndpoint>> GetServicesAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定服务ID的服务
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<ServiceEndpoint?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送心跳
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task HeartbeatAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理过期服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task CleanupExpiredServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取统计信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
