using PulseRPC.Clustering;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Clustering;

/// <summary>One activation generation. Revocation is irreversible, even after a late renewal.</summary>
internal sealed class ActorLeaseState
{
    private readonly object _sync = new();
    private readonly TimeProvider _time;
    private readonly Action<ActorLeaseState> _onLost;
    private readonly CancellationTokenSource _revoked = new();
    private long _expiresAtUtcTicks;
    private long _observedAt;
    private TimeSpan _remainingAtObservation;
    private int _lost;

    internal ActorLeaseState(string hub, string key, ActorPlacement placement,
        TimeProvider time, Action<ActorLeaseState> onLost)
    {
        Hub = hub;
        Key = key;
        NodeId = placement.NodeId;
        LeaseId = placement.LeaseId;
        _time = time;
        _onLost = onLost;
        SetDeadline(placement.ExpiresAtUtcTicks);
    }

    internal string Hub { get; }
    internal string Key { get; }
    internal string NodeId { get; }
    internal string LeaseId { get; }
    internal IPulseService? Service { get; set; }
    internal bool Activated { get; set; }
    internal Task? Cleanup { get; set; }
    internal bool CleanupCompleted { get; set; }
    internal Task? Renewal { get; set; }
    internal CancellationToken CancellationToken => _revoked.Token;
    internal bool IsLost => Volatile.Read(ref _lost) != 0;

    internal bool IsValid
    {
        get
        {
            bool valid;
            lock (_sync)
            {
                valid = !IsLost && !HasExpired();
            }
            if (!valid) Revoke();
            return valid;
        }
    }

    internal void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new InvalidOperationException($"Actor lease is no longer valid: {Hub}:{Key} ({LeaseId}).");
    }

    internal bool Refresh(ActorPlacement placement)
    {
        bool valid;
        lock (_sync)
        {
            valid = !IsLost && !HasExpired()
                && placement.NodeId == NodeId && placement.LeaseId == LeaseId;
            if (valid && placement.ExpiresAtUtcTicks > _expiresAtUtcTicks)
                SetDeadline(placement.ExpiresAtUtcTicks);
        }
        if (!valid) Revoke();
        return valid;
    }

    internal void Revoke(bool notify = true)
    {
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _lost, 1) != 0) return;
        }
        // User cancellation callbacks must not hold up expiry checks for other Actors.
        _ = ObserveCancellationAsync();
        if (Service is PulseServiceBase actor) actor.QuiesceForLeaseLoss();
        if (notify) _onLost(this);
    }

    private bool HasExpired()
        => _time.GetUtcNow().UtcTicks >= _expiresAtUtcTicks
           || _time.GetElapsedTime(_observedAt) >= _remainingAtObservation;

    private void SetDeadline(long expiresAtUtcTicks)
    {
        _expiresAtUtcTicks = expiresAtUtcTicks;
        _observedAt = _time.GetTimestamp();
        _remainingAtObservation = TimeSpan.FromTicks(Math.Max(0, expiresAtUtcTicks - _time.GetUtcNow().UtcTicks));
    }

    private async Task ObserveCancellationAsync()
    {
        try { await _revoked.CancelAsync().ConfigureAwait(false); }
        catch { /* Revocation remains effective even if a user cancellation callback throws. */ }
    }
}
