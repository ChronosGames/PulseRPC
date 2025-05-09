using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册中心接口
/// </summary>
public interface IServiceRegistry : IDisposable
{
    /// <summary>
    /// 注册服务
    /// </summary>
    Task RegisterServiceAsync(ServiceRegistration registration);

    /// <summary>
    /// 注销服务
    /// </summary>
    Task UnregisterServiceAsync(string serviceType, string serviceId);

    /// <summary>
    /// 更新服务心跳
    /// </summary>
    Task<bool> UpdateHeartbeatAsync(string serviceType, string serviceId);

    /// <summary>
    /// 获取指定类型的所有服务
    /// </summary>
    Task<List<ServiceRegistration>> GetServicesAsync(string serviceType);

    /// <summary>
    /// 获取指定服务实例
    /// </summary>
    Task<ServiceRegistration?> GetServiceAsync(string serviceType, string serviceId);
}
