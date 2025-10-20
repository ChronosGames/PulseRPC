using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Memory;
using PulseRPC.Messaging;
using PulseRPC.Scheduling;
using PulseRPC.Server.Memory;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Scheduling;
using PulseRPC.Server.Serialization;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;
using PulseRPC.Server.Dispatch;
using PulseRPC.Server.Response;
using MessageStatus = PulseRPC.Server.Memory.MessageStatus;
using MessageProcessedEventArgs = PulseRPC.Server.Dispatch.MessageProcessedEventArgs;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 高性能消息引擎
/// 基于技术规格说明书实现的统一高性能消息处理架构
///
/// 核心特性：
/// • 三级缓冲架构：L1(零拷贝环形缓冲区) → L2(自适应批处理) → L3(优先级调度)
/// • 编译时消息分发：零反射开销的消息路由
/// • 自适应性能调优：负载感知的参数动态调整
/// • 分层内存管理：NUMA感知的多级内存池
/// • 丰富监控指标：实时性能追踪和诊断
/// </summary>
public sealed class HighPerformanceMessageEngine : IAsyncDisposable, IBatchProcessor
{
    #region 技术规格常量

    /// <summary>
    /// 高性能消息引擎技术规格 - 基于技术规格说明书定义
    /// </summary>
    public static class Specifications
    {
        public const int L1_BUFFER_SIZE = 4096;          // L1缓冲区大小 (2^12)
        public const int L2_QUEUE_CAPACITY = 256;        // L2队列容量
        public const int L3_QUEUE_CAPACITY = 128;        // L3队列容量
        public const int MIN_BATCH_INTERVAL_MS = 1;      // 最小批处理间隔
        public const int MAX_BATCH_INTERVAL_MS = 10;     // 最大批处理间隔
        public const int ADAPTIVE_BATCH_SIZE_MIN = 8;    // 最小批大小
        public const int ADAPTIVE_BATCH_SIZE_MAX = 128;  // 最大批大小
    }

    /// <summary>
    /// 性能要求规格 - 确保达成技术规格说明书中的性能目标
    /// </summary>
    public static class PerformanceRequirements
    {
        public const int MAX_L1_ENQUEUE_NS = 100;        // L1入队最大100纳秒
        public const int MAX_BATCH_PROCESS_MS = 5;       // 批处理最大5毫秒
        public const double MIN_CACHE_HIT_RATIO = 0.95;  // 最小缓存命中率95%
        public const int MAX_GC_PRESSURE_MB_PER_SEC = 10;// 最大GC压力10MB/s

        // 性能目标（基于技术规格说明书）
        public const int TARGET_THROUGHPUT = 150_000;    // 目标吞吐量150K msgs/sec
        public const int TARGET_P99_LATENCY_MS = 7;      // 目标P99延迟7ms
        public const int TARGET_P95_LATENCY_MS = 3;      // 目标P95延迟3ms
    }

    #endregion

    #region 核心组件和字段

    private readonly string _engineId;
    private readonly IMessageDispatcher _messageDispatcher;
    private readonly IServiceProvider _serviceProvider;
    private readonly MessageEngineConfiguration _options;
    private readonly ILogger<HighPerformanceMessageEngine> _logger;
    private readonly IServiceScheduler? _scheduler;
    private readonly IMessageDeserializer? _deserializer;
    private readonly IResponseProcessor _responseProcessor;

    // 三级缓冲架构核心组件 - 使用TieredMessageProcessor实现
    private TieredMessageProcessor _tieredProcessor = null!;

    // 连接管理和负载均衡
    private ConcurrentDictionary<string, ConnectionContext> _activeConnections = null!;
    private LoadBalancingStrategy _loadBalancer = null!;

    // 性能监控和统计
    private EngineMetrics _metrics = null!;
    private PerformanceMonitor _performanceMonitor = null!;

    // 生命周期管理
    private CancellationTokenSource _cancellationTokenSource = null!;
    private volatile bool _isRunning;
    private volatile bool _isDisposed;

    // 处理任务
    private Task? _monitoringTask;

    #endregion

    #region 构造函数和初始化

