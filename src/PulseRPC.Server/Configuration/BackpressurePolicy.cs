namespace PulseRPC.Server.Configuration;

/// <summary>
/// Multi-level backpressure policy for load management.
/// </summary>
public sealed class BackpressurePolicy
{
    /// <summary>
    /// Queue depth threshold for warning level (0.0-1.0).
    /// Default: 0.80 (80% capacity)
    /// </summary>
    public double WarningThreshold { get; set; } = 0.80;

    /// <summary>
    /// Queue depth threshold for throttling level (0.0-1.0).
    /// Default: 0.90 (90% capacity)
    /// </summary>
    public double ThrottleThreshold { get; set; } = 0.90;

    /// <summary>
    /// Duration throttling must persist before taking action.
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan ThrottleDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Queue depth threshold for rejecting connections (0.0-1.0).
    /// Default: 1.0 (100% capacity)
    /// </summary>
    public double RejectionThreshold { get; set; } = 1.0;

    /// <summary>
    /// Duration rejection must persist before taking action.
    /// Default: 10 seconds
    /// </summary>
    public TimeSpan RejectionDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Hysteresis threshold for releasing backpressure (0.0-1.0).
    /// Default: 0.70 (70% capacity)
    /// </summary>
    public double HysteresisThreshold { get; set; } = 0.70;

    /// <summary>
    /// Maximum memory usage in megabytes before applying backpressure.
    /// Default: 2048 MB (2 GB)
    /// </summary>
    public long MaxMemoryMB { get; set; } = 2048;

    /// <summary>
    /// Enable backpressure based on memory usage.
    /// Default: true
    /// </summary>
    public bool EnableMemoryBackpressure { get; set; } = true;

    private BackpressureLevel _currentLevel = BackpressureLevel.None;
    private DateTime _levelTransitionTime = DateTime.UtcNow;

    /// <summary>
    /// Current backpressure level.
    /// </summary>
    public BackpressureLevel CurrentLevel => _currentLevel;

    /// <summary>
    /// Evaluates queue depth and updates backpressure level.
    /// </summary>
    public BackpressureLevel EvaluateQueueDepth(int currentDepth, int maxDepth)
    {
        var ratio = (double)currentDepth / maxDepth;
        var now = DateTime.UtcNow;
        var timeSinceTransition = now - _levelTransitionTime;

        var newLevel = _currentLevel;

        // State machine for backpressure levels
        switch (_currentLevel)
        {
            case BackpressureLevel.None:
                if (ratio >= WarningThreshold)
                {
                    newLevel = BackpressureLevel.Warning;
                    _levelTransitionTime = now;
                }
                break;

            case BackpressureLevel.Warning:
                if (ratio < HysteresisThreshold)
                {
                    newLevel = BackpressureLevel.None;
                    _levelTransitionTime = now;
                }
                else if (ratio >= ThrottleThreshold && timeSinceTransition >= ThrottleDuration)
                {
                    newLevel = BackpressureLevel.Throttling;
                    _levelTransitionTime = now;
                }
                break;

            case BackpressureLevel.Throttling:
                if (ratio < HysteresisThreshold)
                {
                    newLevel = BackpressureLevel.None;
                    _levelTransitionTime = now;
                }
                else if (ratio >= RejectionThreshold && timeSinceTransition >= RejectionDuration)
                {
                    newLevel = BackpressureLevel.Rejecting;
                    _levelTransitionTime = now;
                }
                break;

            case BackpressureLevel.Rejecting:
                if (ratio < HysteresisThreshold)
                {
                    newLevel = BackpressureLevel.None;
                    _levelTransitionTime = now;
                }
                else if (ratio < ThrottleThreshold)
                {
                    newLevel = BackpressureLevel.Throttling;
                    _levelTransitionTime = now;
                }
                break;
        }

        _currentLevel = newLevel;
        return newLevel;
    }

    /// <summary>
    /// Evaluates memory usage and determines if backpressure should be applied.
    /// </summary>
    public bool ShouldApplyMemoryBackpressure()
    {
        if (!EnableMemoryBackpressure)
            return false;

        var currentMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
        return currentMemoryMB > MaxMemoryMB;
    }

    /// <summary>
    /// Resets backpressure state.
    /// </summary>
    public void Reset()
    {
        _currentLevel = BackpressureLevel.None;
        _levelTransitionTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Backpressure levels.
/// </summary>
public enum BackpressureLevel
{
    /// <summary>
    /// No backpressure - normal operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Warning level - queue depth at 80%.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Throttling level - slow down connection accept rate.
    /// </summary>
    Throttling = 2,

    /// <summary>
    /// Rejecting level - reject new connections.
    /// </summary>
    Rejecting = 3
}
