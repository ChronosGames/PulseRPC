using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// Represents a single dedicated thread that processes work items sequentially.
/// </summary>
public sealed class WorkerThread : IAsyncDisposable
{
    private readonly Channel<WorkItem> _messageChannel;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;
    private long _processedCount;
    private bool _isDisposed;

    /// <summary>
    /// Gets the unique thread identifier within the pool.
    /// </summary>
    public int ThreadId { get; }

    /// <summary>
    /// Gets whether the worker thread is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets the total number of messages processed.
    /// </summary>
    public long ProcessedCount => Interlocked.Read(ref _processedCount);

    /// <summary>
    /// Gets the current queue depth (approximation).
    /// </summary>
    public int CurrentQueueDepth => _messageChannel.Reader.Count;

    /// <summary>
    /// Initializes a new WorkerThread.
    /// </summary>
    /// <param name="threadId">The unique thread identifier.</param>
    /// <param name="channelCapacity">The bounded channel capacity.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public WorkerThread(int threadId, int channelCapacity, ILogger? logger = null)
    {
        ThreadId = threadId;
        _logger = logger ?? NullLogger.Instance;
        _cts = new CancellationTokenSource();

        // Create bounded channel with blocking behavior when full
        _messageChannel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait // Block when full (backpressure)
        });
    }

    /// <summary>
    /// Start the worker thread processing loop.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException($"WorkerThread {ThreadId} is already running.");

        IsRunning = true;
        _processingTask = Task.Run(() => ProcessingLoopAsync(_cts.Token), cancellationToken);

        _logger.LogInformation("WorkerThread {ThreadId} started", ThreadId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Enqueue work to be processed by this thread.
    /// Blocks if the channel is full (backpressure).
    /// </summary>
    public async Task EnqueueAsync(WorkItem workItem, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(WorkerThread));

        await _messageChannel.Writer.WriteAsync(workItem, cancellationToken);
    }

    /// <summary>
    /// Stop the worker thread gracefully, completing in-flight work.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
            return;

        // Complete the channel to stop accepting new work
        _messageChannel.Writer.Complete();

        // Signal cancellation
        _cts.Cancel();

        // Wait for processing task to complete
        if (_processingTask != null)
        {
            try
            {
                await _processingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        IsRunning = false;
        _logger.LogInformation("WorkerThread {ThreadId} stopped. Processed {Count} messages.",
            ThreadId, ProcessedCount);
    }

    private async Task ProcessingLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("WorkerThread {ThreadId} processing loop started on thread {ManagedThreadId}",
            ThreadId, Environment.CurrentManagedThreadId);

        try
        {
            await foreach (var workItem in _messageChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Execute the work
                    await workItem.Work();

                    // Increment processed count
                    Interlocked.Increment(ref _processedCount);

                    // Calculate and log latency if metrics enabled
                    var latency = DateTimeOffset.UtcNow - workItem.EnqueuedTime;
                    if (latency.TotalMilliseconds > 100)
                    {
                        _logger.LogWarning("WorkerThread {ThreadId} processed work with high latency: {LatencyMs}ms",
                            ThreadId, latency.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WorkerThread {ThreadId} failed to process work for key {Key}",
                        ThreadId, workItem.Key);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WorkerThread {ThreadId} processing loop cancelled", ThreadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkerThread {ThreadId} processing loop failed", ThreadId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        await StopAsync();
        _cts.Dispose();
        _isDisposed = true;
    }
}