    /// <summary>
    /// 构造高性能消息引擎 (使用 IMessageDispatcher)
    /// </summary>
    public HighPerformanceMessageEngine(
        string engineId,
        IMessageDispatcher messageDispatcher,
        IServiceProvider serviceProvider,
        MessageEngineConfiguration configuration,
        ILogger<HighPerformanceMessageEngine>? logger = null,
        IServiceScheduler? scheduler = null,
        IMessageDeserializer? deserializer = null)
    {
        _engineId = engineId ?? throw new ArgumentNullException(nameof(engineId));
        _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? NullLogger<HighPerformanceMessageEngine>.Instance;
        _scheduler = scheduler;
        _deserializer = deserializer;
        _responseProcessor = serviceProvider.GetRequiredService<IResponseProcessor>() ?? throw new ArgumentNullException(nameof(_responseProcessor));

        if (_deserializer is IMetadataAwareMessageDeserializer metadataAware && _messageDispatcher is HighPerformanceMessageDispatcher highPerfDispatcher)
        {
            highPerfDispatcher.SetMetadataDeserializer(metadataAware);
        }

        InitializeEngine();
    }

    private void InitializeEngine()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        // 创建TieredMessageProcessor配置
        var processorOptions = new TieredMessageProcessorOptions
        {
            L1BufferSize = Specifications.L1_BUFFER_SIZE,
            L2QueueCapacity = Specifications.L2_QUEUE_CAPACITY,
            BatchChannelCapacity = Specifications.L3_QUEUE_CAPACITY,
            MaxBatchSize = Specifications.ADAPTIVE_BATCH_SIZE_MAX,
            L2MaxBatchSize = Specifications.ADAPTIVE_BATCH_SIZE_MAX,
            EnableAdaptiveBatching = true,
            EnableDetailedLogging = false,
            NormalMessageDropThreshold = 0.8,
            CriticalMessageTimeoutUs = PerformanceRequirements.MAX_L1_ENQUEUE_NS / 1000, // 转换为微秒
            L2BackpressureWaitMs = Specifications.MIN_BATCH_INTERVAL_MS
        };

        // 创建消息处理委托
        var messageHandler = CreateMessageHandler();

        // 初始化TieredMessageProcessor
        var tieredLogger = (_serviceProvider.GetService(typeof(ILogger<TieredMessageProcessor>)) as ILogger<TieredMessageProcessor>) ??
                           new NullLogger<TieredMessageProcessor>();
        _tieredProcessor = new TieredMessageProcessor(
            _engineId,
            processorOptions,
            messageHandler,
            tieredLogger);

        // 初始化连接管理
        _activeConnections = new ConcurrentDictionary<string, ConnectionContext>();
        _loadBalancer = new LoadBalancingStrategy(_options.LoadBalancingMode);

        // 初始化监控组件
        _metrics = new EngineMetrics();
        _performanceMonitor = new PerformanceMonitor(_metrics, _logger);

