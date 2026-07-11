using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Clustering;

/// <summary>
/// Actor 租约存储后端，要求提供 CAS + TTL 语义。
/// </summary>
/// <remarks>
/// 分布式实现应把本接口映射到 Redis、Etcd 或数据库等共享后端：激活必须是 compare-and-set，
/// 租约必须带 TTL，续租和释放必须同时校验 owner node 与 lease id。
/// </remarks>
public interface IActorLeaseStore
{
    /// <summary>查询当前有效租约；未激活或租约过期返回 <c>null</c>。</summary>
    ValueTask<ActorPlacement?> ResolveAsync(
        string hub,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>CAS 激活：无有效租约时写入候选 owner；已有有效租约时返回既有租约。</summary>
    ValueTask<ActorPlacement> ActivateAsync(
        string hub,
        string key,
        string candidateNodeId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>续租：仅当 node/lease 匹配当前有效租约时延长 TTL。</summary>
    ValueTask<bool> RenewAsync(
        string hub,
        string key,
        string nodeId,
        string leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>释放：仅当 node/lease 匹配当前有效租约时删除。</summary>
    ValueTask ReleaseAsync(
        string hub,
        string key,
        string nodeId,
        string leaseId,
        CancellationToken cancellationToken = default);
}
