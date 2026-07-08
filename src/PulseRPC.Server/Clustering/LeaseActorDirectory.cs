using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="LeaseActorDirectory"/> 的配置项。
/// </summary>
public sealed class LeaseActorDirectoryOptions
{
    /// <summary>每次激活/续租授予的租约时长。默认 30 秒。</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// <see cref="IActorDirectory"/> 首版实现 —— 基于可插拔租约存储的单一激活（single-activation）保证。
/// </summary>
/// <remarks>
/// <para>
/// Phase D 起，目录本身只负责编排 <c>(Hub, Key)</c> 到 owner node 的租约语义；实际 CAS + TTL 持久化由
/// <see cref="IActorLeaseStore"/> 提供。默认 <see cref="InMemoryActorLeaseStore"/> 用于单进程和测试；生产环境可替换为
/// Redis/Etcd/数据库实现。
/// </para>
/// </remarks>
public sealed class LeaseActorDirectory : IActorDirectory
{
    private readonly IActorLeaseStore _leaseStore;
    private readonly TimeSpan _leaseDuration;

    /// <summary>创建租约目录。</summary>
    public LeaseActorDirectory(IOptions<LeaseActorDirectoryOptions>? options = null, IActorLeaseStore? leaseStore = null)
    {
        var configured = options?.Value?.LeaseDuration ?? TimeSpan.FromSeconds(30);
        _leaseDuration = configured > TimeSpan.Zero ? configured : TimeSpan.FromSeconds(30);
        _leaseStore = leaseStore ?? new InMemoryActorLeaseStore();
    }

    /// <inheritdoc/>
    public ValueTask<ActorPlacement?> ResolveAsync(string hub, string key, CancellationToken cancellationToken = default)
        => _leaseStore.ResolveAsync(hub, key, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ActorPlacement> ActivateAsync(string hub, string key, string candidateNodeId, CancellationToken cancellationToken = default)
        => _leaseStore.ActivateAsync(hub, key, candidateNodeId, _leaseDuration, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> RenewAsync(string hub, string key, string nodeId, string leaseId, CancellationToken cancellationToken = default)
        => _leaseStore.RenewAsync(hub, key, nodeId, leaseId, _leaseDuration, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(string hub, string key, string nodeId, string leaseId, CancellationToken cancellationToken = default)
        => _leaseStore.ReleaseAsync(hub, key, nodeId, leaseId, cancellationToken);
}
