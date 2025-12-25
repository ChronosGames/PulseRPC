using PulseRPC.Server.Core;
using PulseRPC.Server.Pipeline;

namespace PulseRPC.Server.Configuration;

/// <summary>
/// Configuration options for UnifiedPulseServer.
/// Consolidates options from both PulseServer and ServerHost architectures.
/// </summary>
public sealed class UnifiedServerOptions
{
    // === Transport Configuration (from PulseServer) ===

    /// <summary>
    /// List of transport configurations.
    /// At least one transport must be configured, and exactly one must be marked as default.
    /// </summary>
    public List<TransportChannelConfiguration> Transports { get; set; } = new();

    // === Pipeline Configuration (from ServerHost) ===

    /// <summary>
    /// Configuration for message receiver component.
    /// </summary>
    public MessageReceiverOptions MessageReceiver { get; set; } = new();

    /// <summary>
    /// Configuration for message dispatcher component.
    /// </summary>
    public MessageDispatcherOptions MessageDispatcher { get; set; } = new();

    /// <summary>
    /// Configuration for response transmitter component.
    /// </summary>
    public ResponseTransmitterOptions ResponseTransmitter { get; set; } = new();

    /// <summary>
    /// Configuration for backpressure policy component.
    /// </summary>
    public BackpressurePolicyOptions BackpressurePolicy { get; set; } = new();

    // === General Server Options ===

    /// <summary>
    /// Default timeout for server operations.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan DefaultOperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of concurrent operations.
    /// Default: 1000.
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 1000;

    /// <summary>
    /// Enable detailed logging for debugging.
    /// Default: false.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="InvalidOperationException">Configuration is invalid</exception>
    public void Validate()
    {
        if (Transports.Count == 0)
            throw new InvalidOperationException("At least one transport must be configured");

        var defaultCount = Transports.Count(t => t.IsDefault);
        if (defaultCount != 1)
            throw new InvalidOperationException($"Exactly one transport must be marked as default (found {defaultCount})");

        var uniqueNames = Transports.Select(t => t.Name).Distinct().Count();
        if (uniqueNames != Transports.Count)
            throw new InvalidOperationException("Transport names must be unique");

        // Validate port ranges
        foreach (var transport in Transports)
        {
            if (transport.Port < 1 || transport.Port > 65535)
                throw new InvalidOperationException($"Transport '{transport.Name}' port must be between 1 and 65535 (got {transport.Port})");
        }

        if (DefaultOperationTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("DefaultOperationTimeout must be positive");

        if (MaxConcurrentOperations <= 0)
            throw new InvalidOperationException("MaxConcurrentOperations must be greater than zero");
    }
}
