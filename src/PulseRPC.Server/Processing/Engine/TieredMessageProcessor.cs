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
