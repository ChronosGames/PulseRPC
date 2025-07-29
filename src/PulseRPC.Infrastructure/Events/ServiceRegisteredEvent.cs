using PulseRPC.ServiceDiscovery;

namespace PulseRPC.Infrastructure;

/// <summary>
/// 服务注册事件
/// </summary>
public class ServiceRegisteredEvent : ServiceEvent
{
    /// <summary>
    /// 注册是否成功
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// 错误信息（如果注册失败）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 创建成功的服务注册事件
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="source">事件源</param>
    /// <returns>服务注册事件</returns>
    public static ServiceRegisteredEvent CreateSuccess(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, string source = "") => new()
    {
        Endpoint = endpoint,
        Success = true,
        Source = source
    };

    /// <summary>
    /// 创建失败的服务注册事件
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="errorMessage">错误信息</param>
    /// <param name="source">事件源</param>
    /// <returns>服务注册事件</returns>
    public static ServiceRegisteredEvent CreateFailed(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, string errorMessage, string source = "") => new()
    {
        Endpoint = endpoint,
        Success = false,
        ErrorMessage = errorMessage,
        Source = source
    };
}
