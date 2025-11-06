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
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP 端口
    /// </summary>
    public int TcpPort { get; set; }

    /// <summary>
    /// KCP 端口（可选）
    /// </summary>
    public int? KcpPort { get; set; }

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
}
