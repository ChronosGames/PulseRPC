using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Memory;
using PulseRPC.Server.Memory;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Scheduling;
using PulseRPC.Transport;
using MessageStatus = PulseRPC.Server.Memory.MessageStatus;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 适配器统计信息
/// </summary>
public class AdapterStatistics
{
    public string ConnectionId { get; set; } = "";

    // 适配器统计
    public long TotalAdapterMessages { get; set; }
    public long TotalConversions { get; set; }

    // TieredProcessor统计
    public PerformanceSummary? TieredProcessorSummary { get; set; }

    // 性能指标
    public double CurrentThroughput { get; set; }
    public TimeSpan AverageBatchProcessingTime { get; set; }
    public TimeSpan P95BatchProcessingTime { get; set; }
    public double L1BackpressureRate { get; set; }
    public double MessageErrorRate { get; set; }
}

/// <summary>
/// 分层消息处理器 - 实现三层缓冲和自适应批处理的高性能消息处理
///
/// L1: 高速无锁环形缓冲区 (ZeroCopyCircularBuffer)
/// L2: 自适应批处理层 (AdaptiveBatchScheduler)
/// L3: 分层内存池 (TieredMemoryPool)
/// </summary>
public sealed class TieredMessageProcessor : IAsyncDisposable
{
    private readonly string _processorId;
    private readonly ILogger<TieredMessageProcessor> _logger;
    private readonly TieredMessageProcessorOptions _options;

    // 三层架构核心组件
    private readonly ZeroCopyCircularBuffer<MessageSlot> _l1Buffer;
    private readonly AdaptiveBatchScheduler _l2Scheduler;
    private readonly TieredMemoryPool _l3MemoryPool;

    // 消息流水线
    private readonly Channel<TieredMessageBatch> _batchChannel;
    private readonly ChannelReader<TieredMessageBatch> _batchReader;
    private readonly ChannelWriter<TieredMessageBatch> _batchWriter;

    // 性能统计
    private readonly TieredProcessorMetrics _metrics;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // 消息处理委托
    private readonly Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> _messageHandler;

    public TieredMessageProcessor(
        string processorId,
        TieredMessageProcessorOptions options,
        Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> messageHandler,
        ILogger<TieredMessageProcessor> logger)
    {
        _processorId = processorId ?? throw new ArgumentNullException(nameof(processorId));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 初始化L1高速缓冲区
        _l1Buffer = new ZeroCopyCircularBuffer<MessageSlot>(_options.L1BufferSize);

        // 初始化L2自适应调度器
        _l2Scheduler = new AdaptiveBatchScheduler();
        _l2Scheduler.RegisterProcessor(new BatchProcessorAdapter(this));

        // 初始化L3内存池
        _l3MemoryPool = TieredMemoryPool.Instance;

        // 初始化批处理通道
        var channelOptions = new BoundedChannelOptions(_options.BatchChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false, // 支持多个batch producer
            AllowSynchronousContinuations = false
        };

        var channel = Channel.CreateBounded<TieredMessageBatch>(channelOptions);
        _batchChannel = channel;
        _batchReader = channel.Reader;
        _batchWriter = channel.Writer;

        // 初始化性能指标
        _metrics = new TieredProcessorMetrics();
        _cancellationTokenSource = new CancellationTokenSource();

        // 启动处理管道
        StartProcessingPipeline();

        _logger.LogInformation("TieredMessageProcessor启动: ProcessorId={ProcessorId}, L1Size={L1Size}, L2Enabled={L2Enabled}",
            _processorId, _options.L1BufferSize, _options.EnableAdaptiveBatching);
    }

    /// <summary>
    /// 尝试将消息入队到L1缓冲区
    /// </summary>
    public bool TryEnqueueMessage(ReadOnlyMemory<byte> messageData, MessagePriority priority = MessagePriority.Normal)
    {
        var slot = CreateMessageSlot(messageData, priority);

        // 尝试快速入队到L1
        if (_l1Buffer.TryEnqueue(slot))
        {
            _metrics.L1MessagesEnqueued.Add(1);

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("消息入队L1成功: ProcessorId={ProcessorId}, Priority={Priority}, Size={Size}",
                    _processorId, priority, messageData.Length);
            }

            return true;
        }

