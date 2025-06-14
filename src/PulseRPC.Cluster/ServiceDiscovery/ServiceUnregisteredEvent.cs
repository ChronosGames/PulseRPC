namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务注销事件
/// </summary>
public class ServiceUnregisteredEvent
{
    /// <summary>
    /// 服务端点
    /// </summary>
    public ServiceEndpoint Endpoint { get; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 创建服务注销事件
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    public ServiceUnregisteredEvent(ServiceEndpoint endpoint)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        Timestamp = DateTime.UtcNow;
    }
} 