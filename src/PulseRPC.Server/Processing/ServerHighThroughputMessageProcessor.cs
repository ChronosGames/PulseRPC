using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Transport;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Processing;

/// <summary>
/// 服务端高吞吐量消息处理器 - 基于三级缓冲架构优化网络I/O性能
/// </summary>
public class ServerHighThroughputMessageProcessor : IAsyncDisposable
{
    private readonly string _connectionId;
    private readonly IServerChannel _serverChannel;
    private readonly HighThroughputProcessorOptions _options;
    private readonly IMessageHandlerRegistry _handlerRegistry;
    private readonly ILogger _logger;

    // 三级缓冲架构
    private readonly LockFreeRingBuffer<ServerMessageSlot> _l1Buffer;
    private readonly ChannelReader<ServerMessageBatch> _l2BatchQueue;
    private readonly ChannelWriter<ServerMessageBatch> _l2BatchWriter;
    private readonly ChannelReader<ServerResponseBatch> _l3ResponseQueue;
    private readonly ChannelWriter<ServerResponseBatch> _l3ResponseWriter;

    private readonly Timer _batchTimer;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // 性能统计计数器
    private long _messagesInL1;
    private long _messagesInL2;
    private long _messagesInL3;
    private long _totalMessagesProcessed;
    private long _totalMessagesDropped;
    private long _totalCriticalMessagesForced;

    // 数组池优化
    private static readonly ArrayPool<ServerMessageSlot> MessageSlotPool = ArrayPool<ServerMessageSlot>.Shared;
    private static readonly ArrayPool<ServerMessageResponse> MessageResponsePool = ArrayPool<ServerMessageResponse>.Shared;

    public ServerHighThroughputMessageProcessor(
        string connectionId,
        PulseRPC.Server.Transport.IServerChannel serverChannel,
        IMessageHandlerRegistry handlerRegistry,
        IOptions<HighThroughputProcessorOptions> options,
        ILogger<ServerHighThroughputMessageProcessor> logger)
    {
        _connectionId = connectionId;
        _serverChannel = serverChannel;
        _handlerRegistry = handlerRegistry;
        _options = options.Value;
        _logger = logger;

        // 初始化L1无锁环形缓冲区
        _l1Buffer = new LockFreeRingBuffer<ServerMessageSlot>(_options.L1BufferSize);

        // 初始化L2批量处理队列
        var l2Options = new BoundedChannelOptions(_options.L2QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        };
        var l2Channel = Channel.CreateBounded<ServerMessageBatch>(l2Options);
        _l2BatchQueue = l2Channel.Reader;
        _l2BatchWriter = l2Channel.Writer;

        // 初始化L3响应发送队列
        var l3Options = new BoundedChannelOptions(_options.L3QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        };
        var l3Channel = Channel.CreateBounded<ServerResponseBatch>(l3Options);
        _l3ResponseQueue = l3Channel.Reader;
        _l3ResponseWriter = l3Channel.Writer;

        _cancellationTokenSource = new CancellationTokenSource();

        // 启动批处理定时器
        _batchTimer = new Timer(ProcessL1ToL2Batch, null,
            TimeSpan.FromMilliseconds(_options.BatchIntervalMs),
            TimeSpan.FromMilliseconds(_options.BatchIntervalMs));

        // 启动处理管道
        _ = Task.Run(() => ProcessL2MessagesAsync(_cancellationTokenSource.Token));
        _ = Task.Run(() => ProcessL3ResponsesAsync(_cancellationTokenSource.Token));

        _logger.LogInformation("高吞吐量消息处理器已启动: ConnectionId={ConnectionId}, L1Size={L1Size}, BatchInterval={BatchInterval}ms",
            _connectionId, _options.L1BufferSize, _options.BatchIntervalMs);
    }

    /// <summary>
    /// 尝试将消息入队 - IO线程调用，必须极速返回
    /// </summary>
    public bool TryEnqueueMessage(ServerMessage message)
    {
        var slot = new ServerMessageSlot
        {
            Message = message,
            SequenceId = message.SequenceId,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = ServerMessageStatus.Pending,
            Priority = message.Priority
        };

        // 无锁快速投递到L1缓冲区
        var success = _l1Buffer.TryEnqueue(slot, TimeSpan.Zero);

        if (success)
        {
            Interlocked.Increment(ref _messagesInL1);
            if (_options.EnableDetailedLogging)
            {
                _logger.LogDebug("消息已入队L1: ConnectionId={ConnectionId}, SequenceId={SequenceId}, Priority={Priority}",
                    _connectionId, message.SequenceId, message.Priority);
            }
        }
        else
        {
            // L1满了，执行背压策略
            success = HandleBackpressure(message);
        }

        return success;
    }

