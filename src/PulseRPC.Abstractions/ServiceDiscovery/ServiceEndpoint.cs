using System.Net;

namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务端点信息
/// </summary>
public class ServiceEndpoint
{
    /// <summary>
    /// 服务唯一标识
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 服务端点地址
    /// </summary>
    public IPEndPoint EndPoint { get; set; } = new(IPAddress.Loopback, 0);

    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus HealthStatus { get; set; } = HealthStatus.Unknown;

    /// <summary>
    /// 服务标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 服务权重（用于负载均衡）
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 服务版本
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public override string ToString()
    {
        return $"{ServiceName}({ServiceId}) @ {EndPoint} [{HealthStatus}]";
    }

    public override bool Equals(object? obj)
    {
        return obj is ServiceEndpoint endpoint &&
               ServiceId == endpoint.ServiceId;
    }

    public override int GetHashCode()
    {
        return ServiceId.GetHashCode();
    }
}

/// <summary>
/// 健康状态枚举
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 健康
    /// </summary>
    Healthy = 1,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy = 2,

    /// <summary>
    /// 警告
    /// </summary>
    Warning = 3,

    /// <summary>
    /// 临界状态
    /// </summary>
    Critical = 4,

    /// <summary>
    /// 正在启动
    /// </summary>
    Starting = 5,

    /// <summary>
    /// 正在停止
    /// </summary>
    Stopping = 6
}
