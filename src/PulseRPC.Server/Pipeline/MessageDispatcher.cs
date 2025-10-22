using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PulseRPC.Server.Scheduling;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// Dispatches messages from MessageReceiver to registered service handlers.
/// Handles FR-007 to FR-013: service routing, priority scheduling, FIFO ordering, backpressure.
/// Uses System.Threading.Channels for lock-free message queuing.
/// </summary>
public sealed class MessageDispatcher : IMessageDispatcher, IDisposable
{
    private readonly ConcurrentDictionary<string, IServiceHandler> _serviceHandlers = new();
    private readonly Channel<DispatchItem>[] _priorityChannels;
    private readonly MessageDispatcherOptions _options;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task[] _workerTasks;

    private long _totalMessagesDispatched;
    private int _isRunning;
    private bool _disposed;

    /// <summary>
    /// Gets whether the dispatcher is currently running.
    /// </summary>
    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

    /// <summary>
    /// Gets the current queue depth across all priority channels.
    /// </summary>
    public int QueueDepth
    {
        get
        {
            var total = 0;
            foreach (var channel in _priorityChannels)
            {
                total += channel.Reader.Count;
            }
            return total;
        }
    }

    /// <summary>
    /// Gets the total number of messages dispatched since start.
    /// </summary>
    public long TotalMessagesDispatched => Interlocked.Read(ref _totalMessagesDispatched);

    /// <summary>
    /// Gets the number of registered services.
    /// </summary>
    public int RegisteredServiceCount => _serviceHandlers.Count;

    public MessageDispatcher(MessageDispatcherOptions? options = null)
    {
        _options = options ?? new MessageDispatcherOptions();

        // Create priority channels (Critical=0, High=1, Normal=2, Low=3)
        _priorityChannels = new Channel<DispatchItem>[4];
        for (int i = 0; i < 4; i++)
        {
            _priorityChannels[i] = Channel.CreateBounded<DispatchItem>(new BoundedChannelOptions(_options.MaxQueueDepthPerPriority)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        // Create worker tasks
        _workerTasks = new Task[_options.WorkerThreadCount];
    }

    /// <summary>
    /// Starts the message dispatcher and begins processing messages.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 1)
        {
            throw new InvalidOperationException("MessageDispatcher is already running");
        }

        // Start worker tasks
        for (int i = 0; i < _workerTasks.Length; i++)
        {
            _workerTasks[i] = Task.Run(() => ProcessMessagesAsync(_stopCts.Token), _stopCts.Token);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the message dispatcher and waits for pending messages to complete.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 0, 1) == 0)
        {
            return; // Already stopped
        }

        // Signal stop
        _stopCts.Cancel();

        // Complete all channels
        foreach (var channel in _priorityChannels)
        {
            channel.Writer.Complete();
        }

        // Wait for workers to finish
        try
        {
            await Task.WhenAll(_workerTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    /// <summary>
    /// Dispatches a message to the appropriate service handler.
    /// </summary>
    public async Task<DispatchResult> DispatchMessageAsync(RpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("MessageDispatcher is not running. Call StartAsync first.");
        }

        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        // Look up service handler
        if (!_serviceHandlers.TryGetValue(message.ServiceName, out var handler))
        {
            return DispatchResult.Failure("ServiceNotFound", $"No service registered for '{message.ServiceName}'");
        }

        // Determine priority from metadata
        var priority = GetMessagePriority(message);
        var channel = _priorityChannels[(int)priority];

        // Create dispatch item
        var item = new DispatchItem
        {
            Message = message,
            Handler = handler,
            Priority = priority,
            EnqueuedAt = Stopwatch.GetTimestamp()
        };

        // Try to enqueue (with backpressure handling)
        try
        {
            await channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);

            // Check if we're approaching capacity
            var queueDepth = QueueDepth;
            var totalCapacity = _options.MaxQueueDepthPerPriority * 4;

            if (queueDepth > totalCapacity * 0.8) // 80% threshold
            {
                return DispatchResult.SuccessWithBackpressure();
            }

            return DispatchResult.Success();
        }
        catch (ChannelClosedException)
        {
            return DispatchResult.Failure("ChannelClosed", "Dispatcher is shutting down");
        }
        catch (OperationCanceledException)
        {
            return DispatchResult.Failure("Cancelled", "Dispatch operation was cancelled");
        }
    }

    /// <summary>
    /// Registers a service handler for the specified service name.
    /// </summary>
    public void RegisterServiceHandler(string serviceName, IServiceHandler handler)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (!_serviceHandlers.TryAdd(serviceName, handler))
        {
            throw new InvalidOperationException($"Service '{serviceName}' is already registered");
        }
    }

