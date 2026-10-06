using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Clustering;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;

namespace PulseRPC.Server.Clustering;

/// <summary>Actor 租约续租心跳。</summary>
public interface IActorLeaseHeartbeat
{
    /// <summary>跟踪一个本节点持有的 Actor 租约，并由后台心跳续租。</summary>
    void Track(string hub, string key, ActorPlacement placement);
    /// <summary>停止跟踪一个 Actor 租约。</summary>
    void Untrack(string hub, string key, string leaseId);
}

internal interface IActorLeaseBinding
{
    void BindService(string hub, string key, ActorPlacement placement, IPulseService service);
}

/// <summary><see cref="ActorLeaseHeartbeat"/> 配置项。</summary>
public sealed class ActorLeaseHeartbeatOptions
{
    /// <summary>续租间隔。默认 10 秒。</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>同时续租的最大 Actor 数量。默认 8。</summary>
    public int MaxConcurrentRenewals { get; set; } = 8;
    /// <summary>单次续租和确认操作的最长等待时间。默认 5 秒。</summary>
    public TimeSpan RenewalTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>使用独立到期检查和有界并发续租的 owner 租约续租器。</summary>
/// <remarks>
/// 明确失租或超过最后确认的有效期时，永久撤销该实例的执行资格并清理旧实例。
/// 业务存储仍须拒绝旧 owner 的写入；合作式取消无法撤回已经发生的副作用。
/// </remarks>
public sealed class ActorLeaseHeartbeat : IActorLeaseHeartbeat, IActorLeaseBinding,
    IServiceInstanceLeaseBindingLifetime, IDisposable
{
    private readonly IActorDirectory _directory;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _renewalTimeout;
    private readonly int _maxConcurrentRenewals;
    private readonly SemaphoreSlim _renewalSlots;
    private readonly TimeProvider _time;
    private readonly Func<IPulseService, ValueTask>? _removeService;
    private readonly ILogger<ActorLeaseHeartbeat> _logger;
    private readonly ConcurrentDictionary<(string Hub, string Key), ActorLeaseState> _tracked = new();
    private readonly ConditionalWeakTable<IPulseService, ActorLeaseState> _bindings = new();
    private readonly object _lifecycleLock = new();
    private readonly ITimer _timer;
    private Task? _renewTask;
    private long _lastRenewal;
    private int _renewing;
    private bool _disposed;

    /// <summary>创建租约续租器。</summary>
    public ActorLeaseHeartbeat(IActorDirectory directory, ActorLeaseHeartbeatOptions? options = null)
        : this(directory, options, null, TimeProvider.System, NullLogger<ActorLeaseHeartbeat>.Instance)
    {
    }

    internal ActorLeaseHeartbeat(IActorDirectory directory, ActorLeaseHeartbeatOptions? options,
        Func<IPulseService, ValueTask>? removeService, TimeProvider time,
        ILogger<ActorLeaseHeartbeat>? logger = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _interval = options?.Interval > TimeSpan.Zero ? options.Interval : TimeSpan.FromSeconds(10);
        _renewalTimeout = options?.RenewalTimeout > TimeSpan.Zero ? options.RenewalTimeout : TimeSpan.FromSeconds(5);
        _maxConcurrentRenewals = options?.MaxConcurrentRenewals > 0 ? options.MaxConcurrentRenewals : 8;
        _renewalSlots = new SemaphoreSlim(_maxConcurrentRenewals, _maxConcurrentRenewals);
        _removeService = removeService;
        _time = time;
        _logger = logger ?? NullLogger<ActorLeaseHeartbeat>.Instance;
        _lastRenewal = time.GetTimestamp();
        var checkInterval = TimeSpan.FromMilliseconds(Math.Min(250, _interval.TotalMilliseconds));
        _timer = time.CreateTimer(_ => ScheduleRenewal(), null, checkInterval, checkInterval);
    }

    /// <inheritdoc/>
    public void Track(string hub, string key, ActorPlacement placement)
    {
        if (string.IsNullOrEmpty(hub) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(placement.LeaseId)) return;
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            var lease = GetOrCreateLease(hub, key, placement);
            lease.ThrowIfInvalid();
            lease.Activated = true;
        }
    }

