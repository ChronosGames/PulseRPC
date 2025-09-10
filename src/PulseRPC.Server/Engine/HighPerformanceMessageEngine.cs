using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Memory;
using PulseRPC.Server.Memory;
using PulseRPC.Server.Processing;
using PulseRPC.Transport;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 高性能消息引擎 - 替代ServerHighThroughputMessageProcessor
/// 基于零拷贝缓冲区、自适应调度和分层内存管理的新一代消息处理引擎
/// </summary>
public sealed class HighPerformanceMessageEngine : IAsyncDisposable, IBatchProcessor
{
    #region 技术规格常量
    
    /// <summary>
    /// 引擎技术规格
    /// </summary>
    public static class Specifications
    {
        public const int L1_BUFFER_SIZE = 4096;           // L1缓冲区大小 (2^12)
        public const int L2_QUEUE_CAPACITY = 256;        // L2队列容量
        public const int L3_QUEUE_CAPACITY = 128;        // L3队列容量
        public const int MIN_BATCH_INTERVAL_MS = 1;      // 最小批处理间隔
        public const int MAX_BATCH_INTERVAL_MS = 10;     // 最大批处理间隔
        public const int ADAPTIVE_BATCH_SIZE_MIN = 8;    // 最小批大小
        public const int ADAPTIVE_BATCH_SIZE_MAX = 128;  // 最大批大小
    }
    
    /// <summary>
    /// 性能要求
    /// </summary>
    public static class PerformanceRequirements
    {
        public const int MAX_L1_ENQUEUE_NS = 100;        // L1入队最大100纳秒
        public const int MAX_BATCH_PROCESS_MS = 5;       // 批处理最大5毫秒
        public const double MIN_CACHE_HIT_RATIO = 0.95;  // 最小缓存命中率95%
        public const int MAX_GC_PRESSURE_MB_PER_SEC = 10; // 最大GC压力10MB/s
    }
    
    #endregion
    
    #region 字段和属性
    
    private readonly string _connectionId;
    private readonly object _serverChannel; // 临时占位符，需要实际的IServerChannel接口
    private readonly MessageEngineConfiguration _options;
    private readonly IMessageHandlerRegistry _handlerRegistry;
    private readonly ILogger<HighPerformanceMessageEngine> _logger;
    
    // 核心组件
    private readonly ZeroCopyCircularBuffer<MessageEnvelope> _l1Buffer;
    private readonly AdaptiveBatchScheduler _adaptiveBatchScheduler;
    private readonly TieredMemoryPool _memoryPool;
    
    // L2和L3处理管道
    private readonly ChannelReader<MessageBatch> _l2BatchQueue;
    private readonly ChannelWriter<MessageBatch> _l2BatchWriter;
    private readonly ChannelReader<ResponseBatch> _l3ResponseQueue;
    private readonly ChannelWriter<ResponseBatch> _l3ResponseWriter;
    
    // 控制和监控
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly EngineStatistics _statistics = new();
    
    // 状态管理
    private volatile bool _isRunning;
    private Task? _processingTask;
    private Task? _responseTask;
    
    #endregion
    
    #region 构造函数和初始化
    
