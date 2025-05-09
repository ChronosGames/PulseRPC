using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PulseRPC.Server;

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery : IDisposable
{
    /// <summary>
    /// 获取指定类型的所有服务节点
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>服务节点列表</returns>
    Task<IEnumerable<ServiceNode>> GetServiceNodesAsync(string serviceType);

    /// <summary>
    /// 获取指定服务的节点
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="serviceId">服务ID</param>
    /// <returns>服务节点，如果不存在则返回null</returns>
    Task<ServiceNode?> GetServiceNodeAsync(string serviceType, string serviceId);

    /// <summary>
    /// 根据区ID获取服务节点
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="zoneId">区ID</param>
    /// <returns>服务节点列表</returns>
    Task<IEnumerable<ServiceNode>> GetServiceNodesByZoneAsync(string serviceType, string zoneId);

    /// <summary>
    /// 监听服务变更
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="callback">变更回调</param>
    /// <returns>监听器ID，用于取消监听</returns>
    string WatchServiceChanges(string serviceType, Action<ServiceChangeEvent> callback);

    /// <summary>
    /// 取消服务变更监听
    /// </summary>
    /// <param name="watcherId">监听器ID</param>
    void UnwatchServiceChanges(string watcherId);
}

/// <summary>
/// 服务变更事件
/// </summary>
public class ServiceChangeEvent
{
    /// <summary>
    /// 变更类型
    /// </summary>
    public ServiceChangeType ChangeType { get; set; }

    /// <summary>
    /// 服务节点
    /// </summary>
    public required ServiceNode Node { get; set; }
}

/// <summary>
/// 服务变更类型
/// </summary>
public enum ServiceChangeType
{
    /// <summary>
    /// 服务注册
    /// </summary>
    Register,

    /// <summary>
    /// 服务注销
    /// </summary>
    Unregister,

    /// <summary>
    /// 服务更新
    /// </summary>
    Update,

    /// <summary>
    /// 服务健康状态变更
    /// </summary>
    HealthChange
}