    void IActorLeaseBinding.BindService(string hub, string key, ActorPlacement placement, IPulseService service)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_bindings.TryGetValue(service, out var bound))
            {
                if (bound.Hub != hub || bound.Key != key || bound.LeaseId != placement.LeaseId || bound.NodeId != placement.NodeId)
                {
                    bound.Revoke();
                    throw new InvalidOperationException("An existing Actor instance cannot be rebound to another lease generation.");
                }
                bound.ThrowIfInvalid();
                return;
            }
            var lease = GetOrCreateLease(hub, key, placement);
            lease.ThrowIfInvalid();
            if (lease.Service is not null && !ReferenceEquals(lease.Service, service))
                throw new InvalidOperationException("An Actor lease is already bound to another service instance.");
            if (service is PulseServiceBase actor) actor.BindActorLease(lease);
            lease.Service = service;
            _bindings.Add(service, lease);
        }
    }

    /// <inheritdoc/>
    public void Untrack(string hub, string key, string leaseId)
    {
        if (_tracked.TryGetValue((hub, key), out var current) && current.LeaseId == leaseId
            && _tracked.TryRemove(new KeyValuePair<(string, string), ActorLeaseState>((hub, key), current)))
            current.Revoke(notify: false);
    }

    private ActorLeaseState GetOrCreateLease(string hub, string key, ActorPlacement placement)
    {
        if (_tracked.TryGetValue((hub, key), out var current))
        {
            if (current.LeaseId == placement.LeaseId && current.NodeId == placement.NodeId)
            {
                current.Refresh(placement);
                return current;
            }
            current.Revoke();
            if (current.Service is not null)
                throw new InvalidOperationException("The previous Actor generation must finish cleanup before reacquisition.");
        }
        var lease = new ActorLeaseState(hub, key, placement, _time, OnLeaseLost);
        _tracked[(hub, key)] = lease;
        return lease;
    }

    async ValueTask IServiceInstanceLeaseLifetime.ReleaseAsync(string hub, string key, CancellationToken cancellationToken)
    {
        if (_tracked.TryRemove((hub, key), out var lease))
            await ReleaseLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask IServiceInstanceLeaseBindingLifetime.ReleaseAsync(IPulseService service, CancellationToken cancellationToken)
    {
        if (_bindings.TryGetValue(service, out var lease))
        {
            _tracked.TryRemove(new KeyValuePair<(string, string), ActorLeaseState>((lease.Hub, lease.Key), lease));
            await ReleaseLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ((IServiceInstanceLeaseLifetime)this).ReleaseAsync(service.ServiceType, service.ServiceId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask ReleaseLeaseAsync(ActorLeaseState lease, CancellationToken cancellationToken)
    {
        lease.Revoke(notify: false);
        await _directory.ReleaseAsync(lease.Hub, lease.Key, lease.NodeId, lease.LeaseId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Task? renewTask;
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            renewTask = _renewTask;
        }
        renewTask?.GetAwaiter().GetResult();
        foreach (var lease in _tracked.Values) lease.Revoke();
    }

    private void ScheduleRenewal()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            CheckExpirations();
            if (_renewTask is { IsCompleted: false } || _time.GetElapsedTime(_lastRenewal) < _interval) return;
            _lastRenewal = _time.GetTimestamp();
            _renewTask = RenewAllAsync();
        }
    }

    internal void CheckExpirations()
    {
        foreach (var lease in _tracked.Values)
        {
            _ = lease.IsValid;
            if (lease.IsLost && lease.Service is not null) ScheduleCleanup(lease);
        }
    }

    private async Task RenewAllAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _renewing, 1) == 1) return;
        try
        {
            await Parallel.ForEachAsync(_tracked.ToArray(),
                new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrentRenewals },
                async (entry, schedulerToken) =>
                {
                    var lease = entry.Value;
                    if (!lease.Activated || !lease.IsValid || lease.Renewal is { IsCompleted: false }) return;
                    if (!_renewalSlots.Wait(0)) return;
                    lease.Renewal = RenewLeaseAsync(lease);
                    try
                    {
                        await lease.Renewal.WaitAsync(_renewalTimeout).ConfigureAwait(false);
                    }
                    catch (TimeoutException ex)
                    {
                        // A backend may ignore cancellation. Keep its slot until it actually
                        // finishes so repeated timeouts cannot create unbounded backend tasks.
                        _logger.LogWarning(ex, "Actor lease renewal timed out for {Hub}:{Key}", lease.Hub, lease.Key);
                        _ = lease.IsValid;
                    }
                }).ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref _renewing, 0); }
    }

    private async Task RenewLeaseAsync(ActorLeaseState lease)
    {
        using var timeout = new CancellationTokenSource(_renewalTimeout);
        try
        {
            var renewed = await _directory.RenewAsync(lease.Hub, lease.Key, lease.NodeId, lease.LeaseId, timeout.Token)
                .ConfigureAwait(false);
            if (!renewed) { lease.Revoke(); return; }
            // Confirm the actual expiry; the bool renewal contract does not specify duration.
            var placement = await _directory.ResolveAsync(lease.Hub, lease.Key, timeout.Token).ConfigureAwait(false);
            if (placement is null) lease.Revoke();
            else lease.Refresh(placement.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Actor lease renewal failed for {Hub}:{Key}", lease.Hub, lease.Key);
            _ = lease.IsValid;
        }
        finally { _renewalSlots.Release(); }
    }

    private void OnLeaseLost(ActorLeaseState lease)
    {
        _logger.LogWarning("Actor lease revoked: {Hub}:{Key}, lease {LeaseId}", lease.Hub, lease.Key, lease.LeaseId);
        if (lease.Service is null)
            _tracked.TryRemove(new KeyValuePair<(string, string), ActorLeaseState>((lease.Hub, lease.Key), lease));
        else
            ScheduleCleanup(lease);
    }

    private void ScheduleCleanup(ActorLeaseState lease)
    {
        lock (_lifecycleLock)
        {
            if (lease.CleanupCompleted || lease.Cleanup is { IsCompleted: false }) return;
            lease.Cleanup = Task.Run(async () =>
            {
                try
                {
                    if (_removeService is not null)
                        await _removeService(lease.Service!).ConfigureAwait(false);
                    else
                        await lease.Service!.StopAsync().ConfigureAwait(false);
                    lease.CleanupCompleted = true;
                    _tracked.TryRemove(new KeyValuePair<(string, string), ActorLeaseState>((lease.Hub, lease.Key), lease));
                }
                catch (Exception ex)
                {
                    // Retain the revoked generation for cleanup retry, never for reactivation.
                    _logger.LogError(ex, "Actor cleanup after lease loss failed for {Hub}:{Key}", lease.Hub, lease.Key);
                }
            });
        }
    }
}