    /// <summary>
    /// 构造高性能消息引擎
    /// </summary>
    /// <param name="connectionId">连接标识</param>
    /// <param name="serverChannel">服务端通道</param>
    /// <param name="handlerRegistry">消息处理器注册表</param>
    /// <param name="options">引擎配置选项</param>
    /// <param name="logger">日志记录器</param>
    public HighPerformanceMessageEngine(
        string connectionId,
        object serverChannel, // 临时占位符
        IMessageHandlerRegistry handlerRegistry,
        IOptions<MessageEngineConfiguration> options,
        ILogger<HighPerformanceMessageEngine> logger)
    {
        _connectionId = connectionId;
        _serverChannel = serverChannel;
        _handlerRegistry = handlerRegistry;
        _options = options.Value;
        _logger = logger;
        
        // 初始化核心组件
        _l1Buffer = new ZeroCopyCircularBuffer<MessageEnvelope>(Specifications.L1_BUFFER_SIZE);
        // 传递主日志记录器给AdaptiveBatchScheduler使用
        _adaptiveBatchScheduler = new AdaptiveBatchScheduler(
            logger as ILogger<AdaptiveBatchScheduler> ?? 
            new NullLogger<AdaptiveBatchScheduler>());
        _memoryPool = TieredMemoryPool.Instance;
        
        // 初始化处理管道
        var l2Options = new BoundedChannelOptions(Specifications.L2_QUEUE_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        };
        var l2Channel = Channel.CreateBounded<MessageBatch>(l2Options);
        _l2BatchQueue = l2Channel.Reader;
        _l2BatchWriter = l2Channel.Writer;
        
        var l3Options = new BoundedChannelOptions(Specifications.L3_QUEUE_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        };
        var l3Channel = Channel.CreateBounded<ResponseBatch>(l3Options);
        _l3ResponseQueue = l3Channel.Reader;
        _l3ResponseWriter = l3Channel.Writer;
        
        _cancellationTokenSource = new CancellationTokenSource();
        
        // 注册到自适应调度器
        _adaptiveBatchScheduler.RegisterProcessor(this);
        
        _logger.LogInformation("高性能消息引擎已初始化：连接ID={ConnectionId}, L1大小={L1Size}", 
            _connectionId, Specifications.L1_BUFFER_SIZE);
    }
    
    #endregion
    
    #region 核心消息处理API
    
