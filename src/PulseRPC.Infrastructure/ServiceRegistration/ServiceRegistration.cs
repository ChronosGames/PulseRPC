using PulseRPC.HealthCheck;
using PulseRPC.Infrastructure;

namespace PulseRPC.ServiceRegistration;

/// <summary>
/// 服务注册信息
/// </summary>
public class ServiceRegistration
{
    /// <summary>
    /// 服务ID（唯一标识）
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 协议类型
    /// </summary>
    public string Protocol { get; set; } = "tcp";

    /// <summary>
    /// 服务版本
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 标签
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// 元数据
    /// </summary>
    public ServiceMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 健康检查配置
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; set; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// TTL（生存时间）
    /// </summary>
    public TimeSpan? Ttl { get; set; }

    /// <summary>
    /// 转换为服务端点
    /// </summary>
    /// <param name="health">健康状态</param>
    /// <returns>服务端点</returns>
    public ServiceEndpoint ToEndpoint(HealthStatus health = HealthStatus.Unknown) => new()
    {
        ServiceId = Id,
        ServiceType = ServiceType,
        Channel = new ChannelEndpoint
        {
            ChannelId = $"{Id}_channel",
            ChannelName = $"{ServiceType}_channel",
            Protocol = TransportProtocol.Tcp,
            Address = new NetworkAddress
            {
                Host = Host,
                Port = Port,
                UseTls = false
            }
        },
        Health = health,
    };

    /// <summary>
    /// 创建服务注册信息
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="id">服务ID</param>
    /// <returns>服务注册信息</returns>
    public static ServiceRegistration Create(string serviceName, string host, int port, string? id = null) => new()
    {
        Id = id ?? $"{serviceName}-{host}:{port}",
        ServiceType = serviceName,
        Host = host,
        Port = port
    };
}
