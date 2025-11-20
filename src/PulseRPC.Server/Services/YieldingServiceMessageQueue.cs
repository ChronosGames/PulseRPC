using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// 队列项类型 - 统一消息和延续
/// </summary>
internal abstract class QueueItem
{
    public abstract Task ExecuteAsync(Func<ServiceMessage, Task> messageHandler);
}

/// <summary>
/// 消息队列项
/// </summary>
internal sealed class MessageQueueItem : QueueItem
{
    public ServiceMessage Message { get; }

    public MessageQueueItem(ServiceMessage message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public override async Task ExecuteAsync(Func<ServiceMessage, Task> messageHandler)
    {
        await messageHandler(Message);
    }
}

/// <summary>
/// 延续队列项
/// </summary>
internal sealed class ContinuationQueueItem : QueueItem
{
    public SendOrPostCallback Callback { get; }
    public object? State { get; }

    public ContinuationQueueItem(SendOrPostCallback callback, object? state)
    {
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        State = state;
    }

    public override Task ExecuteAsync(Func<ServiceMessage, Task> messageHandler)
    {
        // 延续直接执行，不需要 messageHandler
        Callback(State);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 支持让出的消息队列
/// </summary>
/// <remarks>
/// 核心设计：
/// 1. 统一队列处理消息和延续
/// 2. 设置自定义 SynchronizationContext，拦截 await 的延续
/// 3. 延续自动重新排队，恢复执行时仍在队列线程
/// 4. 保证 Actor 模型的线程安全性
/// </remarks>
public sealed class YieldingServiceMessageQueue : IAsyncDisposable
{
    private readonly Channel<QueueItem> _queue;
    private readonly ServiceSynchronizationContext _syncContext;
    private readonly string _serviceName;
    private readonly PID _servicePID;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;
    private ChannelWriter<(SendOrPostCallback, object?)>? _continuationWriter;

    // 统计指标
    private long _totalMessages;
    private long _totalContinuations;
    private long _messagesProcessed;
    private long _continuationsProcessed;

    public YieldingServiceMessageQueue(
        string serviceName,
        PID servicePID,
        ILogger logger,
        int capacity = -1)
    {
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _servicePID = servicePID;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();

        // 创建统一队列
        if (capacity > 0)
        {
            _queue = Channel.CreateBounded<QueueItem>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }
        else
        {
            _queue = Channel.CreateUnbounded<QueueItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        // 创建自定义同步上下文
        _syncContext = new ServiceSynchronizationContext(
            CreateContinuationWriter(),
            serviceName,
            servicePID,
            logger);

        _logger.LogDebug(
            "YieldingServiceMessageQueue created - Service: {ServiceName}, PID: {PID}, Capacity: {Capacity}",
            serviceName, servicePID, capacity > 0 ? capacity : "Unbounded");
    }

    /// <summary>
    /// 创建延续写入器 - 将延续包装为队列项
    /// </summary>
    private ChannelWriter<(SendOrPostCallback, object?)> CreateContinuationWriter()
    {
        // 创建一个转换管道
        var continuationChannel = Channel.CreateUnbounded<(SendOrPostCallback, object?)>();

        // 保存引用以便在 Dispose 时关闭
        _continuationWriter = continuationChannel.Writer;

        // 后台任务：将延续转换为队列项
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var (callback, state) in continuationChannel.Reader.ReadAllAsync())
                {
                    Interlocked.Increment(ref _totalContinuations);

                    var item = new ContinuationQueueItem(callback, state);

                    // ✅ 使用 TryWrite 避免死锁（UnboundedChannel 应该总是成功）
                    if (!_queue.Writer.TryWrite(item))
                    {
                        // 如果失败，记录警告但不阻塞
                        _logger.LogWarning(
                            "Failed to write continuation to main queue - Service: {ServiceName}",
                            _serviceName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in continuation channel processing");
            }
        });

        return continuationChannel.Writer;
    }

    /// <summary>
    /// 启动消息处理循环
    /// </summary>
    public void Start(Func<ServiceMessage, Task> messageHandler)
    {
        ArgumentNullException.ThrowIfNull(messageHandler);

        if (_processingTask != null)
            throw new InvalidOperationException("Queue already started");

        _processingTask = Task.Run(async () =>
        {
            // ✅ 设置自定义同步上下文 - 这是让出机制的关键
            SynchronizationContext.SetSynchronizationContext(_syncContext);

            _logger.LogInformation(
                "YieldingServiceMessageQueue started - Service: {ServiceName}, PID: {PID}",
                _serviceName, _servicePID);

            await ProcessQueueAsync(messageHandler, _cts.Token);

            _logger.LogInformation(
                "YieldingServiceMessageQueue stopped - Service: {ServiceName}, PID: {PID}",
                _serviceName, _servicePID);
        });
    }

    /// <summary>
    /// 处理队列循环
    /// </summary>
    private async Task ProcessQueueAsync(
        Func<ServiceMessage, Task> messageHandler,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (item is MessageQueueItem messageItem)
                    {
                        Interlocked.Increment(ref _messagesProcessed);

                        _logger.LogTrace(
                            "Processing message: {MessageType} - Service: {ServiceName}",
                            messageItem.Message.GetType().Name, _serviceName);

                        // ✅ 执行消息处理（await 会让出队列）
                        await item.ExecuteAsync(messageHandler);

                        _logger.LogTrace(
                            "Message processed: {MessageType} - Service: {ServiceName}",
                            messageItem.Message.GetType().Name, _serviceName);
                    }
                    else if (item is ContinuationQueueItem continuationItem)
                    {
                        Interlocked.Increment(ref _continuationsProcessed);

                        _logger.LogTrace(
                            "Processing continuation - Service: {ServiceName}",
                            _serviceName);

                        // ✅ 执行延续（恢复 await 后的代码）
                        await item.ExecuteAsync(messageHandler);

                        _logger.LogTrace(
                            "Continuation processed - Service: {ServiceName}",
                            _serviceName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error processing queue item - Service: {ServiceName}, PID: {PID}, ItemType: {ItemType}",
                        _serviceName, _servicePID, item.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Queue processing cancelled - Service: {ServiceName}", _serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Fatal error in queue processing - Service: {ServiceName}, PID: {PID}",
                _serviceName, _servicePID);
        }
    }

    /// <summary>
    /// 发送消息到队列
    /// </summary>
    public async ValueTask<bool> SendMessageAsync(ServiceMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Interlocked.Increment(ref _totalMessages);

        var item = new MessageQueueItem(message);

        try
        {
            await _queue.Writer.WriteAsync(item, cancellationToken);
            return true;
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning(
                "Cannot send message to closed queue - Service: {ServiceName}, PID: {PID}",
                _serviceName, _servicePID);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "Message send cancelled - Service: {ServiceName}, PID: {PID}",
                _serviceName, _servicePID);
            return false;
        }
    }

    /// <summary>
    /// 获取队列深度
    /// </summary>
    /// <remarks>
    /// 注意：UnboundedChannel 不支持 Count 属性，会返回 -1
    /// </remarks>
    public int GetCurrentQueueDepth()
    {
        try
        {
            return _queue.Reader.Count;
        }
        catch (NotSupportedException)
        {
            // UnboundedChannel 不支持 Count
            return -1;
        }
    }

    /// <summary>
    /// 获取监控指标
    /// </summary>
    public YieldingQueueMetrics GetMetrics()
    {
        return new YieldingQueueMetrics
        {
            TotalMessages = Interlocked.Read(ref _totalMessages),
            TotalContinuations = Interlocked.Read(ref _totalContinuations),
            MessagesProcessed = Interlocked.Read(ref _messagesProcessed),
            ContinuationsProcessed = Interlocked.Read(ref _continuationsProcessed),
            CurrentQueueDepth = GetCurrentQueueDepth()
        };
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug(
            "Disposing YieldingServiceMessageQueue - Service: {ServiceName}, PID: {PID}",
            _serviceName, _servicePID);

        // 停止接收新延续
        _continuationWriter?.Complete();

        // 停止接收新消息
        _queue.Writer.Complete();

        // 等待处理任务完成（先让队列处理完所有消息）
        if (_processingTask != null)
        {
            try
            {
                await _processingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // 取消处理任务（已经完成了，但还是取消一下）
        await _cts.CancelAsync();
        _cts.Dispose();

        var metrics = GetMetrics();
        _logger.LogInformation(
            "YieldingServiceMessageQueue disposed - Service: {ServiceName}, PID: {PID}, " +
            "TotalMessages: {TotalMessages}, TotalContinuations: {TotalContinuations}, " +
            "MessagesProcessed: {MessagesProcessed}, ContinuationsProcessed: {ContinuationsProcessed}",
            _serviceName, _servicePID,
            metrics.TotalMessages, metrics.TotalContinuations,
            metrics.MessagesProcessed, metrics.ContinuationsProcessed);
    }
}

/// <summary>
/// 让出队列的监控指标
/// </summary>
public sealed class YieldingQueueMetrics
{
    /// <summary>总消息数</summary>
    public long TotalMessages { get; init; }

    /// <summary>总延续数</summary>
    public long TotalContinuations { get; init; }

    /// <summary>已处理消息数</summary>
    public long MessagesProcessed { get; init; }

    /// <summary>已处理延续数</summary>
    public long ContinuationsProcessed { get; init; }

    /// <summary>当前队列深度</summary>
    public int CurrentQueueDepth { get; init; }

    /// <summary>平均每消息的延续数</summary>
    public double AverageContinuationsPerMessage =>
        TotalMessages > 0 ? (double)TotalContinuations / TotalMessages : 0;
}