    /// <summary>
    /// 背压处理 - 智能丢弃策略
    /// </summary>
    private bool HandleBackpressure(ServerMessage message)
    {
        switch (message.Priority)
        {
            case MessagePriority.Critical:
                // 关键消息：尝试强制插入
                Interlocked.Increment(ref _totalCriticalMessagesForced);
                return ForceEnqueueCriticalMessage(message);

            case MessagePriority.Normal:
                // 普通消息：根据配置的概率丢弃
                if (Random.Shared.NextDouble() < _options.NormalMessageDropRate)
                {
                    Interlocked.Increment(ref _totalMessagesDropped);
                    _logger.LogDebug("背压丢弃普通消息: ConnectionId={ConnectionId}, SequenceId={SequenceId}",
                        _connectionId, message.SequenceId);
                    _ = SendErrorResponseAsync(message.SequenceId, "SERVER_BUSY", "服务器繁忙，请稍后重试");
                    return false;
                }
                // 允许短时等待
                return TryEnqueueWithTimeout(message, TimeSpan.FromMicroseconds(100));

            case MessagePriority.Low:
                // 低优先级消息：直接丢弃
                Interlocked.Increment(ref _totalMessagesDropped);
                _logger.LogDebug("背压丢弃低优先级消息: ConnectionId={ConnectionId}, SequenceId={SequenceId}",
                    _connectionId, message.SequenceId);
                _ = SendErrorResponseAsync(message.SequenceId, "DROPPED", "消息被丢弃");
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// 强制入队关键消息
    /// </summary>
    private bool ForceEnqueueCriticalMessage(ServerMessage message)
    {
        var slot = new ServerMessageSlot
        {
            Message = message,
            SequenceId = message.SequenceId,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = ServerMessageStatus.Critical,
            Priority = message.Priority
        };

        return _l1Buffer.TryEnqueue(slot, TimeSpan.FromMicroseconds(_options.CriticalMessageTimeoutUs));
    }

    /// <summary>
    /// 带超时的入队尝试
    /// </summary>
    private bool TryEnqueueWithTimeout(ServerMessage message, TimeSpan timeout)
    {
        var slot = new ServerMessageSlot
        {
            Message = message,
            SequenceId = message.SequenceId,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = ServerMessageStatus.Pending,
            Priority = message.Priority
        };

        return _l1Buffer.TryEnqueue(slot, timeout);
    }

    /// <summary>
    /// L1→L2批量转移（定时器触发）
    /// </summary>
    private void ProcessL1ToL2Batch(object? state)
    {
        try
        {
            var batchArray = MessageSlotPool.Rent(_options.MaxBatchSize);
            int count = 0;

            // 从L1缓冲区批量读取
            while (count < batchArray.Length && _l1Buffer.TryDequeue(out var slot))
            {
                batchArray[count++] = slot;
                Interlocked.Decrement(ref _messagesInL1);
            }

            if (count > 0)
            {
                var messageBatch = new ServerMessageBatch
                {
                    Messages = new ArraySegment<ServerMessageSlot>(batchArray, 0, count),
                    BatchId = Guid.NewGuid().ToString(),
                    CreateTime = Stopwatch.GetTimestamp(),
                    RentedArray = batchArray,
                    ConnectionId = _connectionId
                };

                if (_l2BatchWriter.TryWrite(messageBatch))
                {
                    Interlocked.Add(ref _messagesInL2, count);
                    if (_options.EnableDetailedLogging)
                    {
                        _logger.LogDebug("L1→L2批量转移: ConnectionId={ConnectionId}, BatchSize={Count}",
                            _connectionId, count);
                    }
                }
                else
                {
                    // L2队列满，执行紧急背压
                    HandleL2Backpressure(messageBatch);
                }
            }
            else
            {
                MessageSlotPool.Return(batchArray);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "L1到L2批量处理失败: ConnectionId={ConnectionId}", _connectionId);
        }
    }

    /// <summary>
    /// L2背压处理
    /// </summary>
    private void HandleL2Backpressure(ServerMessageBatch batch)
    {
        _logger.LogWarning("L2队列满，执行紧急背压: ConnectionId={ConnectionId}, BatchSize={BatchSize}",
            _connectionId, batch.Messages.Count);

        var criticalCount = 0;
        var normalCount = 0;

        // 统计消息类型
        for (int i = 0; i < batch.Messages.Count; i++)
        {
            if (batch.Messages[i].Status == ServerMessageStatus.Critical)
                criticalCount++;
            else
                normalCount++;
        }

        // 丢弃普通消息，保留关键消息
        if (normalCount > 0)
        {
            Interlocked.Add(ref _totalMessagesDropped, normalCount);

            // 异步发送错误响应（不等待完成）
            _ = Task.Run(async () =>
            {
                var errorTasks = new List<Task>(normalCount);
                for (int i = 0; i < batch.Messages.Count; i++)
                {
                    if (batch.Messages[i].Status != ServerMessageStatus.Critical)
                    {
                        var msg = batch.Messages[i];
                        errorTasks.Add(SendErrorResponseAsync(msg.SequenceId, "SERVER_OVERLOAD", "服务器过载"));
                    }
                }

                try
                {
                    await Task.WhenAll(errorTasks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送背压错误响应失败: ConnectionId={ConnectionId}", _connectionId);
                }
            });
        }

        // 关键消息重新打包，强制入队
        if (criticalCount > 0)
        {
            var criticalBatch = CreateCriticalMessageBatch(batch);
            try
            {
                _l2BatchWriter.WriteAsync(criticalBatch).AsTask().Wait(TimeSpan.FromMilliseconds(_options.L2BackpressureWaitMs));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关键消息强制入队失败: ConnectionId={ConnectionId}", _connectionId);
                MessageSlotPool.Return(batch.RentedArray!);
            }
        }
        else
        {
            MessageSlotPool.Return(batch.RentedArray!);
        }
    }

    /// <summary>
    /// 创建关键消息批次
    /// </summary>
    private ServerMessageBatch CreateCriticalMessageBatch(ServerMessageBatch originalBatch)
    {
        var criticalArray = MessageSlotPool.Rent(originalBatch.Messages.Count);
        int criticalCount = 0;

        for (int i = 0; i < originalBatch.Messages.Count; i++)
        {
            if (originalBatch.Messages[i].Status == ServerMessageStatus.Critical)
            {
                criticalArray[criticalCount++] = originalBatch.Messages[i];
            }
        }

        // 归还原数组
        MessageSlotPool.Return(originalBatch.RentedArray!);

        return new ServerMessageBatch
        {
            Messages = new ArraySegment<ServerMessageSlot>(criticalArray, 0, criticalCount),
            BatchId = originalBatch.BatchId + "-critical",
            CreateTime = originalBatch.CreateTime,
            RentedArray = criticalArray,
            ConnectionId = _connectionId
        };
    }

    /// <summary>
    /// L2消息批量处理
    /// </summary>
    private async Task ProcessL2MessagesAsync(CancellationToken cancellationToken)
    {
        while (await _l2BatchQueue.WaitToReadAsync(cancellationToken))
        {
            while (_l2BatchQueue.TryRead(out var batch))
            {
                Interlocked.Add(ref _messagesInL2, -batch.Messages.Count);

                try
                {
                    await ProcessMessageBatch(batch);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "批量消息处理失败: ConnectionId={ConnectionId}, BatchId={BatchId}",
                        _connectionId, batch.BatchId);
                }
                finally
                {
                    if (batch.RentedArray != null)
                    {
                        MessageSlotPool.Return(batch.RentedArray);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 处理消息批次
    /// </summary>
    private async Task ProcessMessageBatch(ServerMessageBatch batch)
    {
        var responseArray = MessageResponsePool.Rent(batch.Messages.Count);
        int responseCount = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < batch.Messages.Count; i++)
            {
                var slot = batch.Messages[i];
                var messageStopwatch = Stopwatch.StartNew();

                try
                {
                    slot.Status = ServerMessageStatus.Processing;

                    // 执行消息处理逻辑
                    var result = await ProcessSingleMessage(slot.Message);

                    slot.Status = ServerMessageStatus.Completed;
                    messageStopwatch.Stop();

                    responseArray[responseCount++] = new ServerMessageResponse
                    {
                        SequenceId = slot.SequenceId,
                        Success = true,
                        Data = result,
                        ProcessingTime = messageStopwatch.Elapsed
                    };

                    // 软超时检查
                    if (responseCount % _options.PerformanceCheckFrequency == 0 &&
                        stopwatch.Elapsed > TimeSpan.FromMilliseconds(_options.BatchSoftTimeoutMs))
                    {
                        _logger.LogWarning("批量处理性能瓶颈: ConnectionId={ConnectionId}, BatchId={BatchId}, Processed={ProcessedCount}, Elapsed={ElapsedMs}ms",
                            _connectionId, batch.BatchId, responseCount, stopwatch.Elapsed.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    slot.Status = ServerMessageStatus.Failed;
                    messageStopwatch.Stop();

                    _logger.LogError(ex, "消息处理失败: ConnectionId={ConnectionId}, SequenceId={SequenceId}",
                        _connectionId, slot.SequenceId);

                    responseArray[responseCount++] = new ServerMessageResponse
                    {
                        SequenceId = slot.SequenceId,
                        Success = false,
                        ErrorCode = "PROCESSING_ERROR",
                        ErrorMessage = ex.Message,
                        ProcessingTime = messageStopwatch.Elapsed
                    };
                }
            }

            stopwatch.Stop();

            // 批量发送响应
            if (responseCount > 0)
            {
                Interlocked.Add(ref _totalMessagesProcessed, responseCount);

                var responseBatch = new ServerResponseBatch
                {
                    BatchId = batch.BatchId,
                    Responses = new ArraySegment<ServerMessageResponse>(responseArray, 0, responseCount),
                    TotalProcessingTime = stopwatch.Elapsed,
                    RentedArray = responseArray,
                    ConnectionId = _connectionId
                };

                await _l3ResponseWriter.WriteAsync(responseBatch);
                Interlocked.Add(ref _messagesInL3, responseCount);
            }
            else
            {
                MessageResponsePool.Return(responseArray);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息批次时发生异常: ConnectionId={ConnectionId}, BatchId={BatchId}",
                _connectionId, batch.BatchId);
            MessageResponsePool.Return(responseArray);
        }
    }

    /// <summary>
    /// 处理单个消息
    /// </summary>
    private async Task<object?> ProcessSingleMessage(ServerMessage message)
    {
        // 这里应该调用具体的消息处理逻辑
        // 暂时返回简单的回显
        return await Task.FromResult($"Echo: {message.GetType().Name}");
    }

    /// <summary>
    /// L3批量响应发送
    /// </summary>
    private async Task ProcessL3ResponsesAsync(CancellationToken cancellationToken)
    {
        while (await _l3ResponseQueue.WaitToReadAsync(cancellationToken))
        {
            while (_l3ResponseQueue.TryRead(out var responseBatch))
            {
                Interlocked.Add(ref _messagesInL3, -responseBatch.Responses.Count);

                try
                {
                    // 批量发送响应
                    await SendResponseBatchAsync(responseBatch.Responses);

                    if (_options.EnableDetailedLogging)
                    {
                        _logger.LogDebug("批量响应发送完成: ConnectionId={ConnectionId}, BatchId={BatchId}, Count={Count}, ProcessingTime={ProcessingTimeMs:F2}ms",
                            _connectionId, responseBatch.BatchId, responseBatch.Responses.Count, responseBatch.TotalProcessingTime.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "批量响应发送失败: ConnectionId={ConnectionId}, BatchId={BatchId}",
                        _connectionId, responseBatch.BatchId);

                    // 回退到单个发送
                    await FallbackToIndividualSend(responseBatch.Responses);
                }
                finally
                {
                    if (responseBatch.RentedArray != null)
                    {
                        MessageResponsePool.Return(responseBatch.RentedArray);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 发送错误响应
    /// </summary>
    private async Task SendErrorResponseAsync(long sequenceId, string errorCode, string errorMessage)
    {
        try
        {
            var errorResponse = new ServerMessageResponse
            {
                SequenceId = sequenceId,
                Success = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            };

            await SendSingleResponseAsync(errorResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送错误响应失败: ConnectionId={ConnectionId}, SequenceId={SequenceId}",
                _connectionId, sequenceId);
        }
    }

    /// <summary>
    /// 批量发送响应
    /// </summary>
    private async Task SendResponseBatchAsync(ArraySegment<ServerMessageResponse> responses)
    {
        // 这里应该实现批量网络发送逻辑
        // 当前简化为逐个发送
        var tasks = new List<Task>(responses.Count);

        for (int i = 0; i < responses.Count; i++)
        {
            var response = responses.Array![responses.Offset + i];
            tasks.Add(SendSingleResponseAsync(response));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 回退到单个发送
    /// </summary>
    private async Task FallbackToIndividualSend(ArraySegment<ServerMessageResponse> responses)
    {
        var tasks = new List<Task>(responses.Count);

        for (int i = 0; i < responses.Count; i++)
        {
            var response = responses.Array![responses.Offset + i];
            tasks.Add(SafeSendResponse(response));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 安全发送单个响应
    /// </summary>
    private async Task SafeSendResponse(ServerMessageResponse response)
    {
        try
        {
            await SendSingleResponseAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "单独响应发送失败: ConnectionId={ConnectionId}, SequenceId={SequenceId}",
                _connectionId, response.SequenceId);
        }
    }

    /// <summary>
    /// 发送单个响应
    /// </summary>
    private async Task SendSingleResponseAsync(ServerMessageResponse response)
    {
        // 这里应该实现具体的网络发送逻辑
        // 当前为模拟实现
        await _serverChannel.SendAsync(SerializeResponse(response));
    }

    /// <summary>
    /// 序列化响应
    /// </summary>
    private ReadOnlyMemory<byte> SerializeResponse(ServerMessageResponse response)
    {
        // 这里应该实现具体的序列化逻辑
        // 当前返回空的内存块
        return ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>
    /// 获取队列统计信息
    /// </summary>
    public ProcessorStats GetStats()
    {
        return new ProcessorStats
        {
            MessagesInL1 = Interlocked.Read(ref _messagesInL1),
            MessagesInL2 = Interlocked.Read(ref _messagesInL2),
            MessagesInL3 = Interlocked.Read(ref _messagesInL3),
            TotalProcessed = Interlocked.Read(ref _totalMessagesProcessed),
            TotalDropped = Interlocked.Read(ref _totalMessagesDropped),
            TotalCriticalForced = Interlocked.Read(ref _totalCriticalMessagesForced)
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _batchTimer.DisposeAsync();
        await _cancellationTokenSource.CancelAsync();

        _l2BatchWriter.TryComplete();
        _l3ResponseWriter.TryComplete();

        try
        {
            await Task.WhenAll(
                _l2BatchQueue.Completion,
                _l3ResponseQueue.Completion
            ).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("处理器关闭超时: ConnectionId={ConnectionId}", _connectionId);
        }

        _cancellationTokenSource.Dispose();

        _logger.LogInformation("高吞吐量消息处理器已释放: ConnectionId={ConnectionId}", _connectionId);
    }
}

// 支持数据结构
public class ServerMessageBatch
{
    public ArraySegment<ServerMessageSlot> Messages { get; set; }
    public string BatchId { get; set; } = "";
    public long CreateTime { get; set; }
    public ServerMessageSlot[]? RentedArray { get; set; }
    public string ConnectionId { get; set; } = "";
}

public class ServerResponseBatch
{
    public ArraySegment<ServerMessageResponse> Responses { get; set; }
    public string BatchId { get; set; } = "";
    public TimeSpan TotalProcessingTime { get; set; }
    public ServerMessageResponse[]? RentedArray { get; set; }
    public string ConnectionId { get; set; } = "";
}

public struct ServerMessageSlot
{
    public ServerMessage Message;
    public long SequenceId;
    public long EnqueueTime;
    public ServerMessageStatus Status;
    public MessagePriority Priority;
}

public enum ServerMessageStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Critical
}

public class ServerMessageResponse
{
    public long SequenceId { get; set; }
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

public abstract class ServerMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public long SequenceId { get; set; }
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public DateTime ServerTimestamp { get; set; }
}

public class ProcessorStats
{
    public long MessagesInL1 { get; set; }
    public long MessagesInL2 { get; set; }
    public long MessagesInL3 { get; set; }
    public long TotalProcessed { get; set; }
    public long TotalDropped { get; set; }
    public long TotalCriticalForced { get; set; }
}

// 服务端消息处理注册表接口
public interface IMessageHandlerRegistry
{
    Task<object?> HandleAsync(ServerMessage message);
}
