using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Memory;
using PulseRPC.Messaging;
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
internal sealed class TieredMessageProcessor : IAsyncDisposable
{
    private readonly string _processorId;
    private readonly ILogger _logger;
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

    public DateTime ConnectedAt
    {
        get;
        private set;
    }

    public TieredMessageProcessor(
        string processorId,
        TieredMessageProcessorOptions options,
        Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> messageHandler,
        ILogger logger)
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

        ConnectedAt = DateTime.UtcNow;

        // 启动处理管道
        StartProcessingPipeline();

        _logger.LogInformation("TieredMessageProcessor启动: ProcessorId={ProcessorId}, L1Size={L1Size}, L2Enabled={L2Enabled}",
            _processorId, _options.L1BufferSize, _options.EnableAdaptiveBatching);
    }

    /// <summary>
    /// 尝试将消息槽入队到L1缓冲区（新方法）
    /// </summary>
    public bool TryEnqueueMessageSlot(MessageSlot slot)
    {
        // 直接入队 MessageSlot，无需再创建
        if (_l1Buffer.TryEnqueue(slot))
        {
            _metrics.L1MessagesEnqueued.Add(1);

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("消息入队L1成功: ProcessorId={ProcessorId}, MessageId={MessageId}, Priority={Priority}, ConnectionId={ConnectionId}",
                    _processorId, slot.MessageId, slot.Priority, slot.ConnectionId);
            }

            return true;
        }

        // L1满了，返回false让调用者处理背压
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
    /// L1到L2批处理转移循环 - 优化为基于信号的等待机制，避免轮询
    /// </summary>
    private async Task L1ToL2BatchTransferLoop()
    {
        var batchBuffer = new MessageSlot[_options.MaxBatchSize];
        // .NET 9 优化：使用 PeriodicTimer 替代 Task.Delay 以获得更精确的时序和更低的开销
        using var batchTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(1));

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var batchSize = 0;
                var batchStartTime = Stopwatch.GetTimestamp();

                // 首先尝试快速收集一批消息
                while (batchSize < batchBuffer.Length && _l1Buffer.TryDequeue(out var slot))
                {
                    batchBuffer[batchSize++] = slot;
                    _metrics.L1MessagesDequeued.Add(1);
                }

                if (batchSize > 0)
                {
                    // 创建批处理（使用ArrayPool减少内存分配）
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
                    // 没有消息时使用 PeriodicTimer 等待，更高效且精确
                    await batchTimer.WaitForNextTickAsync(_cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "L1到L2批处理转移失败: ProcessorId={ProcessorId}", _processorId);
                await batchTimer.WaitForNextTickAsync(_cancellationTokenSource.Token);
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

        // 使用零拷贝方式，无需手动清理内存
        // ReadOnlyMemory<byte> 会自动管理生命周期

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

        // 清理L1缓冲区中剩余的消息
        // 使用零拷贝方式，ReadOnlyMemory<byte> 会自动管理生命周期
        while (_l1Buffer.TryDequeue(out var slot))
        {
            // 无需手动清理
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
/// 消息槽 - 包含完整元数据的消息结构
/// </summary>
public struct MessageSlot
{
    /// <summary>
    /// 消息唯一标识符
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 真实连接ID
    /// </summary>
    public string ConnectionId { get; set; }

    /// <summary>
    /// 完整消息头部
    /// </summary>
    public MessageHeader Header { get; set; }

    /// <summary>
    /// 消息负载数据（零拷贝）
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; set; }

    /// <summary>
    /// 消息优先级
    /// </summary>
    public MessagePriority Priority { get; set; }

    /// <summary>
    /// 入队时间戳
    /// </summary>
    public long EnqueueTime { get; set; }

    /// <summary>
    /// 消息状态
    /// </summary>
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
