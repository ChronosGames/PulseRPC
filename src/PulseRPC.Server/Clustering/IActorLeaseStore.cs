using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// Actor 租约存储后端，要求提供 CAS + TTL 语义。
/// </summary>
/// <remarks>
/// Phase D 的分布式实现应把本接口映射到 Redis/Etcd/数据库等后端：
/// 激活必须是 compare-and-set，租约必须带 TTL，续租/释放必须校验 owner node 与 lease id，
/// 以避免节点失效后出现长期双 owner。
/// </remarks>
public interface IActorLeaseStore
{
    /// <summary>查询当前有效租约；未激活或租约过期返回 <c>null</c>。</summary>
    ValueTask<ActorPlacement?> ResolveAsync(string hub, string key, CancellationToken cancellationToken = default);

    /// <summary>CAS 激活：无有效租约时写入候选 owner；已有有效租约时返回既有租约。</summary>
    ValueTask<ActorPlacement> ActivateAsync(
        string hub,
        string key,
        string candidateNodeId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>续租：仅当 node/lease 匹配当前租约时延长 TTL。</summary>
    ValueTask<bool> RenewAsync(
        string hub,
        string key,
        string nodeId,
        string leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>释放：仅当 node/lease 匹配当前租约时删除。</summary>
    ValueTask ReleaseAsync(
        string hub,
        string key,
        string nodeId,
        string leaseId,
        CancellationToken cancellationToken = default);
}