        // L1满了，执行背压策略
        return HandleL1Backpressure(slot);
    }

    /// <summary>
    /// 创建消息槽
    /// </summary>
    private MessageSlot CreateMessageSlot(ReadOnlyMemory<byte> messageData, MessagePriority priority)
    {
        // 从L3内存池租用缓冲区
        var bufferArray = _l3MemoryPool.Rent(messageData.Length);
        messageData.CopyTo(bufferArray.AsMemory());

        // 创建引用计数缓冲区
        var refCountedBuffer = new ReferenceCountedBuffer(bufferArray, buffer => _l3MemoryPool.Return(buffer));

        return new MessageSlot
        {
            MessageId = Guid.NewGuid(),
            Data = refCountedBuffer,
            Priority = priority,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MessageStatus.Pending
        };
    }

    /// <summary>
    /// L1背压处理
    /// </summary>
    private bool HandleL1Backpressure(MessageSlot slot)
    {
        _metrics.L1BackpressureEvents.Add(1);

        switch (slot.Priority)
        {
            case MessagePriority.Critical:
                // 关键消息：等待短时间后强制入队
                return TryForceEnqueue(slot, TimeSpan.FromMicroseconds(_options.CriticalMessageTimeoutUs));

            case MessagePriority.Normal:
                // 普通消息：根据负载进行自适应处理
                if (_metrics.CurrentL1Utilization < _options.NormalMessageDropThreshold)
                {
                    return TryForceEnqueue(slot, TimeSpan.FromMicroseconds(100));
                }
                break;

            case MessagePriority.Low:
                // 低优先级消息：直接丢弃
                _logger.LogDebug("低优先级消息被丢弃: ProcessorId={ProcessorId}", _processorId);
                break;
        }

        // 释放租用的内存 - 检查null和引用计数避免双重释放
        if (slot.Data != null && slot.Data.ReferenceCount > 0)
        {
            slot.Data.Dispose();
        }
        _metrics.MessagesDropped.Add(1);
        return false;
    }

    /// <summary>
    /// 强制入队（用于关键消息）
    /// </summary>
    private bool TryForceEnqueue(MessageSlot slot, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            if (_l1Buffer.TryEnqueue(slot))
            {
                _metrics.L1MessagesEnqueued.Add(1);
                _metrics.ForcedEnqueues.Add(1);
                return true;
            }

            // 短暂yield
            Thread.Yield();
        }

        return false;
    }

    /// <summary>
    /// 启动处理管道
    /// </summary>
    private void StartProcessingPipeline()
    {
        // 启动L2调度器
        if (_options.EnableAdaptiveBatching)
        {
            _l2Scheduler.Start();
        }

        // 启动L1到L2的批处理转移任务
        _ = Task.Run(L1ToL2BatchTransferLoop, _cancellationTokenSource.Token);

        // 启动批处理处理任务
        _ = Task.Run(BatchProcessingLoop, _cancellationTokenSource.Token);
    }

    /// <summary>
    /// L1到L2批处理转移循环
    /// </summary>
    private async Task L1ToL2BatchTransferLoop()
    {
        var batchBuffer = new MessageSlot[_options.MaxBatchSize];

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var batchSize = 0;
                var batchStartTime = Stopwatch.GetTimestamp();

                // 收集批处理
                while (batchSize < batchBuffer.Length && _l1Buffer.TryDequeue(out var slot))
                {
                    batchBuffer[batchSize++] = slot;
                    _metrics.L1MessagesDequeued.Add(1);
                }

                if (batchSize > 0)
                {
                    // 创建批处理
                    var batch = new TieredMessageBatch
                    {
                        BatchId = Guid.NewGuid(),
                        Messages = new ReadOnlyMemory<MessageSlot>(batchBuffer, 0, batchSize),
                        CreateTime = batchStartTime,
                        ProcessorId = _processorId
                    };

                    // 发送到L2处理
                    await _batchWriter.WriteAsync(batch, _cancellationTokenSource.Token);
                    _metrics.BatchesCreated.Add(1);

                    // 记录批处理统计到L2调度器
                    if (_options.EnableAdaptiveBatching)
                    {
                        var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - batchStartTime);
                        var queueDepth = _l1Buffer.GetStatistics().Count;
                        _l2Scheduler.RecordBatchOperation(batchSize, processingTime, queueDepth);
                    }
                }
                else
                {
                    // 没有消息时短暂等待
                    await Task.Delay(1, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "L1到L2批处理转移失败: ProcessorId={ProcessorId}", _processorId);
                await Task.Delay(100, _cancellationTokenSource.Token);
            }
        }
    }

    /// <summary>
    /// 批处理处理循环
    /// </summary>
    private async Task BatchProcessingLoop()
    {
        await foreach (var batch in _batchReader.ReadAllAsync(_cancellationTokenSource.Token))
        {
            try
            {
                await ProcessBatch(batch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批处理处理失败: ProcessorId={ProcessorId}, BatchId={BatchId}",
                    _processorId, batch.BatchId);
                _metrics.BatchesErrored.Add(1);
            }
        }
    }

    /// <summary>
    /// 处理消息批次
    /// </summary>
    private async Task ProcessBatch(TieredMessageBatch batch)
    {
        var batchStartTime = Stopwatch.GetTimestamp();
        var processedCount = 0;
        var errorCount = 0;

        // 并行处理批次中的消息
        var processingTasks = new Task[batch.Messages.Length];
        var messages = batch.Messages;

        for (int i = 0; i < messages.Length; i++)
        {
            var messageSlot = messages.Span[i];
            processingTasks[i] = ProcessMessageSlot(messageSlot);
        }

        // 等待所有消息处理完成
        await Task.WhenAll(processingTasks);

        // 统计处理结果
        foreach (var task in processingTasks)
        {
            if (task.IsCompletedSuccessfully)
            {
                processedCount++;
            }
            else
            {
                errorCount++;
            }
        }

        // 清理内存 - 检查null和引用计数避免双重释放
        for (int i = 0; i < messages.Length; i++)
        {
            var buffer = messages.Span[i].Data;
            // 检查buffer不为null且引用计数大于0时才释放
            if (buffer != null && buffer.ReferenceCount > 0)
            {
                buffer.Dispose();
            }
        }

        // 更新指标
        var batchProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - batchStartTime);
        _metrics.BatchesProcessed.Add(1);
        _metrics.MessagesProcessed.Add(processedCount);
        _metrics.MessagesErrored.Add(errorCount);
        _metrics.RecordBatchProcessingTime(batchProcessingTime);

        if (_options.EnableDetailedLogging)
        {
            _logger.LogDebug("批处理完成: ProcessorId={ProcessorId}, BatchId={BatchId}, " +
                           "Messages={MessageCount}, Processed={ProcessedCount}, Errors={ErrorCount}, " +
                           "ProcessingTime={ProcessingTimeMs}ms",
                _processorId, batch.BatchId, messages.Length, processedCount, errorCount,
                batchProcessingTime.TotalMilliseconds);
        }
    }

    /// <summary>
    /// 处理单个消息槽
    /// </summary>
    private async Task ProcessMessageSlot(MessageSlot slot)
    {
        try
        {
            slot.Status = MessageStatus.Processing;
            var result = await _messageHandler(slot, _cancellationTokenSource.Token);
            slot.Status = result.Success ? MessageStatus.Completed : MessageStatus.Failed;
        }
        catch (Exception ex)
        {
            slot.Status = MessageStatus.Failed;
            _logger.LogWarning(ex, "消息处理失败: ProcessorId={ProcessorId}, MessageId={MessageId}",
                _processorId, slot.MessageId);
        }
    }

    /// <summary>
    /// 获取处理器指标
    /// </summary>
    public TieredProcessorMetrics GetMetrics() => _metrics;

    /// <summary>
    /// 获取处理器状态
    /// </summary>
    public ProcessorStatus GetStatus()
    {
        var l1Stats = _l1Buffer.GetStatistics();
        var l2Metrics = _options.EnableAdaptiveBatching ? _l2Scheduler.GetMetrics() : (SchedulerMetrics?)null;
        var l3Stats = _l3MemoryPool.GetStatistics();

        return new ProcessorStatus
        {
            ProcessorId = _processorId,
            IsRunning = !_cancellationTokenSource.Token.IsCancellationRequested,
            L1BufferUtilization = l1Stats.Utilization,
            L1BufferCount = l1Stats.Count,
            L2CurrentBatchSize = l2Metrics?.CurrentBatchSize ?? 0,
            L2CurrentInterval = l2Metrics?.CurrentBatchInterval ?? 0,
            L3MemoryUtilization = l3Stats.TotalRents > 0 ? (double)l3Stats.TotalReturns / l3Stats.TotalRents : 0,
            TotalMessagesProcessed = _metrics.MessagesProcessed.Value,
            TotalMessagesDropped = _metrics.MessagesDropped.Value,
            CurrentThroughput = _metrics.GetCurrentThroughput()
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("正在关闭TieredMessageProcessor: ProcessorId={ProcessorId}", _processorId);

        // 停止接收新消息
        await _cancellationTokenSource.CancelAsync();

        // 完成批处理通道
        _batchWriter.TryComplete();

        // 等待处理完成
        try
        {
            await _batchReader.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("TieredMessageProcessor关闭超时: ProcessorId={ProcessorId}", _processorId);
        }

        // 停止L2调度器
        if (_options.EnableAdaptiveBatching)
        {
            await _l2Scheduler.StopAsync();
            await _l2Scheduler.DisposeAsync();
        }

        // 清理L1缓冲区中剩余的消息 - 检查null和引用计数避免双重释放
        while (_l1Buffer.TryDequeue(out var slot))
        {
            if (slot.Data != null && slot.Data.ReferenceCount > 0)
            {
                slot.Data.Dispose();
            }
        }

        // 释放L1缓冲区
        _l1Buffer.Dispose();

        // 释放取消令牌
        _cancellationTokenSource.Dispose();

        _logger.LogInformation("TieredMessageProcessor已关闭: ProcessorId={ProcessorId}", _processorId);
    }

    /// <summary>
    /// 批处理器适配器，用于集成AdaptiveBatchScheduler
    /// </summary>
    private class BatchProcessorAdapter : IBatchProcessor
    {
        private readonly TieredMessageProcessor _processor;

        public BatchProcessorAdapter(TieredMessageProcessor processor)
        {
            _processor = processor;
        }

        public void OnParametersUpdated(int newBatchInterval, int newBatchSize)
        {
            // 这里可以动态调整批处理参数
            _processor._logger.LogDebug("批处理参数更新: ProcessorId={ProcessorId}, " +
                                      "NewInterval={NewInterval}ms, NewBatchSize={NewBatchSize}",
                _processor._processorId, newBatchInterval, newBatchSize);
        }
    }
}

