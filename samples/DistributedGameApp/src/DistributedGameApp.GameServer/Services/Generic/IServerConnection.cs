using DistributedGameApp.Infrastructure.Consul;

namespace DistributedGameApp.GameServer.Services.Generic;

/// <summary>
/// 服务连接接口
/// </summary>
public interface IServerConnection
{
    /// <summary>
    /// 服务信息
    /// </summary>
    ServiceRegistration ServiceInfo { get; }

    /// <summary>
    /// 调用远程方法
    /// </summary>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string hubName,
        string methodName,
        TRequest? request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求计数
    /// </summary>
    long RequestCount { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }
}
