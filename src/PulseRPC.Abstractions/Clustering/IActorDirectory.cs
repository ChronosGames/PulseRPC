using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Clustering;

/// <summary>
/// 一个 keyed Actor 实例的放置信息（属主节点 + 租约）。
/// </summary>
/// <remarks>
/// 用于 L2 一致性级别（目录 + 租约）保证同一 <c>(Hub, Key)</c> 全集群单一激活（single-activation）。
/// </remarks>
public readonly struct ActorPlacement : IEquatable<ActorPlacement>
{
    /// <summary>当前属主节点标识。</summary>
    public string NodeId { get; }

    /// <summary>租约标识（用于续租 / 释放的乐观校验）。</summary>
    public string LeaseId { get; }

    /// <summary>租约到期的 UTC 时间戳（<see cref="DateTime.Ticks"/>）；到期未续租视为释放。</summary>
    public long ExpiresAtUtcTicks { get; }

    /// <summary>创建放置信息。</summary>
    public ActorPlacement(string nodeId, string leaseId, long expiresAtUtcTicks)
    {
        NodeId = nodeId ?? string.Empty;
        LeaseId = leaseId ?? string.Empty;
        ExpiresAtUtcTicks = expiresAtUtcTicks;
    }

    /// <summary>租约是否相对给定 UTC 时间仍然有效。</summary>
    public bool IsValidAt(DateTime utcNow) => ExpiresAtUtcTicks > utcNow.Ticks;

    /// <inheritdoc/>
    public bool Equals(ActorPlacement other)
        => string.Equals(NodeId, other.NodeId, StringComparison.Ordinal)
           && string.Equals(LeaseId, other.LeaseId, StringComparison.Ordinal)
           && ExpiresAtUtcTicks == other.ExpiresAtUtcTicks;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ActorPlacement other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            NodeId is null ? 0 : StringComparer.Ordinal.GetHashCode(NodeId),
            LeaseId is null ? 0 : StringComparer.Ordinal.GetHashCode(LeaseId),
            ExpiresAtUtcTicks);
}

/// <summary>
/// Actor 目录 —— 解析 <c>(Hub, Key) → 属主节点</c>，并通过租约保证单一激活（L2）。
/// </summary>
/// <remarks>
/// <para>
/// 首版实现 = 一致性哈希给出候选属主 + 租约目录保证同一实例键全集群唯一属主。可插拔以便将来增强到 L3
/// （迁移 + 在途消息接管）。单节点默认实现始终把属主解析为本地节点。
/// </para>
/// </remarks>
public interface IActorDirectory
{
    /// <summary>
    /// 查询某实例的当前放置；未激活或租约失效返回 <c>null</c>。
    /// </summary>
    ValueTask<ActorPlacement?> ResolveAsync(string hub, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试在候选节点上激活某实例并取得租约（CAS 语义）：若已被其它节点持有有效租约，返回既有放置。
    /// </summary>
    ValueTask<ActorPlacement> ActivateAsync(string hub, string key, string candidateNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 续租；成功返回 <c>true</c>，租约已被抢占/失效返回 <c>false</c>。
    /// </summary>
    ValueTask<bool> RenewAsync(string hub, string key, string nodeId, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 主动释放某实例的租约（正常停用时调用）。
    /// </summary>
    ValueTask ReleaseAsync(string hub, string key, string nodeId, string leaseId, CancellationToken cancellationToken = default);
}
