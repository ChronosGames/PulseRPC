using PulseRPC.Server.Core;
using PulseRPC.Server.Observability;
using PulseRPC.Server.Pipeline;
using System;
using System.ComponentModel.DataAnnotations;

namespace PulseRPC.Server.Configuration;

/// <summary>
/// Complete server configuration covering FR-063 to FR-067.
/// Thread pool sizes, queue capacities, timeout values, and transport parameters.
/// </summary>
public sealed class PulseServerOptions
{
    /// <summary>
    /// Server name (default: "PulseRPC-Server").
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ServerName { get; set; } = "PulseRPC-Server";

    /// <summary>
    /// Server version (default: "1.0.0").
    /// </summary>
    [Required]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// IP address to bind to (default: "0.0.0.0").
    /// </summary>
    [Required]
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// Port to listen on (default: 5000).
    /// FR-063: Transport parameters.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 5000;

    /// <summary>
    /// Maximum concurrent connections (default: 10,000).
    /// FR-064: Connection limits.
    /// </summary>
    [Range(1, 100_000)]
    public int MaxConnections { get; set; } = 10_000;

    /// <summary>
    /// Number of worker threads for message processing (default: Environment.ProcessorCount).
    /// FR-063: Thread pool size.
    /// </summary>
    [Range(1, 1024)]
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum queue depth per priority level (default: 10,000).
    /// FR-064: Queue capacity.
    /// </summary>
    [Range(100, 1_000_000)]
    public int MaxQueueDepthPerPriority { get; set; } = 10_000;

    /// <summary>
    /// Default timeout for RPC invocations (default: 30 seconds).
    /// FR-065: Timeout values.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan DefaultInvocationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Connection idle timeout (default: 5 minutes).
    /// FR-066: Connection timeout.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan ConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum message size (default: 10 MB).
    /// FR-067: Transport parameters.
    /// </summary>
    [Range(1024, 100 * 1024 * 1024)]
    public int MaxMessageSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Receive buffer size per connection (default: 8 KB).
    /// FR-067: Transport parameters.
    /// </summary>
    [Range(1024, 1024 * 1024)]
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// Send buffer size per connection (default: 8 KB).
    /// FR-067: Transport parameters.
    /// </summary>
    [Range(1024, 1024 * 1024)]
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// Enable TCP_NODELAY (default: true).
    /// FR-067: Transport parameters.
    /// </summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>
    /// Socket backlog size (default: 100).
    /// FR-067: Transport parameters.
    /// </summary>
    [Range(10, 10000)]
    public int BacklogSize { get; set; } = 100;

    /// <summary>
    /// Message receiver options.
    /// </summary>
    public MessageReceiverOptions MessageReceiverOptions { get; set; } = new();

    /// <summary>
    /// Message dispatcher options.
    /// </summary>
    public MessageDispatcherOptions MessageDispatcherOptions { get; set; } = new();

    /// <summary>
    /// Response transmitter options.
    /// </summary>
    public ResponseTransmitterOptions ResponseTransmitterOptions { get; set; } = new();

    /// <summary>
    /// Connection manager options.
    /// </summary>
    public ConnectionManagerOptions ConnectionManagerOptions { get; set; } = new();

    /// <summary>
    /// Service registry options.
    /// </summary>
    public ServiceRegistryOptions ServiceRegistryOptions { get; set; } = new();

    /// <summary>
    /// Backpressure policy options.
    /// </summary>
    public BackpressurePolicyOptions BackpressurePolicyOptions { get; set; } = new();

    /// <summary>
    /// Metrics collector options.
    /// </summary>
    public MetricsCollectorOptions MetricsCollectorOptions { get; set; } = new();

    /// <summary>
    /// Enable distributed tracing (default: false).
    /// </summary>
    public bool EnableDistributedTracing { get; set; } = false;

    /// <summary>
    /// Enable diagnostic endpoints (default: true).
    /// </summary>
    public bool EnableDiagnosticEndpoints { get; set; } = true;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
        {
            throw new ValidationException("ServerName cannot be null or empty");
        }

        if (Port < 1 || Port > 65535)
        {
            throw new ValidationException($"Port must be between 1 and 65535 (got: {Port})");
        }

        if (MaxConnections < 1)
        {
            throw new ValidationException($"MaxConnections must be positive (got: {MaxConnections})");
        }

        if (WorkerThreadCount < 1)
        {
            throw new ValidationException($"WorkerThreadCount must be positive (got: {WorkerThreadCount})");
        }

        if (MaxQueueDepthPerPriority < 100)
        {
            throw new ValidationException($"MaxQueueDepthPerPriority must be at least 100 (got: {MaxQueueDepthPerPriority})");
        }

        if (DefaultInvocationTimeout < TimeSpan.FromSeconds(1))
        {
            throw new ValidationException($"DefaultInvocationTimeout must be at least 1 second (got: {DefaultInvocationTimeout})");
        }

        if (MaxMessageSize < 1024)
        {
            throw new ValidationException($"MaxMessageSize must be at least 1KB (got: {MaxMessageSize})");
        }
    }

    /// <summary>
    /// Creates a ServerHostOptions instance from this configuration.
    /// </summary>
    public ServerHostOptions ToServerHostOptions()
    {
        // Apply top-level settings to component options
        MessageDispatcherOptions.WorkerThreadCount = WorkerThreadCount;
        MessageDispatcherOptions.MaxQueueDepthPerPriority = MaxQueueDepthPerPriority;
        MessageDispatcherOptions.DefaultTimeout = DefaultInvocationTimeout;

        MessageReceiverOptions.MaxBufferSize = MaxMessageSize;

        ConnectionManagerOptions.MaxConnections = MaxConnections;
        ConnectionManagerOptions.InactivityTimeout = ConnectionIdleTimeout;

        ServiceRegistryOptions.DefaultTimeout = DefaultInvocationTimeout;

        return new ServerHostOptions
        {
            MessageReceiverOptions = MessageReceiverOptions,
            MessageDispatcherOptions = MessageDispatcherOptions,
            ResponseTransmitterOptions = ResponseTransmitterOptions,
            ConnectionManagerOptions = ConnectionManagerOptions,
            ServiceRegistryOptions = ServiceRegistryOptions,
            BackpressurePolicyOptions = BackpressurePolicyOptions
        };
    }
}

/// <summary>
/// Options for metrics collector configuration.
/// </summary>
public sealed class MetricsCollectorOptions
{
    /// <summary>
    /// Time window for keeping latency samples (default: 60 seconds).
    /// </summary>
    public double SampleWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of latency samples to keep (default: 10,000).
    /// </summary>
    public int MaxSampleCount { get; set; } = 10_000;
}