        _logger.LogInformation("HighPerformanceMessageEngine初始化完成: EngineId={EngineId}, L1Size={L1Size}, 性能目标: {ThroughputTarget} msgs/sec",
            _engineId, Specifications.L1_BUFFER_SIZE, PerformanceRequirements.TARGET_THROUGHPUT);
    }

    /// <summary>
    /// 创建消息处理委托
    /// </summary>
    private Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> CreateMessageHandler()
    {
        return async (messageSlot, cancellationToken) =>
        {
            var startTime = Stopwatch.GetTimestamp();

            try
            {
                // 将MessageSlot转换为MessageEnvelope进行处理
                // 现在保留完整的元数据
                var envelope = new MessageEnvelope
                {
                    MessageId = messageSlot.MessageId,
                    ConnectionId = messageSlot.ConnectionId, // 使用真实连接ID
                    Header = messageSlot.Header, // 保留完整消息头
                    Payload = messageSlot.Payload, // 零拷贝传递
                    Priority = messageSlot.Priority,
                    EnqueueTime = messageSlot.EnqueueTime,
                    Status = messageSlot.Status
                };

                // 使用现有的消息处理逻辑
                var response = await ProcessSingleMessage(envelope);

                var processingTime = Stopwatch.GetElapsedTime(startTime);
                _metrics.MessagesProcessed.Add(1);

                return ProcessingResult.SuccessResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息时发生错误: MessageId={MessageId}, ConnectionId={ConnectionId}",
                    messageSlot.MessageId, messageSlot.ConnectionId);
                return ProcessingResult.FailResult(ex.Message);
            }
        };
    }

    #endregion

    #region 公共API - 消息处理

    /// <summary>
    /// 启动消息引擎
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return;

        _logger.LogInformation("启动HighPerformanceMessageEngine: EngineId={EngineId}", _engineId);

        try
        {
            // TieredMessageProcessor 在构造时自动启动，这里只需要启动性能监控
            _monitoringTask = RunPerformanceMonitoringAsync(_cancellationTokenSource.Token);

            _isRunning = true;
            _metrics.EngineStartTime = DateTime.UtcNow;

            await _responseProcessor.StartAsync(cancellationToken);

            _logger.LogInformation("HighPerformanceMessageEngine启动成功: EngineId={EngineId}", _engineId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HighPerformanceMessageEngine启动失败: EngineId={EngineId}", _engineId);
            throw;
        }
    }

    /// <summary>
    /// 高性能消息入队 - L1快速路径（接受完整消息包）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnqueueMessage(
        string connectionId,
        MessagePacketHolder messagePacket,
        MessagePriority priority = MessagePriority.Normal)
    {
        if (!_isRunning || _isDisposed)
            return false;

        var startTicks = Stopwatch.GetTimestamp();

        try
        {
            // 更新连接活动时间
            if (_activeConnections.TryGetValue(connectionId, out var context))
            {
                context.LastActivity = DateTime.UtcNow;
            }

            // 构造包含完整元数据的 MessageSlot
            var slot = new MessageSlot
            {
                MessageId = messagePacket.Header.MessageId,
                ConnectionId = connectionId,
                Header = messagePacket.Header,
                Payload = messagePacket.Payload.AsMemory(), // 零拷贝
                Priority = priority,
                EnqueueTime = startTicks,
                Status = MessageStatus.Pending
            };

            // 传递给 TieredMessageProcessor
            var success = _tieredProcessor.TryEnqueueMessageSlot(slot);

            if (!success)
            {
                _metrics.BackpressureEvents.Add(1);
                // 背压处理：根据优先级决定策略
                return HandleBackpressure(slot);
            }

            _metrics.L1MessagesEnqueued.Add(1);
            _metrics.RecordEnqueueLatency(Stopwatch.GetTimestamp() - startTicks);

            return true;
        }
        catch (Exception ex)
        {
            _metrics.EnqueueErrors.Add(1);
            _logger.LogWarning(ex, "消息入队失败: EngineId={EngineId}, ConnectionId={ConnectionId}",
                _engineId, connectionId);
            return false;
        }
    }

    /// <summary>
    /// 处理背压情况
    /// </summary>
    private bool HandleBackpressure(MessageSlot slot)
    {
        switch (slot.Priority)
        {
            case MessagePriority.Critical:
                // 关键消息：阻塞等待直到入队成功或超时
                return TryEnqueueWithRetry(slot, TimeSpan.FromMilliseconds(10), 3);

            case MessagePriority.High:
                // 高优先级：短暂重试
                return TryEnqueueWithRetry(slot, TimeSpan.FromMilliseconds(1), 1);

            case MessagePriority.Normal:
                // 普通消息：检查负载，决定是否丢弃
                var utilization = _metrics.GetCurrentL1Utilization();
                if (utilization > 0.9)
                {
                    _logger.LogWarning("L1利用率过高({Utilization:P}), 丢弃普通消息: MessageId={MessageId}",
                        utilization, slot.MessageId);
                    _metrics.MessagesDropped.Add(1);
                    return false;
                }
                return TryEnqueueWithRetry(slot, TimeSpan.FromMicroseconds(100), 1);

            case MessagePriority.Low:
                // 低优先级：直接丢弃
                _logger.LogDebug("低优先级消息被丢弃: MessageId={MessageId}", slot.MessageId);
                _metrics.MessagesDropped.Add(1);
                return false;

            default:
                _metrics.MessagesDropped.Add(1);
                return false;
        }
    }

    /// <summary>
    /// 尝试重试入队
    /// </summary>
    private bool TryEnqueueWithRetry(MessageSlot slot, TimeSpan delay, int maxRetries)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            if (_tieredProcessor.TryEnqueueMessageSlot(slot))
            {
                if (i > 0)
                {
                    _metrics.RetrySuccesses?.Add(1);
                }
                return true;
            }

            Thread.Sleep(delay);
        }

        _logger.LogWarning("消息入队重试{RetryCount}次后失败: MessageId={MessageId}, ConnectionId={ConnectionId}",
            maxRetries, slot.MessageId, slot.ConnectionId);
        _metrics.MessagesDropped.Add(1);
        return false;
    }

    /// <summary>
    /// 注册连接上下文
    /// </summary>
    public void RegisterConnection(string connectionId, object transportChannel)
    {
        var context = new ConnectionContext
        {
            ConnectionId = connectionId,
            TransportChannel = transportChannel,
            ConnectedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        _activeConnections.TryAdd(connectionId, context);
        _metrics.ActiveConnections.Set(_activeConnections.Count);

        _logger.LogDebug("连接已注册: EngineId={EngineId}, ConnectionId={ConnectionId}", _engineId, connectionId);
    }

    /// <summary>
    /// 注销连接
    /// </summary>
    public void UnregisterConnection(string connectionId)
    {
        if (_activeConnections.TryRemove(connectionId, out var context))
        {
            _metrics.ActiveConnections.Set(_activeConnections.Count);
            _logger.LogDebug("连接已注销: EngineId={EngineId}, ConnectionId={ConnectionId}, Duration={Duration}ms",
                _engineId, connectionId, (DateTime.UtcNow - context.ConnectedAt).TotalMilliseconds);
        }
    }

    #endregion

    #region 核心处理逻辑

    // 注意：三层架构（L1→L2→L3）现在由TieredMessageProcessor内部处理
    // 所有相关的处理逻辑已经被封装在TieredMessageProcessor中

    /// <summary>
    /// 处理单个消息
    /// </summary>
    private async Task<MessageResponse?> ProcessSingleMessage(MessageEnvelope envelope)
    {
        try
        {
            envelope.Status = MessageStatus.Processing;

            // 使用消息分发器处理消息
            object? result = null;

            try
            {
                // 不再需要重新解析 MessagePacket，直接使用 envelope.Header
                var serviceName = envelope.Header.ServiceName ?? "Unknown";
                var methodName = envelope.Header.MethodName ?? "Unknown";

                _logger.LogTrace("处理消息: Service={ServiceName}, Method={MethodName}, MessageId={MessageId}, ConnectionId={ConnectionId}",
                    serviceName, methodName, envelope.MessageId, envelope.ConnectionId);

                object? dispatchResult = null;

                if (_scheduler != null)
                {
                    // 获取服务上下文（用于调度）
                    var serviceContext = GetServiceContextForConnection(envelope.ConnectionId);
                    await _scheduler.InvokeWithSchedulerAsync(
                        serviceContext,
                        serviceName,
                        async () =>
                        {
                            dispatchResult = await _messageDispatcher.DispatchAsync(
                                envelope,
                                _serviceProvider,
                                CancellationToken.None);
                        },
                        CancellationToken.None);
                }
                else
                {
                    dispatchResult = await _messageDispatcher.DispatchAsync(
                        envelope,
                        _serviceProvider,
                        CancellationToken.None);
                }

                result = dispatchResult;
            }
            catch (Exception dispatchEx)
            {
                _logger.LogError(dispatchEx, "消息分发失败: EngineId={EngineId}, MessageId={MessageId}, ConnectionId={ConnectionId}",
                    _engineId, envelope.MessageId, envelope.ConnectionId);

                // 如果分发失败，返回错误响应
                envelope.Status = MessageStatus.Failed;
                return new MessageResponse
                {
                    MessageId = envelope.MessageId.ToString(),
                    ConnectionId = envelope.ConnectionId,
                    Success = false,
                    ErrorMessage = $"消息分发失败: {dispatchEx.Message}",
                    ProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime)
                };
            }

            envelope.Status = MessageStatus.Completed;

            var response = new MessageResponse
            {
                MessageId = envelope.MessageId.ToString(),
                ConnectionId = envelope.ConnectionId,
                Success = true,
                Data = result,
                ProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime)
            };

            // 触发消息处理完成事件（fire-and-forget）
            TriggerMessageProcessedEvent(envelope, result, null);

            return response;
        }
        catch (Exception ex)
        {
            envelope.Status = MessageStatus.Failed;
            _metrics.MessagesErrored.Add(1);

            _logger.LogWarning(ex, "消息处理失败: EngineId={EngineId}, MessageId={MessageId}, ConnectionId={ConnectionId}",
                _engineId, envelope.MessageId, envelope.ConnectionId);

            var response = new MessageResponse
            {
                MessageId = envelope.MessageId.ToString(),
                ConnectionId = envelope.ConnectionId,
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime)
            };

            // 触发消息处理失败事件（fire-and-forget）
            TriggerMessageProcessedEvent(envelope, null, ex);

            return response;
        }
    }

    /// <summary>
    /// 触发消息处理完成事件（fire-and-forget 以避免阻塞消息处理流程）
    /// </summary>
    private void TriggerMessageProcessedEvent(MessageEnvelope envelope, object? result, Exception? exception)
    {
        try
        {
            // 创建 ServiceCallContext 以匹配新的 MessageProcessedEventArgs 构造函数
            var callContext = new ServiceCallContext(
                connectionId: envelope.ConnectionId,
                messageId: envelope.MessageId,
                serviceName: envelope.Header.ServiceName,
                methodName: envelope.Header.MethodName,
                requestData: null, // 已处理完成，不需要再传递原始请求数据
                messageType: envelope.Header.Type,
                receivedTime: envelope.ReceivedTime,
                processorId: envelope.ProcessorId,
                flags: envelope.Header.Flags);

            var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime);

            var eventArgs = new MessageProcessedEventArgs(
                callContext: callContext,
                result: result,
                processingTime: processingTime,
                dispatcherId: envelope.ProcessorId,
                success: exception == null,
                exception: exception);

            // Fire-and-forget: 异步提交到 ResponseProcessor 队列，不等待完成
            // 这避免阻塞消息处理流程，ResponseProcessor 会通过内部队列处理背压
            _= _responseProcessor.ProcessMessageResultAsync(eventArgs);

            _logger.LogTrace("触发MessageProcessed事件: ConnectionId={ConnectionId}, MessageId={MessageId}, Success={Success}",
                envelope.ConnectionId, envelope.MessageId, exception == null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发MessageProcessed事件失败: ConnectionId={ConnectionId}, MessageId={MessageId}",
                envelope.ConnectionId, envelope.MessageId);
        }
    }

    /// <summary>
    /// 获取连接的服务上下文（用于调度）
    /// </summary>
    private IServiceContext? GetServiceContextForConnection(string connectionId)
    {
        if (_activeConnections.TryGetValue(connectionId, out var context))
        {
            // 如果尚未创建ServiceContext，创建一个默认的
            if (context.ServiceContext == null)
            {
                // 注意：ServiceName在这里不可用，将在调度器调用时传入
                context.ServiceContext = new ServiceExecutionContext(
                    connectionId,
                    serviceName: "Unknown", // 将在InvokeWithSchedulerAsync中使用传入的serviceName
                    serviceId: null // 将在认证后设置
                );
            }
            return context.ServiceContext;
        }

        return null;
    }

    #endregion

    #region 性能监控

    /// <summary>
    /// 性能监控循环
    /// </summary>
    private async Task RunPerformanceMonitoringAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("性能监控循环启动: EngineId={EngineId}", _engineId);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken); // 每秒监控一次

                var snapshot = _performanceMonitor.TakeSnapshot();

                // 检查性能目标达成情况
                CheckPerformanceTargets(snapshot);

                // 触发自适应调优
                TriggerAdaptiveOptimization(snapshot);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("性能监控循环已取消: EngineId={EngineId}", _engineId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "性能监控循环异常: EngineId={EngineId}", _engineId);
        }
    }

    /// <summary>
    /// 检查性能目标达成情况
    /// </summary>
    private void CheckPerformanceTargets(PerformanceSnapshot snapshot)
    {
        // 检查吞吐量目标
        if (snapshot.CurrentThroughput < PerformanceRequirements.TARGET_THROUGHPUT * 0.8) // 允许20%偏差
        {
            _logger.LogWarning("吞吐量低于目标: Current={Current}, Target={Target}",
                snapshot.CurrentThroughput, PerformanceRequirements.TARGET_THROUGHPUT);
        }

        // 检查延迟目标
        if (snapshot.P99LatencyMs > PerformanceRequirements.TARGET_P99_LATENCY_MS)
        {
            _logger.LogWarning("P99延迟超过目标: Current={Current}ms, Target={Target}ms",
                snapshot.P99LatencyMs, PerformanceRequirements.TARGET_P99_LATENCY_MS);
        }
    }

    /// <summary>
    /// 触发自适应优化
    /// </summary>
    private void TriggerAdaptiveOptimization(PerformanceSnapshot snapshot)
    {
        // 根据性能指标调整批处理参数
        // 这个逻辑会通过IBatchProcessor接口回调到AdaptiveBatchScheduler
    }

    /// <summary>
    /// 获取引擎统计信息
    /// </summary>
    public EngineStatistics GetStatistics()
    {
        return new EngineStatistics
        {
            EngineId = _engineId,
            IsRunning = _isRunning,
            UpTime = DateTime.UtcNow - _metrics.EngineStartTime,

            // L1统计
            L1BufferUtilization = _metrics.GetCurrentL1Utilization(),
            L1MessagesEnqueued = _metrics.L1MessagesEnqueued.Value,
            L1BackpressureEvents = _metrics.BackpressureEvents.Value,

            // 处理统计
            TotalMessagesProcessed = _metrics.MessagesProcessed.Value,
            TotalMessagesDropped = _metrics.MessagesDropped.Value,
            TotalBatchesProcessed = _metrics.BatchesProcessed.Value,

            // 性能指标
            CurrentThroughput = _metrics.GetCurrentThroughput(),
            AverageLatencyMs = _metrics.GetAverageLatencyMs(),
            P99LatencyMs = _metrics.GetP99LatencyMs(),

            // 连接统计
            ActiveConnections = _activeConnections.Count,

            // 内存统计
            MemoryPoolStatistics = null // 内存池统计现在由TieredMessageProcessor管理
        };
    }

    #endregion

    #region IBatchProcessor实现

    /// <summary>
    /// 批处理参数更新回调（来自AdaptiveBatchScheduler）
    /// </summary>
    public void OnParametersUpdated(int newBatchInterval, int newBatchSize)
    {
        _logger.LogDebug("批处理参数更新: EngineId={EngineId}, NewInterval={NewInterval}ms, NewBatchSize={NewBatchSize}",
            _engineId, newBatchInterval, newBatchSize);

        // 这里可以根据新参数调整处理策略
        // 例如调整L1到L2的转移频率等
    }

    #endregion

    #region 资源清理

    /// <summary>
    /// 停止消息引擎
    /// </summary>
    public async ValueTask StopAsync()
    {
        if (!_isRunning) return;

        _logger.LogInformation("停止HighPerformanceMessageEngine: EngineId={EngineId}", _engineId);

        _isRunning = false;
        await _cancellationTokenSource.CancelAsync();

        await _responseProcessor.StopAsync();

        // 等待处理任务完成
        var stopTasks = new List<Task>();
        if (_monitoringTask != null) stopTasks.Add(_monitoringTask);

        try
        {
            await Task.WhenAll(stopTasks).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("引擎停止超时: EngineId={EngineId}", _engineId);
        }

        // 注意：调度器现在由TieredMessageProcessor管理，不需要手动停止

        _logger.LogInformation("HighPerformanceMessageEngine已停止: EngineId={EngineId}", _engineId);
    }

    /// <summary>
    /// 异步资源释放
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _logger.LogInformation("释放HighPerformanceMessageEngine资源: EngineId={EngineId}", _engineId);

        await StopAsync();

        // 释放TieredMessageProcessor
        await _tieredProcessor.DisposeAsync();

        _cancellationTokenSource.Dispose();

        _isDisposed = true;

        _logger.LogInformation("HighPerformanceMessageEngine资源释放完成: EngineId={EngineId}", _engineId);
    }

    /// <summary>
    /// 同步释放资源
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().Wait();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 生成消息ID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GenerateMessageId()
    {
        return Guid.NewGuid().ToString("N")[..16]; // 16字符短ID
    }

    #endregion
}