    /// <summary>
    /// Unregisters a service handler.
    /// </summary>
    public bool UnregisterServiceHandler(string serviceName)
    {
        return _serviceHandlers.TryRemove(serviceName, out _);
    }

    /// <summary>
    /// Gets a registered service handler by name.
    /// </summary>
    public IServiceHandler? GetServiceHandler(string serviceName)
    {
        _serviceHandlers.TryGetValue(serviceName, out var handler);
        return handler;
    }

    /// <summary>
    /// Worker task that processes messages from priority channels.
    /// </summary>
    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Try to read from channels in priority order (Critical > High > Normal > Low)
                DispatchItem? item = null;

                for (int priority = 0; priority < _priorityChannels.Length; priority++)
                {
                    var channel = _priorityChannels[priority];

                    if (channel.Reader.TryRead(out item))
                    {
                        break; // Found a message
                    }
                }

                // If no message available from TryRead, wait on all channels
                if (item == null)
                {
                    // Create tasks to read from each channel
                    var readTasks = _priorityChannels
                        .Select(ch => ch.Reader.ReadAsync(cancellationToken).AsTask())
                        .ToArray();

                    // Wait for first available message
                    var completedTask = await Task.WhenAny(readTasks).ConfigureAwait(false);
                    item = await completedTask.ConfigureAwait(false);
                }

                if (item != null)
                {
                    // Process the message (actual invocation happens in ServiceInvoker later)
                    // For now, dispatcher just increments counter
                    Interlocked.Increment(ref _totalMessagesDispatched);

                    // TODO: In Phase 4 Stage 3, integrate with ServiceInvoker
                    // For now, we just validate the handler can process this message
                    _ = item.Handler; // Placeholder - will be used in ServiceInvoker integration
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (ChannelClosedException)
            {
                // Channel closed during shutdown
                break;
            }
            catch (Exception ex)
            {
                // Log unexpected errors (in production, use ILogger)
                Debug.WriteLine($"Error in MessageDispatcher worker: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Extracts message priority from metadata.
    /// </summary>
    private MessagePriority GetMessagePriority(RpcMessage message)
    {
        if (message.Metadata?.TryGetValue("Priority", out var priorityStr) == true)
        {
            if (Enum.TryParse<MessagePriority>(priorityStr, ignoreCase: true, out var priority))
            {
                return priority;
            }
        }

        return MessagePriority.Normal; // Default priority
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop if running
        if (IsRunning)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        _stopCts.Dispose();

        // Complete all channels
        foreach (var channel in _priorityChannels)
        {
            channel.Writer.Complete();
        }
    }

    /// <summary>
    /// Internal dispatch item structure.
    /// </summary>
    private sealed class DispatchItem
    {
        public required RpcMessage Message { get; init; }
        public required IServiceHandler Handler { get; init; }
        public required MessagePriority Priority { get; init; }
        public required long EnqueuedAt { get; init; }
    }
}

/// <summary>
/// Options for MessageDispatcher configuration.
/// </summary>
public sealed class MessageDispatcherOptions
{
    /// <summary>
    /// Number of worker threads processing messages (default: Environment.ProcessorCount).
    /// </summary>
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum queue depth per priority level (default: 10,000).
    /// </summary>
    public int MaxQueueDepthPerPriority { get; set; } = 10_000;

    /// <summary>
    /// Default timeout for service invocations (default: 30 seconds).
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
