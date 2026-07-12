using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using PulseRPC.Clustering;
using PulseRPC.Server.Services.Management;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// Actor 租约续租心跳。
/// </summary>
public interface IActorLeaseHeartbeat
{
    /// <summary>跟踪一个本节点持有的 Actor 租约，并由后台心跳续租。</summary>
    void Track(string hub, string key, ActorPlacement placement);

    /// <summary>停止跟踪一个 Actor 租约。</summary>
    void Untrack(string hub, string key, string leaseId);
}

/// <summary>
/// <see cref="ActorLeaseHeartbeat"/> 配置项。
/// </summary>
public sealed class ActorLeaseHeartbeatOptions
{
    /// <summary>续租间隔。默认 10 秒。</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// 基于定时器的 owner 租约续租器。
/// </summary>
/// <remarks>
/// 当 owner 节点失效或租约被抢占时，续租会失败并停止跟踪；之后新的请求会经 placement/directory 重新激活。
/// </remarks>
public sealed class ActorLeaseHeartbeat : IActorLeaseHeartbeat, IServiceInstanceLeaseLifetime, IDisposable
{
    private readonly IActorDirectory _directory;
    private readonly TimeSpan _interval;
    private readonly ConcurrentDictionary<string, TrackedLease> _tracked = new(StringComparer.Ordinal);
    private readonly object _lifecycleLock = new();
    private readonly Timer _timer;
    private System.Threading.Tasks.Task? _renewTask;
    private int _renewing;
    private bool _disposed;

    /// <summary>创建租约续租器。</summary>
    public ActorLeaseHeartbeat(IActorDirectory directory, ActorLeaseHeartbeatOptions? options = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _interval = options?.Interval > TimeSpan.Zero ? options.Interval : TimeSpan.FromSeconds(10);
        _timer = new Timer(_ => ScheduleRenewal(), null, _interval, _interval);
    }

    /// <inheritdoc/>
    public void Track(string hub, string key, ActorPlacement placement)
    {
        if (_disposed || string.IsNullOrEmpty(hub) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(placement.LeaseId))
        {
            return;
        }

        _tracked[BuildKey(hub, key)] = new TrackedLease(hub, key, placement.NodeId, placement.LeaseId);
    }

    /// <inheritdoc/>
    public void Untrack(string hub, string key, string leaseId)
    {
        if (string.IsNullOrEmpty(hub) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(leaseId))
        {
            return;
        }

        var trackingKey = BuildKey(hub, key);
        if (_tracked.TryGetValue(trackingKey, out var current) && string.Equals(current.LeaseId, leaseId, StringComparison.Ordinal))
        {
            _tracked.TryRemove(new KeyValuePair<string, TrackedLease>(trackingKey, current));
        }
    }

    async ValueTask IServiceInstanceLeaseLifetime.ReleaseAsync(
        string hub,
        string key,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(hub) || string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!_tracked.TryRemove(BuildKey(hub, key), out var lease))
        {
            return;
        }

        await _directory.ReleaseAsync(
            lease.Hub,
            lease.Key,
            lease.NodeId,
            lease.LeaseId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        System.Threading.Tasks.Task? renewTask;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _timer.Dispose();
            renewTask = _renewTask;
        }

        renewTask?.GetAwaiter().GetResult();
    }

    private void ScheduleRenewal()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_renewTask is { IsCompleted: false })
            {
                return;
            }

            _renewTask = RenewAllAsync();
        }
    }

    private async System.Threading.Tasks.Task RenewAllAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _renewing, 1) == 1)
        {
            return;
        }

        try
        {
            foreach (var kvp in _tracked)
            {
                var lease = kvp.Value;
                bool renewed;
                try
                {
                    renewed = await _directory.RenewAsync(lease.Hub, lease.Key, lease.NodeId, lease.LeaseId).ConfigureAwait(false);
                }
                catch
                {
                    // 后端瞬时失败时保留跟踪项，下一轮继续续租；明确返回 false 才代表租约已失效/被抢占。
                    continue;
                }

                if (!renewed)
                {
                    _tracked.TryRemove(kvp);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _renewing, 0);
        }
    }

    private static string BuildKey(string hub, string key) => $"{hub}:{key}";

    private readonly struct TrackedLease
    {
        public TrackedLease(string hub, string key, string nodeId, string leaseId)
        {
            Hub = hub;
            Key = key;
            NodeId = nodeId;
            LeaseId = leaseId;
        }

        public string Hub { get; }
        public string Key { get; }
        public string NodeId { get; }
        public string LeaseId { get; }
    }
}
