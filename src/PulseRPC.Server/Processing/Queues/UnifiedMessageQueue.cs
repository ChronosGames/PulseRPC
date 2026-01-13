using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Processing.Queues;

/// <summary>
/// 统一消息队列 - Pure FIFO，高性能串行执行
/// </summary>
public sealed class UnifiedMessageQueue : IAsyncDisposable
{
    private readonly string _queueId;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;

    // 消息队列（单通道 FIFO）
    private readonly Channel<Func<CancellationToken, ValueTask>> _messageQueue;

    // 执行线程
    private Task? _executionTask;

    // 统计指标（本地计数器，避免 Interlocked 开销）
    private long _localTotalMessages;
    private long _localMessagesCompleted;

    // 线程安全的读取字段
    private long _totalMessages;
    private long _messagesCompleted;

    public UnifiedMessageQueue(
        string queueId,
        ILogger logger,
        int capacity = -1)
    {
        _queueId = queueId ?? throw new ArgumentNullException(nameof(queueId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();

        _messageQueue = capacity > 0
            ? Channel.CreateBounded<Func<CancellationToken, ValueTask>>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            })
            : Channel.CreateUnbounded<Func<CancellationToken, ValueTask>>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        _logger.LogDebug(
            "UnifiedMessageQueue created - QueueId: {QueueId}, Capacity: {Capacity}",
            queueId, capacity > 0 ? capacity : "Unbounded");
    }

    public void Start()
    {
        if (_executionTask != null)
            throw new InvalidOperationException("Queue already started");

        _executionTask = Task.Run(ExecutionLoop, _cts.Token);
    }

    public async ValueTask<bool> EnqueueAsync(
        Func<CancellationToken, ValueTask> messageHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageHandler);

        try
        {
            await _messageQueue.Writer.WriteAsync(messageHandler, cancellationToken);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ExecutionLoop()
    {
        try
        {
            // 使用优化的 ReadAllAsync - .NET 内部高度优化的路径
            await foreach (var messageHandler in _messageQueue.Reader.ReadAllAsync(_cts.Token))
            {
                _localTotalMessages++;

                try
                {
                    await messageHandler(_cts.Token);
                    _localMessagesCompleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing message in queue {QueueId}", _queueId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposing
        }
        finally
        {
            // 刷新本地计数器到线程安全字段
            FlushMetrics();
        }
    }

    private void FlushMetrics()
    {
        Interlocked.Add(ref _totalMessages, _localTotalMessages);
        Interlocked.Add(ref _messagesCompleted, _localMessagesCompleted);
        _localTotalMessages = 0;
        _localMessagesCompleted = 0;
    }

    public QueueMetrics GetMetrics()
    {
        // 临时刷新以获取最新数据
        FlushMetrics();

        return new QueueMetrics
        {
            QueueId = _queueId,
            TotalMessages = Interlocked.Read(ref _totalMessages),
            MessagesCompleted = Interlocked.Read(ref _messagesCompleted),
            PendingQueueDepth = _messageQueue.Reader.Count
        };
    }

    public async ValueTask DisposeAsync()
    {
        _messageQueue.Writer.Complete();
        await _cts.CancelAsync();

        if (_executionTask != null)
        {
            try
            {
                await _executionTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cts.Dispose();
    }
}

public sealed class QueueMetrics
{
    public string QueueId { get; init; } = "";
    public long TotalMessages { get; init; }
    public long MessagesCompleted { get; init; }
    public int PendingQueueDepth { get; init; }
}
