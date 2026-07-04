using System;
using System.Collections.Concurrent;
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
/// <see cref="IActorDirectory"/> 首版实现 —— 基于内存租约表的单一激活（single-activation）保证。
/// </summary>
/// <remarks>
/// <para>
/// P4「静态成员 + 一致性哈希」拓扑下，任意 <c>(Hub, Key)</c> 在所有节点上经
/// <see cref="NodeConsistentHashRing"/> 算出的属主节点是确定且一致的，因此本目录无需在多个节点间
/// 做分布式共识：每个节点各自持有一份本类实例，只有当某节点确实是候选属主时其它节点才会经
/// <see cref="INodeLink"/> 把调用转发过来，从而天然形成"唯一权威"。租约机制仍然保留，用于在
/// 之后引入动态成员 / 故障接管（P7）时支撑迁移与抢占语义。
/// </para>
/// <para>线程安全：基于 <see cref="ConcurrentDictionary{TKey,TValue}"/> + CAS 重试，无锁。</para>
/// </remarks>
public sealed class LeaseActorDirectory : IActorDirectory
{
    private sealed class LeaseEntry
    {
        public required string NodeId { get; init; }
        public required string LeaseId { get; init; }
        public required long ExpiresAtUtcTicks { get; init; }
    }

    private readonly ConcurrentDictionary<string, LeaseEntry> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _leaseDuration;

    /// <summary>创建租约目录。</summary>
    public LeaseActorDirectory(IOptions<LeaseActorDirectoryOptions>? options = null)
    {
        var configured = options?.Value?.LeaseDuration ?? TimeSpan.FromSeconds(30);
        _leaseDuration = configured > TimeSpan.Zero ? configured : TimeSpan.FromSeconds(30);
    }

    private static string BuildKey(string hub, string key) => $"{hub}:{key}";

    /// <inheritdoc/>
    public ValueTask<ActorPlacement?> ResolveAsync(string hub, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(key);

        if (_leases.TryGetValue(BuildKey(hub, key), out var entry) && entry.ExpiresAtUtcTicks > DateTime.UtcNow.Ticks)
        {
            return new ValueTask<ActorPlacement?>(new ActorPlacement(entry.NodeId, entry.LeaseId, entry.ExpiresAtUtcTicks));
        }

        return new ValueTask<ActorPlacement?>((ActorPlacement?)null);
    }

    /// <inheritdoc/>
    public ValueTask<ActorPlacement> ActivateAsync(string hub, string key, string candidateNodeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(candidateNodeId);

        var addressKey = BuildKey(hub, key);

        while (true)
        {
            var now = DateTime.UtcNow;

            if (_leases.TryGetValue(addressKey, out var existing))
            {
                if (existing.ExpiresAtUtcTicks > now.Ticks)
                {
                    // 已有有效租约（无论是否为同一候选节点）：CAS 语义下不覆盖，直接返回既有放置。
                    return new ValueTask<ActorPlacement>(new ActorPlacement(existing.NodeId, existing.LeaseId, existing.ExpiresAtUtcTicks));
                }

                var replacement = new LeaseEntry
                {
                    NodeId = candidateNodeId,
                    LeaseId = Guid.NewGuid().ToString("N"),
                    ExpiresAtUtcTicks = (now + _leaseDuration).Ticks,
                };

                if (_leases.TryUpdate(addressKey, replacement, existing))
                {
                    return new ValueTask<ActorPlacement>(new ActorPlacement(replacement.NodeId, replacement.LeaseId, replacement.ExpiresAtUtcTicks));
                }

                continue; // 竞争失败（另一线程已抢先续/换租），重试
            }

            var created = new LeaseEntry
            {
                NodeId = candidateNodeId,
                LeaseId = Guid.NewGuid().ToString("N"),
                ExpiresAtUtcTicks = (now + _leaseDuration).Ticks,
            };

            if (_leases.TryAdd(addressKey, created))
            {
                return new ValueTask<ActorPlacement>(new ActorPlacement(created.NodeId, created.LeaseId, created.ExpiresAtUtcTicks));
            }

            // 竞争失败（另一线程刚插入），重试
        }
    }

    /// <inheritdoc/>
    public ValueTask<bool> RenewAsync(string hub, string key, string nodeId, string leaseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(leaseId);

        var addressKey = BuildKey(hub, key);

        if (!_leases.TryGetValue(addressKey, out var existing)
            || !string.Equals(existing.NodeId, nodeId, StringComparison.Ordinal)
            || !string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
        {
            return new ValueTask<bool>(false);
        }

        var renewed = new LeaseEntry
        {
            NodeId = nodeId,
            LeaseId = leaseId,
            ExpiresAtUtcTicks = (DateTime.UtcNow + _leaseDuration).Ticks,
        };

        return new ValueTask<bool>(_leases.TryUpdate(addressKey, renewed, existing));
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(string hub, string key, string nodeId, string leaseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(leaseId);

        var addressKey = BuildKey(hub, key);

        if (_leases.TryGetValue(addressKey, out var existing)
            && string.Equals(existing.NodeId, nodeId, StringComparison.Ordinal)
            && string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
        {
            _leases.TryRemove(addressKey, out _);
        }

        return default;
    }
}
