using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Scheduling;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// Main scheduler implementation that routes service invocations to dedicated threads based on ServiceSchedulingKey.
/// </summary>
public sealed class ServiceThreadScheduler : IServiceScheduler
{
    private readonly ServiceThreadPool _threadPool;
    private readonly SchedulerConfiguration _configuration;
    private readonly ILogger<ServiceThreadScheduler> _logger;
    private bool _isDisposed;

    /// <summary>
    /// Gets whether the scheduler is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Initializes a new ServiceThreadScheduler.
    /// </summary>
    public ServiceThreadScheduler(
        SchedulerConfiguration configuration,
        ILogger<ServiceThreadScheduler>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();

        _logger = logger ?? NullLogger<ServiceThreadScheduler>.Instance;
        _threadPool = new ServiceThreadPool(configuration, logger);
    }

    /// <summary>
    /// Start the scheduler and initialize the worker thread pool.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("Scheduler is already started.");

        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceThreadScheduler));

        _logger.LogInformation("Starting ServiceThreadScheduler");

        await _threadPool.InitializeAsync(cancellationToken);
        IsRunning = true;

        _logger.LogInformation("ServiceThreadScheduler started with {ThreadCount} worker threads",
            _threadPool.ThreadCount);
    }

    /// <summary>
    /// Schedule work for execution on the thread assigned to the given key.
    /// </summary>
    public async Task ScheduleAsync(
        ServiceSchedulingKey key,
        Func<Task> work,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceThreadScheduler));

        if (work == null)
            throw new ArgumentNullException(nameof(work));

        if (string.IsNullOrWhiteSpace(key.ServiceId))
            throw new InvalidOperationException(
                $"ServiceId not set for ServiceName '{key.ServiceName}'. " +
                "Ensure authentication middleware sets ServiceId in IServiceContext.");

        if (!IsRunning)
            throw new InvalidOperationException("Scheduler not started. Call StartAsync() first.");

        // Create work item
        var workItem = new WorkItem(key, work, MessagePriority.Normal);

        // Enqueue to thread pool
        await _threadPool.EnqueueWorkAsync(workItem, cancellationToken);

        _logger.LogDebug("Scheduled work for key {Key}", key);
    }

    /// <summary>
    /// Stop the scheduler gracefully, completing in-flight work.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
            return;

        _logger.LogInformation("Stopping ServiceThreadScheduler");

        await _threadPool.DisposeAsync();
        IsRunning = false;

        _logger.LogInformation("ServiceThreadScheduler stopped");
    }

    /// <summary>
    /// Get current performance metrics.
    /// </summary>
    public SchedulerMetrics GetMetrics()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceThreadScheduler));

        return _threadPool.GetAggregatedMetrics();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        if (IsRunning)
        {
            await StopAsync();
        }

        await _threadPool.DisposeAsync();
        _isDisposed = true;

        _logger.LogInformation("ServiceThreadScheduler disposed");
    }
}