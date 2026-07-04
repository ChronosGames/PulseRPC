namespace PulseRPC.Backplane.Redis;

/// <summary>
/// <see cref="RedisPulseBackplane"/> 的配置项。
/// </summary>
public sealed class RedisBackplaneOptions
{
    /// <summary>
    /// 本部署的键/频道命名前缀，用于在同一 Redis 实例上隔离不同的 PulseRPC 集群部署（如多环境共用一个 Redis）。
    /// 默认 <c>"pulserpc"</c>。
    /// </summary>
    public string KeyPrefix { get; set; } = "pulserpc";

    /// <summary>
    /// 模型 Y 成员目录条目的存活时间（Redis Hash 整体 TTL，随任意写操作刷新）。
    /// 作为"断线未清理"时的兜底自愈机制（设计文档 §9.2 "成员表的断线清理是 Y 的正确性关键"）；
    /// 默认 6 小时——业务侧仍应在连接断开/离开组时显式调用 <see cref="PulseRPC.Clustering.IPulseBackplane.RemoveMemberAsync"/>，
    /// 本 TTL 只是防止異常退出导致的成员表条目永久残留。
    /// </summary>
    public TimeSpan MemberEntryTimeToLive { get; set; } = TimeSpan.FromHours(6);
}
