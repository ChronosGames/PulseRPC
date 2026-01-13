using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Scheduling;
using PulseRPC.Server.Health;

namespace PulseRPC.Server.Services.Scheduling;

/// <summary>
/// Main scheduler implementation that routes service invocations to dedicated threads based on ServiceSchedulingKey.
/// </summary>
public sealed class ServiceThreadScheduler : IServiceScheduler
{
    private readonly ServiceThreadPool _threadPool;
    private readonly SchedulerConfiguration _configuration;
    private readonly ILogger<ServiceThreadScheduler> _logger;
    private readonly ServiceHealthMonitor? _healthMonitor;
    private bool _isDisposed;

    /// <summary>
    /// Gets whether the scheduler is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets the health monitor (if enabled).
    /// </summary>
    public ServiceHealthMonitor? HealthMonitor => _healthMonitor;

    /// <summary>
    /// Initializes a new ServiceThreadScheduler.
    /// </summary>
    public ServiceThreadScheduler(
        SchedulerConfiguration configuration,
        ILogger<ServiceThreadScheduler>? logger = null,
        ServiceHealthMonitor? healthMonitor = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();

        _logger = logger ?? NullLogger<ServiceThreadScheduler>.Instance;
        _healthMonitor = healthMonitor;
        _threadPool = new ServiceThreadPool(configuration, logger);

        if (_healthMonitor != null)
        {
            _logger.LogInformation("ServiceThreadScheduler initialized with health monitoring enabled");
        }
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

        // Health check: reject requests if circuit is broken
        if (_healthMonitor != null)
        {
            var healthState = _healthMonitor.GetHealthState(key);
            if (healthState == ServiceHealthState.CircuitBroken)
            {
                _logger.LogWarning(
                    "Request rejected - service circuit broken: {ServiceName}:{ServiceId}",
                    key.ServiceName, key.ServiceId);

                throw new InvalidOperationException(
                    $"Service instance {key.ServiceName}:{key.ServiceId} is unavailable (circuit broken). Please try again later.");
            }
        }

        // Wrap work with health monitoring
        Func<Task> monitoredWork = async () =>
        {
            try
            {
                await work();

                // Record success
                _healthMonitor?.RecordSuccess(key);
            }
            catch (Exception ex)
            {
                // Record failure
                _healthMonitor?.RecordFailure(key, ex);
                throw;
            }
        };

        // Create work item
        var workItem = new WorkItem(key, monitoredWork, MessagePriority.Normal);

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

        if (_healthMonitor != null)
        {
            await _healthMonitor.DisposeAsync();
        }

        _isDisposed = true;

        _logger.LogInformation("ServiceThreadScheduler disposed");
    }
}