using PulseRPC.Scheduling;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// Implementation of IServiceContext for providing service-specific context during request processing.
/// </summary>
public sealed class ServiceExecutionContext : IServiceContext
{
    /// <summary>
    /// Gets or sets the ServiceId for this service instance.
    /// Set during authentication; required for scheduling.
    /// </summary>
    public string? ServiceId { get; set; }

    /// <summary>
    /// Gets the underlying connection identifier.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the ServiceName associated with the current service interface.
    /// Extracted from ChannelAttribute at compile-time.
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// Gets whether the ServiceId has been set (indicating successful authentication).
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(ServiceId);

    /// <summary>
    /// Initializes a new ServiceExecutionContext.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="serviceName">The service name from ChannelAttribute.</param>
    /// <param name="serviceId">Optional initial ServiceId (typically null until authentication).</param>
    public ServiceExecutionContext(string connectionId, string serviceName, string? serviceId = null)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        ServiceId = serviceId;
    }
}