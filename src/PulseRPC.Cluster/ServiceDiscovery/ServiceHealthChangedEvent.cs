using PulseRPC.HealthCheck;

namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务健康状态变更事件
/// </summary>
public class ServiceHealthChangedEvent
{
    /// <summary>
    /// 服务端点
    /// </summary>
    public ServiceEndpoint Endpoint { get; }

    /// <summary>
    /// 旧状态
    /// </summary>
    public HealthStatus OldStatus { get; }

    /// <summary>
    /// 新状态
    /// </summary>
    public HealthStatus NewStatus { get; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 创建服务健康状态变更事件
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="oldStatus">旧状态</param>
    /// <param name="newStatus">新状态</param>
    public ServiceHealthChangedEvent(ServiceEndpoint endpoint, HealthStatus oldStatus, HealthStatus newStatus)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Timestamp = DateTime.UtcNow;
    }
} 