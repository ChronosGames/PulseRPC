using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PulseRPC.Diagnostics;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing.Memory;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using MessageStatus = PulseRPC.Server.Processing.Memory.MessageStatus;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>
/// Compatibility statistics retained for the shipped monitoring surface.
/// </summary>
[Obsolete("Per-connection tiered processor statistics are no longer produced by the fixed-shard message engine.", false)]
public class AdapterStatistics
{
    public string ConnectionId { get; set; } = "";
    public long TotalAdapterMessages { get; set; }
    public long TotalConversions { get; set; }
    public PerformanceSummary? TieredProcessorSummary { get; set; }
    public double CurrentThroughput { get; set; }
    public TimeSpan AverageBatchProcessingTime { get; set; }
    public TimeSpan P95BatchProcessingTime { get; set; }
    public double L1BackpressureRate { get; set; }
    public double MessageErrorRate { get; set; }
}

/// <summary>
/// One fixed message-engine worker and its bounded queue.
/// </summary>
internal sealed class MessageWorkerShard : IAsyncDisposable
{
    private readonly string _shardId;
    private readonly int _capacity;
    private readonly Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> _messageHandler;
    private readonly Action<MessageSlot> _messageFinalizer;
    private readonly ILogger _logger;
    private readonly ChannelReader<MessageSlot> _reader;
    private readonly ChannelWriter<MessageSlot> _writer;
    private readonly IRuntimeQueueMetricsRegistration _queueMetrics;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifecycleLock = new();
    private readonly Task _workerTask;

    private Task? _disposeTask;
    private bool _accepting = true;
    private int _stopping;
    private long _processed;
    private long _dropped;

    public MessageWorkerShard(
        string shardId,
        int capacity,
        Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> messageHandler,
        Action<MessageSlot> messageFinalizer,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _shardId = shardId;
        _capacity = capacity;
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _messageFinalizer = messageFinalizer ?? throw new ArgumentNullException(nameof(messageFinalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var channel = Channel.CreateBounded<MessageSlot>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _reader = channel.Reader;
        _writer = channel.Writer;
        _queueMetrics = RuntimeQueueMetrics.Register(
            "message-engine.shard",
            shardId,
            capacity,
            () => _reader.Count);
        _workerTask = Task.Run(ProcessLoopAsync);
    }

    public string ShardId => _shardId;
    public int Capacity => _capacity;
    public int QueueDepth => _reader.Count;
    public double Utilization => (double)QueueDepth / _capacity;
    public bool IsRunning => Volatile.Read(ref _stopping) == 0;
    public long ProcessedCount => Interlocked.Read(ref _processed);
    public long DroppedCount => Interlocked.Read(ref _dropped);
    public CancellationToken ShutdownToken => _shutdown.Token;

    public bool TryEnqueue(MessageSlot slot)
    {
        lock (_lifecycleLock)
        {
            if (!_accepting)
            {
                return false;
            }

            if (_writer.TryWrite(slot))
            {
                _queueMetrics.Observe();
                return true;
            }

            _queueMetrics.RecordRejectedEnqueue();
            return false;
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (await _reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (_reader.TryRead(out var slot))
            {
                try
                {
                    _queueMetrics.Observe();

                    if (Volatile.Read(ref _stopping) != 0 || slot.ConnectionLease is { IsActive: false })
                    {
                        Interlocked.Increment(ref _dropped);
                        FinalizeSlot(slot);
                        continue;
                    }

                    await ProcessSlotAsync(slot).ConfigureAwait(false);
                }
                finally
                {
                    // The async state machine lives for the shard lifetime. Do not let its last
                    // loop local retain a completed connection lease, channel or payload owner.
                    slot = default;
                }
            }
        }
    }

    private async Task ProcessSlotAsync(MessageSlot slot)
    {
        CancellationTokenSource? deadlineCts = null;
        var handlerToken = slot.ConnectionLease?.CancellationToken ?? _shutdown.Token;

        try
        {
            if (handlerToken.IsCancellationRequested)
            {
                slot.Status = MessageStatus.Failed;
                Interlocked.Increment(ref _dropped);
                return;
            }

            var timeoutMs = slot.Header?.TimeoutMs ?? 0;
            if (timeoutMs > 0)
            {
                var remainingMs = timeoutMs - Stopwatch.GetElapsedTime(slot.EnqueueTime).TotalMilliseconds;
                if (remainingMs <= 0)
                {
                    slot.Status = MessageStatus.Failed;
                    Interlocked.Increment(ref _dropped);
                    return;
                }

                deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(handlerToken);
                deadlineCts.CancelAfter(TimeSpan.FromMilliseconds(remainingMs));
                handlerToken = deadlineCts.Token;
            }

            slot.Status = MessageStatus.Processing;
            var result = await _messageHandler(slot, handlerToken).ConfigureAwait(false);
            slot.Status = result.Success ? MessageStatus.Completed : MessageStatus.Failed;
            Interlocked.Increment(ref _processed);
        }
        catch (OperationCanceledException) when (handlerToken.IsCancellationRequested)
        {
            slot.Status = MessageStatus.Failed;
            Interlocked.Increment(ref _dropped);
        }
        catch (Exception ex)
        {
            slot.Status = MessageStatus.Failed;
            Interlocked.Increment(ref _dropped);
            SafeLog(() => _logger.LogWarning(ex,
                "消息 shard 处理失败: ShardId={ShardId}, MessageId={MessageId}",
                _shardId,
                slot.MessageId));
        }
        finally
        {
            deadlineCts?.Dispose();
            FinalizeSlot(slot);
        }
    }

    private void FinalizeSlot(MessageSlot slot)
    {
        try
        {
            slot.PayloadOwner?.Dispose();
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex,
                "归还消息载荷失败: ShardId={ShardId}, MessageId={MessageId}",
                _shardId,
                slot.MessageId));
        }

        try
        {
            _messageFinalizer(slot);
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex,
                "执行消息终结清理失败: ShardId={ShardId}, MessageId={MessageId}",
                _shardId,
                slot.MessageId));
        }

