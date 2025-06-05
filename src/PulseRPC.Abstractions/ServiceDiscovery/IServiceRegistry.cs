namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务注册接口
/// </summary>
public interface IServiceRegistry
{
    /// <summary>
    /// 注册服务
    /// </summary>
    /// <param name="endpoint">服务端点信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册任务</returns>
    Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销服务
    /// </summary>
    /// <param name="serviceId">服务唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注销任务</returns>
    Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新服务健康状态
    /// </summary>
    /// <param name="serviceId">服务唯一标识</param>
    /// <param name="status">健康状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新任务</returns>
    Task UpdateHealthAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已注册的服务列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端点列表</returns>
    Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync(CancellationToken cancellationToken = default);
}
