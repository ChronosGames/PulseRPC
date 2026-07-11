using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 基于内存表的 <see cref="IActorLeaseStore"/> 实现，用于单进程、测试和静态集群首版验证。
/// </summary>
public sealed class InMemoryActorLeaseStore : IActorLeaseStore
{
    private sealed class LeaseEntry
    {
        public required string NodeId { get; init; }
        public required string LeaseId { get; init; }
        public required long ExpiresAtUtcTicks { get; init; }
    }

    private readonly ConcurrentDictionary<string, LeaseEntry> _leases = new(StringComparer.Ordinal);

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
    public ValueTask<ActorPlacement> ActivateAsync(
        string hub,
        string key,
        string candidateNodeId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
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
                    return new ValueTask<ActorPlacement>(new ActorPlacement(existing.NodeId, existing.LeaseId, existing.ExpiresAtUtcTicks));
                }

                var replacement = NewEntry(candidateNodeId, now, leaseDuration);
                if (_leases.TryUpdate(addressKey, replacement, existing))
                {
                    return new ValueTask<ActorPlacement>(new ActorPlacement(replacement.NodeId, replacement.LeaseId, replacement.ExpiresAtUtcTicks));
                }

                continue;
            }

            var created = NewEntry(candidateNodeId, now, leaseDuration);
            if (_leases.TryAdd(addressKey, created))
            {
                return new ValueTask<ActorPlacement>(new ActorPlacement(created.NodeId, created.LeaseId, created.ExpiresAtUtcTicks));
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask<bool> RenewAsync(
        string hub,
        string key,
        string nodeId,
        string leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
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
            ExpiresAtUtcTicks = (DateTime.UtcNow + leaseDuration).Ticks,
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

    private static string BuildKey(string hub, string key) => $"{hub}:{key}";

    private static LeaseEntry NewEntry(string nodeId, DateTime utcNow, TimeSpan leaseDuration)
        => new()
        {
            NodeId = nodeId,
            LeaseId = Guid.NewGuid().ToString("N"),
            ExpiresAtUtcTicks = (utcNow + leaseDuration).Ticks,
        };
}
