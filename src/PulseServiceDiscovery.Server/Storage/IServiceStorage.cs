using PulseServiceDiscovery.Abstractions.Models;

namespace PulseServiceDiscovery.Server.Storage;

/// <summary>
/// 服务存储接口
/// </summary>
public interface IServiceStorage
{
    /// <summary>
    /// 存储服务注册信息
    /// </summary>
    /// <param name="registration">服务注册信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StoreServiceAsync(ServiceRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务注册信息
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务注册信息，如果不存在则返回null</returns>
    Task<ServiceRegistration?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据服务名获取所有服务注册信息
    /// </summary>
    /// <param name="serviceName">服务名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务注册信息列表</returns>
    Task<IReadOnlyList<ServiceRegistration>> GetServicesByNameAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有服务注册信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有服务注册信息列表</returns>
    Task<IReadOnlyList<ServiceRegistration>> GetAllServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除服务注册信息
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveServiceAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果存在返回true，否则返回false</returns>
    Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理所有存储的服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取存储统计信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>统计信息</returns>
    Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