        try
        {
            slot.ConnectionLease?.Release();
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex,
                "释放连接消息租约失败: ShardId={ShardId}, MessageId={MessageId}",
                _shardId,
                slot.MessageId));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask == null)
            {
                _accepting = false;
                Volatile.Write(ref _stopping, 1);
                _writer.TryComplete();
                _disposeTask = DisposeCoreAsync();
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(
                ex,
                "取消消息 shard 时回调异常: ShardId={ShardId}",
                _shardId));
        }

        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(
                ex,
                "消息 shard worker 异常退出: ShardId={ShardId}",
                _shardId));
        }

        while (_reader.TryRead(out var slot))
        {
            Interlocked.Increment(ref _dropped);
            FinalizeSlot(slot);
        }

        _queueMetrics.Dispose();
        _shutdown.Dispose();
    }

    private static void SafeLog(Action logAction)
    {
        try
        {
            logAction();
        }
        catch
        {
            // Payload, lease, worker and queue cleanup must not depend on a logger provider.
        }
    }
}

/// <summary>
/// Generation-scoped connection state shared by queued messages.
/// </summary>
internal sealed class MessageConnectionLease
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly ILogger _logger;
    private Task? _deactivateTask;
    private int _pendingMessages;
    private int _active = 1;
    private bool _cancellationCompleted;
    private bool _disposed;

    public MessageConnectionLease(
        string connectionId,
        int shardIndex,
        CancellationToken shardToken,
        ILogger logger,
        IServerChannel? channel = null)
    {
        ConnectionId = connectionId;
        ShardIndex = shardIndex;
        Channel = channel;
        _logger = logger;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shardToken);
    }

    public string ConnectionId { get; }
    public int ShardIndex { get; }
    public IServerChannel? Channel { get; }
    public bool IsActive => Volatile.Read(ref _active) != 0;
    public CancellationToken CancellationToken => _cancellation.Token;
    internal int PendingMessageCount
    {
        get
        {
            lock (_gate)
            {
                return _pendingMessages;
            }
        }
    }

    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    public bool TryAcquire()
    {
        lock (_gate)
        {
            if (_active == 0)
            {
                return false;
            }

            _pendingMessages++;
            return true;
        }
    }

    public void Release()
    {
        var dispose = false;
        lock (_gate)
        {
            if (_pendingMessages <= 0)
            {
                throw new InvalidOperationException("连接消息租约被重复释放。");
            }

            _pendingMessages--;
            if (_active == 0 &&
                _pendingMessages == 0 &&
                _cancellationCompleted &&
                !_disposed)
            {
                _disposed = true;
                dispose = true;
            }
        }

        if (dispose)
        {
            _cancellation.Dispose();
        }
    }

    public Task DeactivateAsync()
    {
        lock (_gate)
        {
            if (_deactivateTask != null)
            {
                return _deactivateTask;
            }

            Volatile.Write(ref _active, 0);
            _deactivateTask = DeactivateCoreAsync();
            return _deactivateTask;
        }
    }

    private async Task DeactivateCoreAsync()
    {
        await Task.Yield();

        try
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                _logger.LogError(ex,
                    "取消连接消息租约时回调异常: ConnectionId={ConnectionId}",
                    ConnectionId);
            }
            catch
            {
                // Cancellation completion and CTS disposal must continue.
            }
        }

        var dispose = false;
        lock (_gate)
        {
            _cancellationCompleted = true;
            if (_pendingMessages == 0 && !_disposed)
            {
                _disposed = true;
                dispose = true;
            }
        }

        if (dispose)
        {
            _cancellation.Dispose();
        }
    }
}