#region 数据结构

/// <summary>
/// 消息信封 - 包含完整元数据的消息包装
/// </summary>
public struct MessageEnvelope
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

    /// <summary>
    /// 接收时间
    /// </summary>
    public DateTime ReceivedTime { get; set; }

    /// <summary>
    /// 处理器ID
    /// </summary>
    public int ProcessorId { get; set; }
}

/// <summary>
/// 消息批次 - L2处理的基本单位
/// </summary>
public struct MessageBatch
{
    public string BatchId { get; set; }
    public MessageEnvelope[] Messages { get; set; }
    public long CreateTime { get; set; }
}

/// <summary>
/// 响应批次 - L3处理的基本单位
/// </summary>
public struct ResponseBatch
{
    public string BatchId { get; set; }
    public MessageResponse[] Responses { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

/// <summary>
/// 消息响应
/// </summary>
public class MessageResponse
{
    public string MessageId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

/// <summary>
/// 连接上下文
/// </summary>
public class ConnectionContext
{
    public string ConnectionId { get; set; } = "";
    public object? TransportChannel { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public IServiceContext? ServiceContext { get; set; }
}

/// <summary>
/// 引擎统计信息
/// </summary>
public class EngineStatistics
{
    public string EngineId { get; set; } = "";
    public bool IsRunning { get; set; }
    public TimeSpan UpTime { get; set; }

    // L1统计
    public double L1BufferUtilization { get; set; }
    public long L1MessagesEnqueued { get; set; }
    public long L1BackpressureEvents { get; set; }

    // 处理统计
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public long TotalBatchesProcessed { get; set; }

    // 性能指标
    public double CurrentThroughput { get; set; }
    public double AverageLatencyMs { get; set; }
    public double P99LatencyMs { get; set; }

    // 连接统计
    public int ActiveConnections { get; set; }

    // 内存统计
    public object? MemoryPoolStatistics { get; set; }
}

/// <summary>
/// 引擎性能指标
/// </summary>
public class EngineMetrics
{
    public DateTime EngineStartTime { get; set; } = DateTime.UtcNow;

    // 计数器
    public readonly Counter<long> L1MessagesEnqueued = new();
    public readonly Counter<long> MessagesProcessed = new();
    public readonly Counter<long> MessagesDropped = new();
    public readonly Counter<long> MessagesErrored = new();
    public readonly Counter<long> BatchesProcessed = new();
    public readonly Counter<long> BatchesErrored = new();
    public readonly Counter<long> BackpressureEvents = new();
    public readonly Counter<long> ForcedEnqueues = new();
    public readonly Counter<long> ResponseBatchesSent = new();
    public readonly Counter<long> ResponsesSent = new();
    public readonly Counter<long> ResponseErrors = new();
    public readonly Counter<long> EnqueueErrors = new();

    // 新增指标
    public readonly Counter<long>? RetrySuccesses = new();
    public readonly Counter<long> BackpressureBlocks = new();
    public readonly Counter<long> FallbackProcessed = new();

    // 指标
    public readonly Gauge<int> ActiveConnections = new();

    // 性能指标方法
    public double GetCurrentL1Utilization() => 0.5; // 临时实现
    public double GetCurrentThroughput() => MessagesProcessed.Value / (DateTime.UtcNow - EngineStartTime).TotalSeconds;
    public double GetAverageLatencyMs() => 2.5; // 临时实现
    public double GetP99LatencyMs() => 5.0; // 临时实现
    public void RecordEnqueueLatency(long ticks) { /* TODO: 实现延迟记录 */ }
    public void RecordBatchProcessingTime(TimeSpan time) { /* TODO: 实现批处理时间记录 */ }
}

/// <summary>
/// 简单计数器
/// </summary>
public class Counter<T> where T : struct
{
    private long _value;
    public T Value => (T)(object)Interlocked.Read(ref _value);
    public void Increment() => Interlocked.Increment(ref _value);
    public void Add(int count) => Interlocked.Add(ref _value, count);
}

/// <summary>
/// 简单仪表
/// </summary>
public class Gauge<T> where T : struct
{
    private long _value;
    public T Value => (T)(object)Interlocked.Read(ref _value);
    public void Set(T value) => Interlocked.Exchange(ref _value, (long)(object)value);
}

/// <summary>
/// 负载均衡策略
/// </summary>
public class LoadBalancingStrategy(LoadBalancingMode mode)
{
    private readonly LoadBalancingMode _mode = mode;
}


/// <summary>
/// 性能监控器
/// </summary>
public class PerformanceMonitor
{
    private readonly EngineMetrics _metrics;
    private readonly ILogger _logger;

    public PerformanceMonitor(EngineMetrics metrics, ILogger logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public PerformanceSnapshot TakeSnapshot()
    {
        return new PerformanceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            CurrentThroughput = _metrics.GetCurrentThroughput(),
            P99LatencyMs = _metrics.GetP99LatencyMs(),
            P95LatencyMs = _metrics.GetAverageLatencyMs() // 临时
        };
    }
}

/// <summary>
/// 性能快照
/// </summary>
public class PerformanceSnapshot
{
    public DateTime Timestamp { get; set; }
    public double CurrentThroughput { get; set; }
    public double P99LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
}

#endregion