/// <summary>
/// 分层消息处理器配置选项
/// </summary>
public class TieredMessageProcessorOptions
{
    public int L1BufferSize { get; set; } = 8192;
    public int MaxBatchSize { get; set; } = 64;
    public int L2MaxBatchSize { get; set; } = 64;
    public int L2QueueCapacity { get; set; } = 256;
    public int BatchChannelCapacity { get; set; } = 256;
    public bool EnableAdaptiveBatching { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;

    // L3内存池配置
    public int L3LargePoolSize { get; set; } = 1024 * 1024; // 1MB
    public int L3MaxPooledBufferSize { get; set; } = 64 * 1024; // 64KB

    // 背压控制
    public double NormalMessageDropThreshold { get; set; } = 0.8; // L1利用率超过80%开始丢弃普通消息
    public double NormalMessageDropRate { get; set; } = 0.8; // 普通消息丢弃率
    public double L1BackpressureThreshold { get; set; } = 0.8; // L1背压阈值
    public int CriticalMessageTimeoutUs { get; set; } = 1000; // 关键消息等待1ms
    public int CriticalMessageTimeoutMs { get; set; } = 1; // 关键消息等待1ms（毫秒版本）
    public int L2BackpressureWaitMs { get; set; } = 1; // L2背压等待时间

    // L2批处理配置
    public int L2BatchIntervalMs { get; set; } = 5; // L2批处理间隔

    // L3内存池详细配置
    public int L3SmallPoolSize { get; set; } = 512 * 1024; // 512KB
    public int L3MediumPoolSize { get; set; } = 2048 * 1024; // 2MB

    // 性能监控
    public int PerformanceCheckFrequency { get; set; } = 10; // 每10个批次检查一次性能
    public int BatchSoftTimeoutMs { get; set; } = 50; // 批处理软超时50ms
    public bool EnablePerformanceMonitoring { get; set; } = true; // 启用性能监控
}

/// <summary>
/// 消息槽
/// </summary>
public struct MessageSlot
{
    public Guid MessageId { get; set; }
    public ReferenceCountedBuffer Data { get; set; }
    public MessagePriority Priority { get; set; }
    public long EnqueueTime { get; set; }
    public MessageStatus Status { get; set; }
}

/// <summary>
/// 消息批次
/// </summary>
public struct TieredMessageBatch
{
    public Guid BatchId { get; set; }
    public ReadOnlyMemory<MessageSlot> Messages { get; set; }
    public long CreateTime { get; set; }
    public string ProcessorId { get; set; }
}


/// <summary>
/// 处理器状态
/// </summary>
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