/// <summary>
/// Legacy per-connection processor options. The fixed-shard engine does not consume this type.
/// </summary>
[Obsolete("This options model is not used. Configure MessageWorkerShardCount and MessageQueueCapacityPerShard on PulseServerOptions.", false)]
public class TieredMessageProcessorOptions
{
    public int L1BufferSize { get; set; } = 8192;
    public int MaxBatchSize { get; set; } = 64;
    public int L2MaxBatchSize { get; set; } = 64;
    public int L2QueueCapacity { get; set; } = 256;
    public int BatchChannelCapacity { get; set; } = 256;
    public bool EnableAdaptiveBatching { get; set; } = true;
    public bool EnableDetailedLogging { get; set; }
    public int L3LargePoolSize { get; set; } = 1024 * 1024;
    public int L3MaxPooledBufferSize { get; set; } = 64 * 1024;
    public double NormalMessageDropThreshold { get; set; } = 0.8;
    public double NormalMessageDropRate { get; set; } = 0.8;
    public double L1BackpressureThreshold { get; set; } = 0.8;
    public int CriticalMessageTimeoutUs { get; set; } = 1000;
    public int CriticalMessageTimeoutMs { get; set; } = 1;
    public int L2BackpressureWaitMs { get; set; } = 1;
    public int L2BatchIntervalMs { get; set; } = 5;
    public int L3SmallPoolSize { get; set; } = 512 * 1024;
    public int L3MediumPoolSize { get; set; } = 2048 * 1024;
    public int PerformanceCheckFrequency { get; set; } = 10;
    public int BatchSoftTimeoutMs { get; set; } = 50;
    public bool EnablePerformanceMonitoring { get; set; } = true;
}

/// <summary>
/// Message payload and metadata queued by the server message engine.
/// </summary>
public struct MessageSlot
{
    public Guid MessageId { get; set; }
    public string ConnectionId { get; set; }
    public MessageHeader Header { get; set; }
    public ReadOnlyMemory<byte> Payload { get; set; }
    public IDisposable? PayloadOwner { get; set; }
    public MessagePriority Priority { get; set; }
    public long EnqueueTime { get; set; }
    public MessageStatus Status { get; set; }

    internal MessageConnectionLease? ConnectionLease { get; set; }
}

/// <summary>
/// Legacy batch DTO retained for binary compatibility.
/// </summary>
[Obsolete("The fixed-shard message engine does not create message batches.", false)]
public struct TieredMessageBatch
{
    public long BatchId { get; set; }
    public ReadOnlyMemory<MessageSlot> Messages { get; set; }
    public long CreateTime { get; set; }
    public string ProcessorId { get; set; }
}

/// <summary>
/// Legacy processor status retained for binary compatibility.
/// </summary>
[Obsolete("Per-connection processor status is no longer produced. Use EngineStatistics and RuntimeQueueMetrics.", false)]
public class ProcessorStatus
{
    public required string ProcessorId { get; set; }
    public bool IsRunning { get; set; }
    public double L1BufferUtilization { get; set; }
    public int L1BufferCount { get; set; }
    public int L2CurrentBatchSize { get; set; }
    public int L2CurrentInterval { get; set; }
    public double L3MemoryUtilization { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public double CurrentThroughput { get; set; }
}
