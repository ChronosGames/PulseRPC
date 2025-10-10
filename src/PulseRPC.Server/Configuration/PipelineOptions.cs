namespace PulseRPC.Server.Configuration;

/// <summary>
/// Configuration options for the message processing pipeline.
/// </summary>
public sealed class PipelineOptions
{
    /// <summary>
    /// Maximum queue depth for the message dispatcher.
    /// Default: 10,000
    /// </summary>
    public int MaxQueueDepth { get; set; } = 10_000;

    /// <summary>
    /// Number of worker threads for message processing.
    /// Default: Environment.ProcessorCount
    /// </summary>
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum batch size for response transmission.
    /// Default: 100
    /// </summary>
    public int ResponseBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum batch delay for response transmission in milliseconds.
    /// Default: 1ms
    /// </summary>
    public int ResponseBatchDelayMs { get; set; } = 1;

    /// <summary>
    /// Enable distributed tracing with System.Diagnostics.Activity.
    /// Default: true
    /// </summary>
    public bool EnableDistributedTracing { get; set; } = true;

    /// <summary>
    /// Enable performance metrics collection.
    /// Default: true
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Backpressure policy configuration.
    /// </summary>
    public BackpressurePolicy BackpressurePolicy { get; set; } = new();

    /// <summary>
    /// Default timeout policy for service invocations.
    /// </summary>
    public TimeoutPolicy DefaultTimeoutPolicy { get; set; } = new();
}
