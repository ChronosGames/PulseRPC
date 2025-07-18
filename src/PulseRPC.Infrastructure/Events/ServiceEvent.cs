namespace PulseRPC.Cluster;

/// <summary>
/// 服务事件基类
/// </summary>
public abstract class ServiceEvent
{
    /// <summary>
    /// 事件时间戳
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 事件源
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 关联的服务端点
    /// </summary>
    public required ServiceEndpoint Endpoint { get; init; }
}
