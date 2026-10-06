using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Diagnostics;
using PulseRPC.Server.Processing.Memory;
using PulseRPC.Shared;
using MessageStatus = PulseRPC.Server.Processing.Memory.MessageStatus;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>Bounded ingress with round-robin connection lanes and bounded execution.</summary>
internal sealed class MessageWorkerShard : IAsyncDisposable
{
    private sealed class ConnectionLane
    {
        internal readonly object Key;
        internal readonly Queue<MessageSlot> Pending = new();
        internal int Active;
        internal bool Ready;
        internal ConnectionLane(object key) => Key = key;
    }

    private readonly string _shardId;
    private readonly int _capacity;
    private readonly int _maxConcurrency;
    private readonly int _maxConcurrencyPerConnection;
    private readonly Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> _messageHandler;
    private readonly Action<MessageSlot> _messageFinalizer;
    private readonly ILogger _logger;
    private readonly IRuntimeQueueMetricsRegistration _queueMetrics;
    private readonly Dictionary<object, ConnectionLane> _lanes = new();
    private readonly Queue<ConnectionLane> _ready = new();
    private readonly SemaphoreSlim _workAvailable = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifecycleLock = new();
    private readonly Task _workerTask;
    private Task? _disposeTask;
    private bool _accepting = true;
    private int _stopping;
    private int _queued;
    private int _inFlight;
    private long _processed;
    private long _dropped;

    public MessageWorkerShard(string shardId, int capacity,
        Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> messageHandler,
        Action<MessageSlot> messageFinalizer, ILogger logger,
        int maxConcurrency = 1, int maxConcurrencyPerConnection = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrencyPerConnection, 1);
        _shardId = shardId;
        _capacity = capacity;
        _maxConcurrency = maxConcurrency;
        _maxConcurrencyPerConnection = Math.Min(maxConcurrencyPerConnection, maxConcurrency);
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _messageFinalizer = messageFinalizer ?? throw new ArgumentNullException(nameof(messageFinalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueMetrics = RuntimeQueueMetrics.Register("message-engine.shard", shardId, capacity, () => QueueDepth);
        _workerTask = Task.Run(ProcessLoopAsync);
    }

    public string ShardId => _shardId;
    public int Capacity => _capacity;
    public int QueueDepth => Volatile.Read(ref _queued);
    public double Utilization => (double)QueueDepth / _capacity;
    public bool IsRunning => Volatile.Read(ref _stopping) == 0;
    public long ProcessedCount => Interlocked.Read(ref _processed);
    public long DroppedCount => Interlocked.Read(ref _dropped);
    public CancellationToken ShutdownToken => _shutdown.Token;
    internal int InFlightCount => Volatile.Read(ref _inFlight);

    public bool TryEnqueue(MessageSlot slot)
    {
        lock (_lifecycleLock)
        {
            if (!_accepting) return false;
            if (_queued >= _capacity)
            {
                _queueMetrics.RecordRejectedEnqueue();
                return false;
            }
            // Physical connection generations must never share a lane with replacements.
            object key = slot.ConnectionLease ?? (object?)slot.ConnectionId ?? string.Empty;
            if (!_lanes.TryGetValue(key, out var lane))
                _lanes.Add(key, lane = new ConnectionLane(key));
            lane.Pending.Enqueue(slot);
            _queued++;
            MakeReady(lane);
            _queueMetrics.Observe();
            SignalWork();
            return true;
        }
    }

    // All queue, lane and signal transitions take place under _lifecycleLock.
    private void MakeReady(ConnectionLane lane)
    {
        if (_stopping == 0 && !lane.Ready && lane.Pending.Count > 0 && lane.Active < _maxConcurrencyPerConnection)
        {
            lane.Ready = true;
            _ready.Enqueue(lane);
        }
    }

    private void SignalWork()
    {
        if (_workAvailable.CurrentCount == 0) _workAvailable.Release();
    }

    private bool TryTake(out MessageSlot slot, out ConnectionLane? lane)
    {
        lock (_lifecycleLock)
        {
            slot = default;
            lane = null;
            if (_stopping != 0 || _inFlight >= _maxConcurrency || _ready.Count == 0) return false;
            lane = _ready.Dequeue();
            lane.Ready = false;
            slot = lane.Pending.Dequeue();
            _queued--;
            _inFlight++;
            lane.Active++;
            MakeReady(lane);
            _queueMetrics.Observe();
            return true;
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (true)
        {
            await _workAvailable.WaitAsync().ConfigureAwait(false);
            while (TryTake(out var slot, out var lane))
            {
                Dispatch(slot, lane!);
                // The shard lives for the host lifetime; do not retain a completed payload.
                slot = default;
                lane = null;
            }
            if (Volatile.Read(ref _stopping) != 0)
            {
                DropBacklog();
                lock (_lifecycleLock)
                {
                    if (_inFlight == 0) return;
                }
            }
        }
    }

    private void Dispatch(MessageSlot slot, ConnectionLane lane)
    {
        // A synchronous/CPU-heavy handler must not block admission for other connection lanes.
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessSlotAsync(slot).ConfigureAwait(false);
            }
            finally
            {
                // ProcessSlotAsync owns payload finalization through actual handler completion.
                slot = default;
                lock (_lifecycleLock)
                {
                    _inFlight--;
                    lane.Active--;
                    if (lane.Active == 0 && lane.Pending.Count == 0) _lanes.Remove(lane.Key);
                    else MakeReady(lane);
                    lane = null!;
                    SignalWork();
                }
            }
        });
    }

    private void DropBacklog()
    {
        List<MessageSlot> dropped;
        lock (_lifecycleLock)
        {
            dropped = new List<MessageSlot>(_queued);
            foreach (var lane in _lanes.Values)
            {
                while (lane.Pending.TryDequeue(out var slot)) dropped.Add(slot);
                lane.Ready = false;
            }
            _ready.Clear();
            _queued = 0;
            foreach (var key in _lanes.Where(pair => pair.Value.Active == 0).Select(pair => pair.Key).ToArray())
                _lanes.Remove(key);
            _queueMetrics.Observe();
        }
        foreach (var slot in dropped)
        {
            Interlocked.Increment(ref _dropped);
            FinalizeSlot(slot);
        }
    }

    private async Task ProcessSlotAsync(MessageSlot slot)
    {
        CancellationTokenSource? deadlineCts = null;
        var handlerToken = slot.ConnectionLease?.CancellationToken ?? _shutdown.Token;

        try
        {
            if (Volatile.Read(ref _stopping) != 0 || slot.ConnectionLease is { IsActive: false }
                || handlerToken.IsCancellationRequested)
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
                SignalWork();
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

        _workAvailable.Dispose();
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
