namespace PulseRPC.Server.Scheduling;

/// <summary>
/// Configuration options for the ServiceThreadScheduler.
/// </summary>
public sealed class SchedulerConfiguration
{
    /// <summary>
    /// Gets or sets the initial thread pool size.
    /// Default: Environment.ProcessorCount
    /// </summary>
    public int InitialThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets or sets the maximum thread pool size.
    /// Default: Environment.ProcessorCount * 2
    /// </summary>
    public int MaxThreadCount { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    /// Gets or sets the idle timeout before a thread is terminated.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan ThreadIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the bounded channel capacity per worker thread.
    /// Default: 1024
    /// </summary>
    public int ChannelCapacity { get; set; } = 1024;

    /// <summary>
    /// Gets or sets whether to enable priority-based message dropping when channels are full.
    /// Default: true (enable L3 degradation)
    /// </summary>
    public bool EnablePriorityDroppingWhenFull { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable performance metrics collection.
    /// Default: true
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Validates the configuration and throws if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">If configuration values are invalid.</exception>
    public void Validate()
    {
        if (InitialThreadCount <= 0)
            throw new ArgumentException("InitialThreadCount must be greater than 0.", nameof(InitialThreadCount));
        if (MaxThreadCount < InitialThreadCount)
            throw new ArgumentException("MaxThreadCount must be >= InitialThreadCount.", nameof(MaxThreadCount));
        if (ThreadIdleTimeout <= TimeSpan.Zero)
            throw new ArgumentException("ThreadIdleTimeout must be positive.", nameof(ThreadIdleTimeout));
        if (ChannelCapacity <= 0)
            throw new ArgumentException("ChannelCapacity must be greater than 0.", nameof(ChannelCapacity));
    }
}