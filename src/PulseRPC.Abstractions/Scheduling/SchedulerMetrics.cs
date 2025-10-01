namespace PulseRPC.Scheduling;

/// <summary>
/// Performance metrics for the service scheduler.
/// </summary>
public class SchedulerMetrics
{
    /// <summary>
    /// Gets or sets the number of active worker threads.
    /// </summary>
    public int ActiveThreadCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of queued messages across all threads.
    /// </summary>
    public int TotalQueuedMessages { get; set; }

    /// <summary>
    /// Gets or sets the P95 latency in milliseconds.
    /// </summary>
    public double P95LatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the total number of dropped messages.
    /// </summary>
    public long DroppedMessageCount { get; set; }
}