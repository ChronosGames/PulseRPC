using DistributedGameApp.Infrastructure.ServiceClient;
using System.Text.Json.Serialization;

namespace DistributedGameApp.Infrastructure.Consul;

/// <summary>
/// Consul 配置选项
/// </summary>
public class ConsulOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "Consul";

    /// <summary>
    /// Consul 连接地址
    /// </summary>
    public string Address { get; set; } = "http://localhost:8500";

    /// <summary>
    /// 服务注册基础路径（标签前缀）
    /// </summary>
    public string ServiceBasePath { get; set; } = "pulserpc";

    /// <summary>
    /// 服务健康检查间隔（秒）
    /// </summary>
    public int HealthCheckInterval { get; set; } = 10;

    /// <summary>
    /// 服务超时时间（秒）
    /// </summary>
    public int HealthCheckTimeout { get; set; } = 3;

    /// <summary>
    /// 服务失效时间（秒）
    /// </summary>
    public int DeregisterCriticalServiceAfter { get; set; } = 30;

    /// <summary>
    /// 健康检查模式：TTL（推送）或 HTTP（拉取）
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HealthCheckMode HealthCheckMode { get; set; } = HealthCheckMode.HTTP;

    /// <summary>
    /// HTTP 健康检查端点地址（当 HealthCheckMode 为 HTTP 时使用）
    /// 格式: http://host:port/path
    /// 如果不设置，将自动从 HttpEndpoint 配置构建
    /// </summary>
    public string? HttpHealthCheckUrl { get; set; }
}

/// <summary>
/// 服务发现配置选项
/// </summary>
public class ServiceDiscoveryOptions
{
    /// <summary>
    /// 是否启用一致性哈希
    /// </summary>
    public bool EnableConsistentHash { get; set; } = true;

    /// <summary>
    /// 是否同步到 Consul（如果为 false，只在本地查询）
    /// </summary>
    public bool SyncToConsul { get; set; } = true;

    /// <summary>
    /// 优先使用内网地址（服务器间通信）
    /// </summary>
    public bool PreferInternalNetwork { get; set; } = true;
}

/// <summary>
/// 服务注册信息
/// </summary>
public class ServiceRegistration
{
    /// <summary>
    /// 服务ID（唯一标识）
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    /// 服务类型（GameServer, BattleServer, BackendServer）
    /// </summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 节点ID
    /// </summary>
    public int NodeId { get; set; }

    /// <summary>
    /// 节点名称
    /// </summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>
    /// 主机地址（保留用于向后兼容）
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP 端口（保留用于向后兼容）
    /// </summary>
    public int TcpPort { get; set; }

    /// <summary>
    /// KCP 端口（可选，保留用于向后兼容）
    /// </summary>
    public int? KcpPort { get; set; }

    /// <summary>
    /// 内网端点（服务器间通信优先使用）
    /// </summary>
    public NetworkEndpoint? InternalEndpoint { get; set; }

    /// <summary>
    /// 外网端点（客户端连接使用）
    /// </summary>
    public NetworkEndpoint? ExternalEndpoint { get; set; }

    /// <summary>
    /// 当前负载（连接数或房间数）
    /// </summary>
    public int CurrentLoad { get; set; }

    /// <summary>
    /// 最大容量
    /// </summary>
    public int MaxCapacity { get; set; }

    /// <summary>
    /// 服务状态（Online, Maintenance, Busy）
    /// </summary>
    public string Status { get; set; } = "Online";

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 额外元数据（JSON）
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// 获取优先使用的端点（内网优先）
    /// </summary>
    public NetworkEndpoint? GetPreferredEndpoint(bool preferInternal = true)
    {
        if (preferInternal && InternalEndpoint?.Enabled == true)
            return InternalEndpoint;

        if (ExternalEndpoint?.Enabled == true)
            return ExternalEndpoint;

        // 向后兼容：如果没有新端点，使用旧字段
        // 但如果明确要求内网端点，且没有内网端点时，不应回退到外网端口
        if (!preferInternal && !string.IsNullOrEmpty(Host) && TcpPort > 0)
        {
            return new NetworkEndpoint
            {
                Host = Host,
                TcpPort = TcpPort,
                KcpPort = KcpPort,
                Enabled = true
            };
        }

        return null;
    }
}
