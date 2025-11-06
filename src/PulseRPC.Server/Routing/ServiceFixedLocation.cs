using MemoryPack;

namespace PulseRPC.Server.Routing;

/// <summary>
/// Service固定位置记录（带过期时间）
/// 用于在节点扩缩容时临时固定Service位置，避免自动迁移
/// </summary>
[MemoryPackable]
public partial class ServiceFixedLocation
{
    /// <summary>
    /// Service标识符的哈希值（用于一致性哈希）
    /// </summary>
    public ulong ServiceIdHash { get; set; }

    /// <summary>
    /// 固定的节点ID
    /// </summary>
    public ushort NodeId { get; set; }

    /// <summary>
    /// 原始策略（用于判断是否需要固定）
    /// </summary>
    public ServicePlacementStrategy OriginalStrategy { get; set; }

    /// <summary>
    /// 固定时间
    /// </summary>
    public DateTime FixedAt { get; set; }

    /// <summary>
    /// 过期时间（默认24小时，可配置）
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 固定原因（用于调试和审计）
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    public bool IsExpired() => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// 获取剩余有效时间
    /// </summary>
    public TimeSpan RemainingTime() => ExpiresAt - DateTime.UtcNow;
}

/// <summary>
/// Service位置策略（用于路由决策）
/// </summary>
public enum ServicePlacementStrategy
{
    /// <summary>使用一致性哈希（默认）</summary>
    ConsistentHash = 0,

    /// <summary>固定到特定节点（扩缩容时使用）</summary>
    FixedNode = 1,

    /// <summary>全局单例（固定到主节点）</summary>
    GlobalSingleton = 2,

    /// <summary>手动指定节点</summary>
    ManualAssignment = 3
}
