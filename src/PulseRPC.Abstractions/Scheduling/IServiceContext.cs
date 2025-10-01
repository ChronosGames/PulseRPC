namespace PulseRPC.Scheduling;

/// <summary>
/// Provides access to service-specific context during request processing, including the ServiceId for scheduling.
/// </summary>
public interface IServiceContext
{
    /// <summary>
    /// Gets or sets the ServiceId for this service instance.
    /// Set during authentication; required for scheduling.
    /// </summary>
    string? ServiceId { get; set; }

    /// <summary>
    /// Gets the underlying connection identifier.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets whether the ServiceId has been set (indicating successful authentication).
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the ServiceName associated with the current service interface.
    /// Extracted from ChannelAttribute at compile-time.
    /// </summary>
    string ServiceName { get; }
}