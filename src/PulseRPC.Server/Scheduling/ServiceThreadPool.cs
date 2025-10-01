using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Scheduling;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// Manages the pool of dedicated worker threads and assigns ServiceSchedulingKeys to threads.
/// </summary>
public sealed class ServiceThreadPool : IAsyncDisposable
{
    private readonly SchedulerConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly List<WorkerThread> _workers;
    private readonly ConcurrentDictionary<ServiceSchedulingKey, int> _keyToThreadMapping;
    private bool _isDisposed;

    /// <summary>
    /// Gets the current number of active worker threads.
    /// </summary>
    public int ThreadCount => _workers.Count;

    /// <summary>
    /// Initializes a new ServiceThreadPool.
    /// </summary>
    public ServiceThreadPool(SchedulerConfiguration configuration, ILogger? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();

        _logger = logger ?? NullLogger.Instance;
        _workers = new List<WorkerThread>();
        _keyToThreadMapping = new ConcurrentDictionary<ServiceSchedulingKey, int>();
    }

    /// <summary>
    /// Initialize the thread pool with the configured initial thread count.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing thread pool with {Count} threads", _configuration.InitialThreadCount);

        for (int i = 0; i < _configuration.InitialThreadCount; i++)
        {
            var worker = new WorkerThread(i, _configuration.ChannelCapacity, _logger);
            _workers.Add(worker);
            await worker.StartAsync(cancellationToken);
        }

        _logger.LogInformation("Thread pool initialized with {Count} workers", _workers.Count);
    }

    /// <summary>
    /// Get the thread index for a given service scheduling key using consistent hashing.
    /// </summary>
    public int GetThreadForKey(ServiceSchedulingKey key)
    {
        // Use consistent hashing: hash the key and mod by thread count
        var hash = key.GetHashCode();
        var threadIndex = Math.Abs(hash % ThreadCount);
        return threadIndex;
    }

    /// <summary>
    /// Enqueue work to the correct worker thread based on the scheduling key.
    /// </summary>
    public async Task EnqueueWorkAsync(WorkItem workItem, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceThreadPool));

        var threadIndex = GetThreadForKey(workItem.Key);
        var worker = _workers[threadIndex];

        await worker.EnqueueAsync(workItem, cancellationToken);
    }

    /// <summary>
    /// Get aggregated metrics from all worker threads.
    /// </summary>
    public SchedulerMetrics GetAggregatedMetrics()
    {
        var metrics = new SchedulerMetrics
        {
            ActiveThreadCount = _workers.Count(w => w.IsRunning),
            TotalQueuedMessages = _workers.Sum(w => w.CurrentQueueDepth),
            DroppedMessageCount = 0 // TODO: Implement dropped message tracking
        };

        return metrics;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _logger.LogInformation("Disposing thread pool with {Count} workers", _workers.Count);

        // Stop all worker threads
        foreach (var worker in _workers)
        {
            await worker.StopAsync();
            await worker.DisposeAsync();
        }

        _workers.Clear();
        _keyToThreadMapping.Clear();
        _isDisposed = true;
    }
}