namespace PulseRPC.Scheduling;

/// <summary>
/// Composite key that uniquely identifies a service instance for scheduling.
/// Combines ServiceName (from ChannelAttribute) with ServiceId (from authentication).
/// </summary>
public readonly struct ServiceSchedulingKey : IEquatable<ServiceSchedulingKey>
{
    /// <summary>
    /// Gets the logical service name from ChannelAttribute.
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// Gets the unique service instance identifier from authentication.
    /// </summary>
    public string ServiceId { get; }

    /// <summary>
    /// Initializes a new ServiceSchedulingKey.
    /// </summary>
    /// <param name="serviceName">The service name. Must not be null or whitespace.</param>
    /// <param name="serviceId">The service instance ID. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">If serviceName or serviceId is null or whitespace.</exception>
    public ServiceSchedulingKey(string serviceName, string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("ServiceName must not be null or whitespace.", nameof(serviceName));
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId must not be null or whitespace.", nameof(serviceId));

        ServiceName = serviceName;
        ServiceId = serviceId;
    }

    /// <summary>
    /// Determines whether two keys are equal.
    /// </summary>
    public bool Equals(ServiceSchedulingKey other) =>
        ServiceName == other.ServiceName && ServiceId == other.ServiceId;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ServiceSchedulingKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(ServiceName, ServiceId);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{ServiceName}:{ServiceId}";

    public static bool operator ==(ServiceSchedulingKey left, ServiceSchedulingKey right) =>
        left.Equals(right);

    public static bool operator !=(ServiceSchedulingKey left, ServiceSchedulingKey right) =>
        !left.Equals(right);
}