namespace PulseRPC.Server.Routing;

/// <summary>
/// 集群路由配置选项
/// </summary>
public class ClusterRoutingOptions
{
    /// <summary>
    /// Etcd服务端点
    /// </summary>
    public string[] EtcdEndpoints { get; set; } = { "http://localhost:2379" };

    /// <summary>
    /// Etcd键前缀
    /// </summary>
    public string EtcdKeyPrefix { get; set; } = "/pulserpc/cluster";

    /// <summary>
    /// 固定映射默认TTL（默认24小时）
    /// </summary>
    public TimeSpan FixedMappingTTL { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 虚拟节点数量（每个物理节点）
    /// </summary>
    public int VirtualNodesPerNode { get; set; } = 150;

    /// <summary>
    /// 过期映射清理间隔（默认10分钟）
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 是否启用固定映射功能
    /// </summary>
    public bool EnableFixedMapping { get; set; } = true;

    /// <summary>
    /// 当前节点ID
    /// </summary>
    public ushort NodeId { get; set; }

    /// <summary>
    /// 节点名称（用于标识）
    /// </summary>
    public string NodeName { get; set; } = Environment.MachineName;
}

/// <summary>
/// Service路由指标（用于监控）
/// </summary>
public class ServiceRoutingMetrics
{
    /// <summary>
    /// 总的固定映射数量
    /// </summary>
    public int TotalFixedMappings { get; set; }

    /// <summary>
    /// 1小时内过期的映射数量
    /// </summary>
    public int ExpiringIn1Hour { get; set; }

    /// <summary>
    /// 6小时内过期的映射数量
    /// </summary>
    public int ExpiringIn6Hours { get; set; }

    /// <summary>
    /// 24小时内过期的映射数量
    /// </summary>
    public int ExpiringIn24Hours { get; set; }

    /// <summary>
    /// 使用一致性哈希的路由次数
    /// </summary>
    public long ConsistentHashRouteCount { get; set; }

    /// <summary>
    /// 使用固定映射的路由次数
    /// </summary>
    public long FixedMappingRouteCount { get; set; }

    /// <summary>
    /// 当前哈希环版本
    /// </summary>
    public long HashRingVersion { get; set; }

    /// <summary>
    /// 活跃节点数量
    /// </summary>
    public int ActiveNodeCount { get; set; }
}

/// <summary>
/// Service下线原因
/// </summary>
public enum ShutdownReason
{
    /// <summary>玩家下线</summary>
    PlayerLogout,

    /// <summary>超时</summary>
    Timeout,

    /// <summary>手动下线</summary>
    ManualShutdown,

    /// <summary>节点关闭</summary>
    NodeShutdown,

    /// <summary>迁移</summary>
    Migration,

    /// <summary>错误</summary>
    Error
}
