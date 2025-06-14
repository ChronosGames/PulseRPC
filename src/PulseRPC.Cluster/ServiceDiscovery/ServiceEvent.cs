namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务事件基类
/// </summary>
public abstract class ServiceEvent
{
    /// <summary>
    /// 事件ID
    /// </summary>
    public string EventId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// 服务端点
    /// </summary>
    public ServiceEndpoint? Endpoint { get; init; }

    /// <summary>
    /// 事件源
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 事件元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

