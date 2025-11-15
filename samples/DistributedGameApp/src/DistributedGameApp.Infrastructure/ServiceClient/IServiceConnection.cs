using DistributedGameApp.Infrastructure.Consul;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 服务连接接口
/// </summary>
public interface IServiceConnection : IRemoteInvoker
{
    /// <summary>
    /// 服务信息
    /// </summary>
    ServiceRegistration ServiceInfo { get; }

    /// <summary>
    /// 请求计数
    /// </summary>
    long RequestCount { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }
}