    /// <summary>
    /// 启动消息引擎
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            _logger.LogWarning("消息引擎已经在运行中：{ConnectionId}", _connectionId);
            return;
        }
        
        _isRunning = true;
        
        // 启动自适应调度器
        _adaptiveBatchScheduler.Start();
        
        // 启动处理管道
        _processingTask = Task.Run(() => ProcessL2MessagesAsync(_cancellationTokenSource.Token));
        _responseTask = Task.Run(() => ProcessL3ResponsesAsync(_cancellationTokenSource.Token));
        
        _logger.LogInformation("高性能消息引擎已启动：{ConnectionId}", _connectionId);
    }
    
    /// <summary>
    /// 尝试入队消息 - IO线程调用，必须极速返回
    /// </summary>
    /// <param name="message">消息实例</param>
    /// <returns>true表示成功入队，false表示缓冲区满或其他错误</returns>
    public bool TryEnqueueMessage(ServerMessage message)
    {
        var startTicks = Stopwatch.GetTimestamp();
        
        var envelope = new MessageEnvelope
        {
            Message = message,
            SequenceId = message.SequenceId,
            EnqueueTime = startTicks,
            Status = MessageStatus.Pending,
            Priority = message.Priority
        };
        
        // 零拷贝快速入队到L1缓冲区
        var success = _l1Buffer.TryEnqueue(in envelope);
        
        if (success)
        {
            Interlocked.Increment(ref _statistics._messagesInL1);
            Interlocked.Increment(ref _statistics._totalEnqueued);
            
            // 性能统计：确保入队操作在100纳秒内完成
            var elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            var elapsedNs = (elapsedTicks * 1_000_000_000) / Stopwatch.Frequency;
            
            if (elapsedNs > PerformanceRequirements.MAX_L1_ENQUEUE_NS)
            {
                _logger.LogWarning("L1入队性能警告：{ElapsedNs}ns > {MaxNs}ns，连接ID={ConnectionId}", 
                    elapsedNs, PerformanceRequirements.MAX_L1_ENQUEUE_NS, _connectionId);
            }
            
            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("消息已入队L1：连接ID={ConnectionId}, 序列号={SequenceId}, 优先级={Priority}",
                    _connectionId, message.SequenceId, message.Priority);
            }
        }
        else
        {
            // L1满了，执行智能背压策略
            success = HandleBackpressure(message);
            
            if (!success)
            {
                Interlocked.Increment(ref _statistics._totalDropped);
            }
        }
        
        return success;
    }
    
    /// <summary>
    /// 获取引擎统计信息
    /// </summary>
    public MessageEngineStatistics GetStatistics()
    {
        var bufferStats = _l1Buffer.GetStatistics();
        var schedulerMetrics = _adaptiveBatchScheduler.GetMetrics();
        var memoryStats = _memoryPool.GetStatistics();
        
        return new MessageEngineStatistics
        {
            ConnectionId = _connectionId,
            IsRunning = _isRunning,
            
            // L1缓冲区统计
            L1Capacity = bufferStats.Capacity,
            L1Count = bufferStats.Count,
            L1Utilization = bufferStats.Utilization,
            
            // 消息处理统计
            TotalEnqueued = _statistics._totalEnqueued,
            TotalProcessed = _statistics._totalProcessed,
            TotalDropped = _statistics._totalDropped,
            MessagesInL1 = _statistics._messagesInL1,
            MessagesInL2 = _statistics._messagesInL2,
            MessagesInL3 = _statistics._messagesInL3,
            
            // 自适应调度统计
            CurrentBatchInterval = schedulerMetrics.CurrentBatchInterval,
            CurrentBatchSize = schedulerMetrics.CurrentBatchSize,
            AverageThroughput = schedulerMetrics.AverageThroughput,
            AverageLatency = schedulerMetrics.AverageLatency,
            P95Latency = schedulerMetrics.P95Latency,
            
            // 内存池统计
            MemoryPoolHitRatio = memoryStats.CacheHitRatio,
            TotalMemoryAllocated = memoryStats.TotalAllocatedBytes
        };
    }
    
    #endregion
    
    #region 智能背压处理
    
    /// <summary>
    /// 处理背压情况的智能策略
    /// </summary>
    private bool HandleBackpressure(ServerMessage message)
    {
        switch (message.Priority)
        {
            case MessagePriority.Critical:
                return HandleCriticalMessageBackpressure(message);
                
            case MessagePriority.Normal:
                return HandleNormalMessageBackpressure(message);
                
            case MessagePriority.Low:
                return HandleLowPriorityMessageBackpressure(message);
                
            default:
                return false;
        }
    }
    
    /// <summary>
    /// 处理关键消息背压
    /// </summary>
    private bool HandleCriticalMessageBackpressure(ServerMessage message)
    {
        // 关键消息：尝试短时等待强制入队
        var envelope = new MessageEnvelope
        {
            Message = message,
            SequenceId = message.SequenceId,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MessageStatus.Critical,
            Priority = message.Priority
        };
        
        var success = _l1Buffer.TryEnqueue(in envelope, TimeSpan.FromMicroseconds(100));
        
        if (success)
        {
            Interlocked.Increment(ref _statistics._criticalForced);
        }
        else
        {
            _logger.LogError("关键消息强制入队失败：连接ID={ConnectionId}, 序列号={SequenceId}", 
                _connectionId, message.SequenceId);
        }
        
        return success;
    }
    
    /// <summary>
    /// 处理普通消息背压
    /// </summary>
    private bool HandleNormalMessageBackpressure(ServerMessage message)
    {
        // 普通消息：根据配置的丢弃率决定是否丢弃
        if (Random.Shared.NextDouble() < _options.NormalMessageDropRate)
        {
            _logger.LogDebug("背压丢弃普通消息：连接ID={ConnectionId}, 序列号={SequenceId}",
                _connectionId, message.SequenceId);
            
            _ = SendErrorResponseAsync(message.SequenceId, "SERVER_BUSY", "服务器繁忙，请稍后重试");
            return false;
        }
        
        // 允许短时等待
        var envelope = new MessageEnvelope
        {
            Message = message,
            SequenceId = message.SequenceId,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MessageStatus.Pending,
            Priority = message.Priority
        };
        
        return _l1Buffer.TryEnqueue(in envelope, TimeSpan.FromMicroseconds(50));
    }
    
    /// <summary>
    /// 处理低优先级消息背压
    /// </summary>
    private bool HandleLowPriorityMessageBackpressure(ServerMessage message)
    {
        // 低优先级消息：直接丢弃
        _logger.LogDebug("背压丢弃低优先级消息：连接ID={ConnectionId}, 序列号={SequenceId}",
            _connectionId, message.SequenceId);
        
        _ = SendErrorResponseAsync(message.SequenceId, "DROPPED", "消息被丢弃");
        return false;
    }
    
    #endregion
    
    #region L2消息批量处理
    
    /// <summary>
    /// L2消息批量处理主循环
    /// </summary>
    private async Task ProcessL2MessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("L2消息处理循环已启动：{ConnectionId}", _connectionId);
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 从L1缓冲区批量读取消息
                await ProcessL1ToL2Batch();
                
                // 处理L2队列中的批次
                while (_l2BatchQueue.TryRead(out var batch))
                {
                    Interlocked.Add(ref _statistics._messagesInL2, -batch.Messages.Length);
                    await ProcessMessageBatch(batch);
                }
                
                // 短暂等待，避免忙等待
                await Task.Delay(1, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不记录错误
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "L2消息处理循环异常：{ConnectionId}", _connectionId);
        }
        
        _logger.LogDebug("L2消息处理循环已退出：{ConnectionId}", _connectionId);
    }
    
    /// <summary>
    /// 从L1到L2的批量转移
    /// </summary>
    private async Task ProcessL1ToL2Batch()
    {
        var batchSize = _adaptiveBatchScheduler.CurrentBatchSize;
        var messageBuffer = _memoryPool.Rent(batchSize * Unsafe.SizeOf<MessageEnvelope>());
        
        try
        {
            // 从L1缓冲区批量读取
            var span = MemoryMarshal.Cast<byte, MessageEnvelope>(messageBuffer.AsSpan());
            var readCount = _l1Buffer.TryDequeueBatch(span, batchSize);
            
            if (readCount > 0)
            {
                Interlocked.Add(ref _statistics._messagesInL1, -readCount);
                
                var batch = new MessageBatch
                {
                    Messages = span.Slice(0, readCount).ToArray(),
                    BatchId = Guid.NewGuid().ToString(),
                    CreateTime = Stopwatch.GetTimestamp(),
                    ConnectionId = _connectionId
                };
                
                if (_l2BatchWriter.TryWrite(batch))
                {
                    Interlocked.Add(ref _statistics._messagesInL2, readCount);
                    
                    // 记录批处理性能指标
                    _adaptiveBatchScheduler.RecordBatchOperation(
                        readCount, 
                        TimeSpan.FromTicks(Stopwatch.GetTimestamp() - batch.CreateTime),
                        _l1Buffer.Count);
                }
                else
                {
                    await HandleL2Backpressure(batch);
                }
            }
        }
        finally
        {
            _memoryPool.Return(messageBuffer);
        }
    }
    
    /// <summary>
    /// 处理单个消息批次
    /// </summary>
    private async Task ProcessMessageBatch(MessageBatch batch)
    {
        var stopwatch = Stopwatch.StartNew();
        var responses = new List<MessageResponse>(batch.Messages.Length);
        
        try
        {
            // 顺序处理批次内的消息
            foreach (var envelope in batch.Messages)
            {
                var messageStopwatch = Stopwatch.StartNew();
                
                try
                {
                    // 执行消息处理逻辑
                    var result = await _handlerRegistry.HandleAsync(envelope.Message);
                    
                    responses.Add(new MessageResponse
                    {
                        SequenceId = envelope.SequenceId,
                        Success = true,
                        Data = result,
                        ProcessingTime = messageStopwatch.Elapsed
                    });
                    
                    Interlocked.Increment(ref _statistics._totalProcessed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "消息处理失败：连接ID={ConnectionId}, 序列号={SequenceId}",
                        _connectionId, envelope.SequenceId);
                    
                    responses.Add(new MessageResponse
                    {
                        SequenceId = envelope.SequenceId,
                        Success = false,
                        ErrorCode = "PROCESSING_ERROR",
                        ErrorMessage = ex.Message,
                        ProcessingTime = messageStopwatch.Elapsed
                    });
                }
                
                // 软超时检查
                if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(PerformanceRequirements.MAX_BATCH_PROCESS_MS))
                {
                    _logger.LogWarning("批处理性能瓶颈：连接ID={ConnectionId}, 批次ID={BatchId}, 已处理={ProcessedCount}, 耗时={ElapsedMs}ms",
                        _connectionId, batch.BatchId, responses.Count, stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            
            // 批量发送响应
            if (responses.Count > 0)
            {
                var responseBatch = new ResponseBatch
                {
                    BatchId = batch.BatchId,
                    Responses = responses.ToArray(),
                    TotalProcessingTime = stopwatch.Elapsed,
                    ConnectionId = _connectionId
                };
                
                await _l3ResponseWriter.WriteAsync(responseBatch);
                Interlocked.Add(ref _statistics._messagesInL3, responses.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息批次异常：连接ID={ConnectionId}, 批次ID={BatchId}",
                _connectionId, batch.BatchId);
        }
    }
    
    #endregion
    
    #region L3响应处理
    
    /// <summary>
    /// L3响应处理主循环
    /// </summary>
    private async Task ProcessL3ResponsesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("L3响应处理循环已启动：{ConnectionId}", _connectionId);
        
        try
        {
            while (await _l3ResponseQueue.WaitToReadAsync(cancellationToken))
            {
                while (_l3ResponseQueue.TryRead(out var responseBatch))
                {
                    Interlocked.Add(ref _statistics._messagesInL3, -responseBatch.Responses.Length);
                    
                    try
                    {
                        await SendResponseBatchAsync(responseBatch.Responses);
                        
                        if (_options.EnableDetailedLogging)
                        {
                            _logger.LogTrace("批量响应发送完成：连接ID={ConnectionId}, 批次ID={BatchId}, 数量={Count}, 处理时间={ProcessingTimeMs:F2}ms",
                                _connectionId, responseBatch.BatchId, responseBatch.Responses.Length, responseBatch.TotalProcessingTime.TotalMilliseconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "批量响应发送失败：连接ID={ConnectionId}, 批次ID={BatchId}",
                            _connectionId, responseBatch.BatchId);
                        
                        await FallbackToIndividualSend(responseBatch.Responses);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不记录错误
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "L3响应处理循环异常：{ConnectionId}", _connectionId);
        }
        
        _logger.LogDebug("L3响应处理循环已退出：{ConnectionId}", _connectionId);
    }
    
    /// <summary>
    /// 批量发送响应
    /// </summary>
    private async Task SendResponseBatchAsync(MessageResponse[] responses)
    {
        // 实现批量网络发送逻辑
        // 当前简化为并行发送
        var tasks = new Task[responses.Length];
        
        for (int i = 0; i < responses.Length; i++)
        {
            tasks[i] = SendSingleResponseAsync(responses[i]);
        }
        
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// 发送单个响应
    /// </summary>
    private async Task SendSingleResponseAsync(MessageResponse response)
    {
        try
        {
            var serializedResponse = SerializeResponse(response);
            // 临时占位符实现 - 实际需要通过IServerChannel发送
            await Task.CompletedTask;
            
            _logger.LogTrace("响应已发送：连接ID={ConnectionId}, 序列号={SequenceId}", 
                _connectionId, response.SequenceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送单个响应失败：连接ID={ConnectionId}, 序列号={SequenceId}",
                _connectionId, response.SequenceId);
        }
    }
    
    /// <summary>
    /// 序列化响应
    /// </summary>
    private ReadOnlyMemory<byte> SerializeResponse(MessageResponse response)
    {
        // 这里应该实现具体的序列化逻辑
        // 当前返回空的内存块作为占位符
        return ReadOnlyMemory<byte>.Empty;
    }
    
    #endregion
    
    #region IBatchProcessor实现
    
    /// <summary>
    /// 参数更新通知
    /// </summary>
    public void OnParametersUpdated(int batchInterval, int batchSize)
    {
        _logger.LogDebug("批处理参数已更新：连接ID={ConnectionId}, 间隔={Interval}ms, 批大小={BatchSize}",
            _connectionId, batchInterval, batchSize);
        
        // 这里可以根据新参数调整引擎行为
        // 当前实现中主要由自适应调度器控制
    }
    
    #endregion
    
    #region 辅助方法
    
    /// <summary>
    /// L2背压处理
    /// </summary>
    private async Task HandleL2Backpressure(MessageBatch batch)
    {
        _logger.LogWarning("L2队列满，执行紧急背压：连接ID={ConnectionId}, 批大小={BatchSize}",
            _connectionId, batch.Messages.Length);
        
        // 分离关键消息和普通消息
        var criticalMessages = new List<MessageEnvelope>();
        var normalMessages = new List<MessageEnvelope>();
        
        foreach (var envelope in batch.Messages)
        {
            if (envelope.Status == MessageStatus.Critical)
                criticalMessages.Add(envelope);
            else
                normalMessages.Add(envelope);
        }
        
        // 丢弃普通消息，保留关键消息
        if (normalMessages.Count > 0)
        {
            Interlocked.Add(ref _statistics._totalDropped, normalMessages.Count);
            
            // 异步发送错误响应
            _ = Task.Run(async () =>
            {
                var errorTasks = normalMessages.Select(msg =>
                    SendErrorResponseAsync(msg.SequenceId, "SERVER_OVERLOAD", "服务器过载")).ToArray();
                
                try
                {
                    await Task.WhenAll(errorTasks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送背压错误响应失败：连接ID={ConnectionId}", _connectionId);
                }
            });
        }
        
        // 关键消息强制入队
        if (criticalMessages.Count > 0)
        {
            var criticalBatch = new MessageBatch
            {
                Messages = criticalMessages.ToArray(),
                BatchId = batch.BatchId + "-critical",
                CreateTime = batch.CreateTime,
                ConnectionId = _connectionId
            };
            
            try
            {
                await _l2BatchWriter.WriteAsync(criticalBatch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关键消息强制入队失败：连接ID={ConnectionId}", _connectionId);
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
            var errorResponse = new MessageResponse
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
            _logger.LogError(ex, "发送错误响应失败：连接ID={ConnectionId}, 序列号={SequenceId}",
                _connectionId, sequenceId);
        }
    }
    
    /// <summary>
    /// 回退到单个发送
    /// </summary>
    private async Task FallbackToIndividualSend(MessageResponse[] responses)
    {
        var tasks = responses.Select(response => Task.Run(async () =>
        {
            try
            {
                await SendSingleResponseAsync(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "单独响应发送失败：连接ID={ConnectionId}, 序列号={SequenceId}",
                    _connectionId, response.SequenceId);
            }
        })).ToArray();
        
        await Task.WhenAll(tasks);
    }
    
    #endregion
    
    #region IAsyncDisposable实现
    
    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_isRunning) return;
        
        _isRunning = false;
        
        // 停止自适应调度器
        await _adaptiveBatchScheduler.DisposeAsync();
        
        // 取消所有任务
        await _cancellationTokenSource.CancelAsync();
        
        // 等待处理任务完成
        var tasks = new List<Task>();
        if (_processingTask != null) tasks.Add(_processingTask);
        if (_responseTask != null) tasks.Add(_responseTask);
        
        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("消息引擎关闭超时：连接ID={ConnectionId}", _connectionId);
        }
        
        // 释放资源
        _l2BatchWriter.TryComplete();
        _l3ResponseWriter.TryComplete();
        _cancellationTokenSource.Dispose();
        _l1Buffer.Dispose();
        
        _logger.LogInformation("高性能消息引擎已释放：连接ID={ConnectionId}", _connectionId);
    }
    
    #endregion
}

#region 支持类型

/// <summary>
/// 服务端消息基类
/// </summary>
public abstract class ServerMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public long SequenceId { get; set; }
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public DateTime ServerTimestamp { get; set; }
}

/// <summary>
/// 消息处理器注册表接口
/// </summary>
public interface IMessageHandlerRegistry
{
    Task<object?> HandleAsync(ServerMessage message);
}

/// <summary>
/// 消息信封 - 零拷贝消息包装器
/// </summary>
public struct MessageEnvelope
{
    public ServerMessage Message;
    public long SequenceId;
    public long EnqueueTime;
    public MessageStatus Status;
    public MessagePriority Priority;
}

/// <summary>
/// 消息批次
/// </summary>
public class MessageBatch
{
    public MessageEnvelope[] Messages { get; set; } = Array.Empty<MessageEnvelope>();
    public string BatchId { get; set; } = "";
    public long CreateTime { get; set; }
    public string ConnectionId { get; set; } = "";
}

/// <summary>
/// 响应批次
/// </summary>
public class ResponseBatch
{
    public MessageResponse[] Responses { get; set; } = Array.Empty<MessageResponse>();
    public string BatchId { get; set; } = "";
    public TimeSpan TotalProcessingTime { get; set; }
    public string ConnectionId { get; set; } = "";
}

/// <summary>
/// 消息引擎配置
/// </summary>
public class MessageEngineConfiguration
{
    public bool EnableDetailedLogging { get; set; } = false;
    public double NormalMessageDropRate { get; set; } = 0.8;
    public int CriticalMessageTimeoutUs { get; set; } = 100;
    public int L2BackpressureWaitMs { get; set; } = 1;
    public int PerformanceCheckFrequency { get; set; } = 10;
    public int BatchSoftTimeoutMs { get; set; } = 50;
}

/// <summary>
/// 消息引擎统计信息
/// </summary>
public class MessageEngineStatistics
{
    public string ConnectionId { get; set; } = "";
    public bool IsRunning { get; set; }
    
    // L1缓冲区统计
    public int L1Capacity { get; set; }
    public int L1Count { get; set; }
    public double L1Utilization { get; set; }
    
    // 消息处理统计
    public long TotalEnqueued { get; set; }
    public long TotalProcessed { get; set; }
    public long TotalDropped { get; set; }
    public long MessagesInL1 { get; set; }
    public long MessagesInL2 { get; set; }
    public long MessagesInL3 { get; set; }
    
    // 自适应调度统计
    public int CurrentBatchInterval { get; set; }
    public int CurrentBatchSize { get; set; }
    public double AverageThroughput { get; set; }
    public TimeSpan AverageLatency { get; set; }
    public TimeSpan P95Latency { get; set; }
    
    // 内存池统计
    public double MemoryPoolHitRatio { get; set; }
    public long TotalMemoryAllocated { get; set; }
}

/// <summary>
/// 引擎统计计数器
/// </summary>
internal class EngineStatistics
{
    internal long _totalEnqueued;
    internal long _totalProcessed;
    internal long _totalDropped;
    internal long _messagesInL1;
    internal long _messagesInL2;
    internal long _messagesInL3;
    internal long _criticalForced;
}

#endregion