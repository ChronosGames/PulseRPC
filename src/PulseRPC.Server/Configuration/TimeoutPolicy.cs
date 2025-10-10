namespace PulseRPC.Server.Configuration;

/// <summary>
/// Timeout policy for service method invocations.
/// </summary>
public sealed class TimeoutPolicy
{
    /// <summary>
    /// Default timeout duration for service method invocations.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum allowed timeout duration.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Grace period after timeout before forcefully cancelling.
    /// Default: 1 second
    /// </summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to enforce timeout limits globally.
    /// Default: true
    /// </summary>
    public bool EnforceTimeoutLimits { get; set; } = true;

    /// <summary>
    /// Custom timeout overrides per service name.
    /// Key: ServiceName, Value: Timeout duration
    /// </summary>
    public Dictionary<string, TimeSpan> ServiceTimeouts { get; set; } = new();

    /// <summary>
    /// Gets the effective timeout for a service.
    /// </summary>
    public TimeSpan GetTimeoutForService(string serviceName)
    {
        if (ServiceTimeouts.TryGetValue(serviceName, out var serviceTimeout))
        {
            return EnforceTimeoutLimits
                ? TimeSpan.FromMilliseconds(Math.Min(serviceTimeout.TotalMilliseconds, MaxTimeout.TotalMilliseconds))
                : serviceTimeout;
        }

        return DefaultTimeout;
    }

    /// <summary>
    /// Creates a CancellationTokenSource with the appropriate timeout.
    /// </summary>
    public CancellationTokenSource CreateTimeoutTokenSource(string serviceName)
    {
        var timeout = GetTimeoutForService(serviceName);
        return new CancellationTokenSource(timeout);
    }

    /// <summary>
    /// Creates a linked CancellationTokenSource with timeout.
    /// </summary>
    public CancellationTokenSource CreateLinkedTimeoutTokenSource(string serviceName, CancellationToken parentToken)
    {
        var timeout = GetTimeoutForService(serviceName);
        return CancellationTokenSource.CreateLinkedTokenSource(parentToken, new CancellationTokenSource(timeout).Token);
    }
}
