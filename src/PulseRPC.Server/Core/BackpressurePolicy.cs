using System;
using System.Threading;

namespace PulseRPC.Server.Core;

/// <summary>
/// Multi-level backpressure policy for handling system overload.
/// Implements FR-039: queue monitoring → throttling → rejection.
/// Three-level strategy with hysteresis to prevent oscillation.
/// </summary>
public sealed class BackpressurePolicy
{
    private readonly BackpressurePolicyOptions _options;
    private long _currentQueueDepth;
    private BackpressureLevel _currentLevel;
    private readonly object _lock = new();

    /// <summary>
    /// Gets the current backpressure level.
    /// </summary>
    public BackpressureLevel CurrentLevel
    {
        get
        {
            lock (_lock)
            {
                return _currentLevel;
            }
        }
    }

    /// <summary>
    /// Gets the current queue depth.
    /// </summary>
    public long CurrentQueueDepth => Interlocked.Read(ref _currentQueueDepth);

    public BackpressurePolicy(BackpressurePolicyOptions? options = null)
    {
        _options = options ?? new BackpressurePolicyOptions();
        _currentLevel = BackpressureLevel.None;
    }

    /// <summary>
    /// Updates the current queue depth and evaluates backpressure level.
    /// </summary>
    public BackpressureLevel UpdateQueueDepth(long queueDepth, long maxQueueCapacity)
    {
        Interlocked.Exchange(ref _currentQueueDepth, queueDepth);

        lock (_lock)
        {
            var utilizationRatio = (double)queueDepth / maxQueueCapacity;

            // Evaluate thresholds with hysteresis
            var newLevel = EvaluateLevel(utilizationRatio, _currentLevel);

            if (newLevel != _currentLevel)
            {
                _currentLevel = newLevel;
            }

            return _currentLevel;
        }
    }

    /// <summary>
    /// Determines if a request should be accepted based on current backpressure level.
    /// </summary>
    public BackpressureDecision ShouldAcceptRequest()
    {
        var level = CurrentLevel;

        switch (level)
        {
            case BackpressureLevel.None:
                return new BackpressureDecision
                {
                    Accept = true,
                    Level = level,
                    Reason = null
                };

            case BackpressureLevel.Throttle:
                // Apply throttling: random rejection based on throttle rate
                var shouldThrottle = Random.Shared.NextDouble() < _options.ThrottleRate;
                return new BackpressureDecision
                {
                    Accept = !shouldThrottle,
                    Level = level,
                    Reason = shouldThrottle ? "Throttled due to high queue utilization" : null
                };

            case BackpressureLevel.Reject:
                // Reject all new requests
                return new BackpressureDecision
                {
                    Accept = false,
                    Level = level,
                    Reason = "System overloaded - queue capacity exceeded"
                };

            default:
                return new BackpressureDecision
                {
                    Accept = true,
                    Level = level,
                    Reason = null
                };
        }
    }

    /// <summary>
    /// Gets backpressure statistics.
    /// </summary>
    public BackpressureStatistics GetStatistics()
    {
        return new BackpressureStatistics
        {
            CurrentLevel = CurrentLevel,
            CurrentQueueDepth = CurrentQueueDepth
        };
    }

    private BackpressureLevel EvaluateLevel(double utilizationRatio, BackpressureLevel currentLevel)
    {
        // Apply hysteresis to prevent oscillation
        switch (currentLevel)
        {
            case BackpressureLevel.None:
                // Transition to Throttle when queue is 70% full
                if (utilizationRatio >= _options.ThrottleThreshold)
                {
                    return BackpressureLevel.Throttle;
                }
                return BackpressureLevel.None;

            case BackpressureLevel.Throttle:
                // Transition to Reject when queue is 90% full
                if (utilizationRatio >= _options.RejectThreshold)
                {
                    return BackpressureLevel.Reject;
                }
                // Transition back to None when queue drops below 60% (hysteresis)
                if (utilizationRatio < _options.ThrottleThreshold - _options.Hysteresis)
                {
                    return BackpressureLevel.None;
                }
                return BackpressureLevel.Throttle;

            case BackpressureLevel.Reject:
                // Transition back to Throttle when queue drops below 80% (hysteresis)
                if (utilizationRatio < _options.RejectThreshold - _options.Hysteresis)
                {
                    return BackpressureLevel.Throttle;
                }
                return BackpressureLevel.Reject;

            default:
                return BackpressureLevel.None;
        }
    }
}

/// <summary>
/// Backpressure levels.
/// </summary>
public enum BackpressureLevel
{
    /// <summary>
    /// No backpressure - accept all requests.
    /// </summary>
    None = 0,

    /// <summary>
    /// Throttle mode - randomly reject some requests.
    /// </summary>
    Throttle = 1,

    /// <summary>
    /// Reject mode - reject all new requests.
    /// </summary>
    Reject = 2
}

/// <summary>
/// Configuration options for BackpressurePolicy.
/// </summary>
public sealed class BackpressurePolicyOptions
{
    /// <summary>
    /// Queue utilization threshold to enter throttle mode (default: 0.7 = 70%).
    /// </summary>
    public double ThrottleThreshold { get; set; } = 0.7;

    /// <summary>
    /// Queue utilization threshold to enter reject mode (default: 0.9 = 90%).
    /// </summary>
    public double RejectThreshold { get; set; } = 0.9;

    /// <summary>
    /// Hysteresis value to prevent oscillation (default: 0.1 = 10%).
    /// </summary>
    public double Hysteresis { get; set; } = 0.1;

    /// <summary>
    /// Throttle rate: probability of rejecting a request in throttle mode (default: 0.5 = 50%).
    /// </summary>
    public double ThrottleRate { get; set; } = 0.5;
}

/// <summary>
/// Decision result for a backpressure evaluation.
/// </summary>
public sealed class BackpressureDecision
{
    /// <summary>
    /// Whether the request should be accepted.
    /// </summary>
    public required bool Accept { get; init; }

    /// <summary>
    /// Current backpressure level.
    /// </summary>
    public required BackpressureLevel Level { get; init; }

    /// <summary>
    /// Reason for rejection (if not accepted).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Backpressure statistics.
/// </summary>
public sealed class BackpressureStatistics
{
    public BackpressureLevel CurrentLevel { get; init; }
    public long CurrentQueueDepth { get; init; }
}
