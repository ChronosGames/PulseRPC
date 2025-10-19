using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Server.Scheduling;
using PulseRPC.Server.Serialization;
using PulseRPC.Server.Engine;
using PulseRPC.Server.Routing;
using MemoryPack;

namespace PulseRPC.Server.Dispatch;

/// <summary>
/// 高性能消息调度器接口
/// </summary>
public interface IMessageDispatcher : IDisposable
{
    /// <summary>
    /// 启动消息调度器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止消息调度器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 分发反序列化的消息
    /// </summary>
    ValueTask<object?> DispatchAsync(MessageEnvelope message, IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册服务处理器
    /// </summary>
    void RegisterServiceHandler(string serviceName, IServiceHandler handler);

    /// <summary>
    /// 消息处理完成事件
    /// </summary>
    event EventHandler<MessageProcessedEventArgs> MessageProcessed;
}

/// <summary>
/// 高性能消息调度器实现
/// 支持优先级调度、负载均衡和背压控制
/// </summary>
internal sealed class HighPerformanceMessageDispatcher : IMessageDispatcher
{
    private readonly ILogger<HighPerformanceMessageDispatcher> _logger;
    private readonly DispatcherOptions _options;

    // 服务处理器注册表
    private readonly ConcurrentDictionary<string, IServiceHandler> _serviceHandlers = new();

    // 多优先级调度通道
    private readonly Channel<ServiceInvocationTask>[] _priorityChannels;
    private readonly ChannelWriter<ServiceInvocationTask>[] _priorityWriters;
    private readonly ChannelReader<ServiceInvocationTask>[] _priorityReaders;

    // 调度器线程池
    private Task[]? _dispatcherTasks;
    private readonly CancellationTokenSource _shutdownCts = new();

    // 负载均衡和统计
    private long _totalMessagesDispatched;
    private long _totalProcessingTime;
    private readonly ConcurrentDictionary<string, ServiceStatistics> _serviceStats = new();

    // 可识别元数据的高性能反序列化器 (可选依赖)
    private IMetadataAwareMessageDeserializer? _metadataDeserializer;

    // 编译时消息分发器 (可选，由 Source Generator 生成)
    private AbstractCompiledMessageDispatcher? _compiledMessageDispatcher;

    public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

    public void SetMetadataDeserializer(IMetadataAwareMessageDeserializer? metadataDeserializer)
    {
        _metadataDeserializer = metadataDeserializer;
    }

    public void SetCompiledDispatcher(AbstractCompiledMessageDispatcher? compiledDispatcher)
    {
        _compiledMessageDispatcher = compiledDispatcher;
    }

    public HighPerformanceMessageDispatcher()
        : this(null, null, null)
    {
    }

    public HighPerformanceMessageDispatcher(
        IMetadataAwareMessageDeserializer? metadataDeserializer = null,
        DispatcherOptions? options = null,
        ILogger<HighPerformanceMessageDispatcher>? logger = null)
    {
        _metadataDeserializer = metadataDeserializer;
        _options = options ?? new DispatcherOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HighPerformanceMessageDispatcher>.Instance;

        // 创建多优先级通道
        var priorityCount = Enum.GetValues<MessagePriority>().Length;
        _priorityChannels = new Channel<ServiceInvocationTask>[priorityCount];
        _priorityWriters = new ChannelWriter<ServiceInvocationTask>[priorityCount];
        _priorityReaders = new ChannelReader<ServiceInvocationTask>[priorityCount];

        for (int i = 0; i < priorityCount; i++)
        {
            var channelOptions = new BoundedChannelOptions(_options.ChannelCapacity)
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            };

            _priorityChannels[i] = Channel.CreateBounded<ServiceInvocationTask>(channelOptions);
            _priorityWriters[i] = _priorityChannels[i].Writer;
            _priorityReaders[i] = _priorityChannels[i].Reader;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("启动高性能消息调度器，调度器线程数: {DispatcherCount}", _options.DispatcherThreadCount);

        // 启动多个调度器线程
        _dispatcherTasks = new Task[_options.DispatcherThreadCount];

        for (var i = 0; i < _options.DispatcherThreadCount; i++)
        {
            var dispatcherId = i;
            _dispatcherTasks[i] = Task.Run(async () => await RunDispatcherAsync(dispatcherId, _shutdownCts.Token), cancellationToken);
        }

        _logger.LogInformation("消息调度器启动完成");

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("停止消息调度器");

        // 标记所有通道写入完成
        foreach (var writer in _priorityWriters)
        {
            writer.Complete();
        }

        // 取消所有调度任务
        await _shutdownCts.CancelAsync();

        // 等待所有调度任务完成
        if (_dispatcherTasks != null)
        {
            try
            {
                await Task.WhenAll(_dispatcherTasks).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("消息调度器停止超时");
            }
        }

        _logger.LogInformation("消息调度器停止完成");
    }

    /// <summary>
    /// 分发消息 - 高性能入口点
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<object?> DispatchAsync(MessageEnvelope message, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var header = message.Header;
        if (header == null)
        {
            _logger.LogWarning("消息头为空，无法分发");
            return new { Success = false, Error = "Missing header" };
        }

        var serviceName = header.ServiceName ?? string.Empty;
        var methodName = header.MethodName ?? string.Empty;

        if (string.IsNullOrEmpty(serviceName) || string.IsNullOrEmpty(methodName))
        {
            _logger.LogWarning("消息缺少 ServiceName 或 MethodName，无法分发");
            return new { Success = false, Error = "Missing service or method" };
        }

        // 快速路径1：优先使用编译时生成的 ServiceRoutingTable（零反射，最快）
        var routingTable = ServiceRoutingTableRegistry.Instance;
        if (routingTable != null)
        {
            try
            {
                _logger.LogTrace("使用 ServiceRoutingTable 路由: Service={Service}, Method={Method}", serviceName, methodName);
                return await routingTable.RouteAsync(serviceProvider, serviceName, methodName, message.Payload, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ServiceRoutingTable 路由失败: Service={Service}, Method={Method}", serviceName, methodName);
                return new { Success = false, Error = ex.Message };
            }
        }

        // 慢速路径：反射路径（兼容性后备）
        _logger.LogDebug("编译时分发器不可用，使用反射路径: Service={Service}, Method={Method}", serviceName, methodName);

        if (!_compiledMessageDispatcher!.TryGetHandlerMetadata(serviceName, methodName, out var metadata))
        {
            _logger.LogWarning("未找到服务元数据: Service={Service}, Method={Method}", serviceName, methodName);
            return new { Success = false, Error = "Missing handler metadata" };
        }

        object? requestModel = null;
        if (message.Payload.Length > 0)
        {
            try
            {
                requestModel = await DeserializeRequestAsync(message.Payload, metadata, serviceProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "消息反序列化失败: Service={Service}, Method={Method}", serviceName, methodName);
                return new { Success = false, Error = ex.Message };
            }
        }

        var callContext = new ServiceCallContext(
            connectionId: message.ConnectionId ?? string.Empty,
            messageId: header.MessageId != Guid.Empty ? header.MessageId : Guid.NewGuid(),
            serviceName: serviceName,
            methodName: methodName,
            requestData: requestModel,
            messageType: header.Type,
            receivedTime: DateTime.UtcNow,
            processorId: Environment.CurrentManagedThreadId,
            flags: header.Flags);

        return await DispatchCallContextAsync(callContext, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册服务处理器
    /// </summary>
    public void RegisterServiceHandler(string serviceName, IServiceHandler handler)
    {
        if (string.IsNullOrEmpty(serviceName))
            throw new ArgumentException("服务名称不能为空", nameof(serviceName));

        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _serviceHandlers[serviceName] = handler;
        _logger.LogInformation("注册服务处理器: {ServiceName}", serviceName);
    }

    /// <summary>
    /// 运行调度器主循环 - 支持优先级调度
    /// </summary>
    private async Task RunDispatcherAsync(int dispatcherId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("消息调度器 #{DispatcherId} 启动", dispatcherId);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 按优先级顺序检查通道
                var taskHandled = false;

                for (int priority = (int)MessagePriority.Critical; priority >= (int)MessagePriority.Low; priority--)
                {
                    var reader = _priorityReaders[priority];

                    if (reader.TryRead(out var invocationTask))
                    {
                        await ProcessInvocationTaskAsync(invocationTask, dispatcherId, cancellationToken);
                        taskHandled = true;
                        break; // 处理一个任务后重新检查高优先级
                    }
                }

                // 如果没有任务，等待一小段时间
                if (!taskHandled)
                {
                    // 使用 WaitToReadAsync 等待任何优先级通道有数据
                    var waitTasks = _priorityReaders.Select(reader => reader.WaitToReadAsync(cancellationToken).AsTask()).ToArray();

                    try
                    {
                        await Task.WhenAny(waitTasks);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息调度器 #{DispatcherId} 发生异常", dispatcherId);
        }

        _logger.LogDebug("消息调度器 #{DispatcherId} 停止", dispatcherId);
    }

    /// <summary>
    /// 处理服务调用任务
    /// </summary>
    private async Task ProcessInvocationTaskAsync(ServiceInvocationTask invocationTask, int dispatcherId, CancellationToken cancellationToken)
    {
        var callContext = invocationTask.CallContext;

        try
        {
            var result = await ProcessServiceCallAsync(callContext, invocationTask.ServiceHandler, dispatcherId, cancellationToken).ConfigureAwait(false);
            invocationTask.CompletionSource?.TrySetResult(result);
        }
        catch (Exception ex)
        {
            invocationTask.CompletionSource?.TrySetException(ex);
        }
    }

    /// <summary>
    /// 执行服务调用并触发事件
    /// </summary>
    private async Task<object?> ProcessServiceCallAsync(ServiceCallContext callContext, IServiceHandler serviceHandler, int dispatcherId, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogTrace("开始处理服务调用: 服务={ServiceName}, 方法={MethodName}, 消息ID={MessageId}, 调度器={DispatcherId}",
                callContext.ServiceName, callContext.MethodName, callContext.MessageId, dispatcherId);

            var result = await serviceHandler.HandleAsync(callContext).ConfigureAwait(false);

            var processingTime = DateTime.UtcNow - startTime;
            UpdateServiceStatistics(callContext.ServiceName, processingTime, true);
            Interlocked.Increment(ref _totalMessagesDispatched);

            MessageProcessed?.Invoke(this, new MessageProcessedEventArgs(
                callContext,
                result,
                processingTime,
                dispatcherId,
                true));

            _logger.LogTrace("服务调用完成: 服务={ServiceName}, 方法={MethodName}, 耗时={ProcessingTime}ms",
                callContext.ServiceName, callContext.MethodName, processingTime.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            UpdateServiceStatistics(callContext.ServiceName, processingTime, false);

            _logger.LogError(ex, "服务调用失败: 服务={ServiceName}, 方法={MethodName}, 消息ID={MessageId}",
                callContext.ServiceName, callContext.MethodName, callContext.MessageId);

            MessageProcessed?.Invoke(this, new MessageProcessedEventArgs(
                callContext,
                null,
                processingTime,
                dispatcherId,
                false,
                ex));

            throw;
        }
    }

    /// <summary>
    /// 确定消息优先级
    /// </summary>
    private MessagePriority DeterminePriority(ServiceCallContext callContext)
    {
        // 检查消息标志
        if (callContext.Flags.HasFlag(MessageFlags.HighPriority))
            return MessagePriority.Critical;

        // 根据服务名称确定优先级
        return callContext.ServiceName.ToLower() switch
        {
            var name when name.Contains("auth") => MessagePriority.High,
            var name when name.Contains("health") => MessagePriority.High,
            var name when name.Contains("metrics") => MessagePriority.Low,
            var name when name.Contains("log") => MessagePriority.Low,
            _ => MessagePriority.Normal
        };
    }

    /// <summary>
    /// 更新服务统计信息
    /// </summary>
    private void UpdateServiceStatistics(string serviceName, TimeSpan processingTime, bool success)
    {
        _serviceStats.AddOrUpdate(serviceName,
            new ServiceStatistics(serviceName, processingTime, success),
            (_, existing) => existing.Update(processingTime, success));

        // 更新全局统计
        var totalTime = Interlocked.Read(ref _totalProcessingTime);
        Interlocked.Exchange(ref _totalProcessingTime, totalTime + (long)processingTime.TotalMilliseconds);
    }

    private async ValueTask<object?> DeserializeRequestAsync(ReadOnlyMemory<byte> payload, HandlerMetadata metadata, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        if (metadata.RequestType == null || payload.IsEmpty)
        {
            return null;
        }

        // 优先使用可识别元数据的高性能反序列化器
        if (_metadataDeserializer != null)
        {
            var context = new MetadataDeserializationContext(metadata, payload, cancellationToken);
            return await _metadataDeserializer.DeserializeAsync(context).ConfigureAwait(false);
        }

        // 回退到 MemoryPack 反序列化 - 使用 MemoryPack 2.x API
        var requestType = metadata.RequestType;
        return MemoryPackSerializer.Deserialize(requestType, payload.Span);
    }

    private async ValueTask<bool> RouteInvocationAsync(
        ServiceCallContext callContext,
        IServiceHandler serviceHandler,
        MessagePriority priority,
        TaskCompletionSource<object?>? completionSource,
        CancellationToken cancellationToken)
    {
        var invocationTask = new ServiceInvocationTask(
            callContext,
            serviceHandler,
            priority,
            DateTime.UtcNow,
            completionSource);

        var priorityIndex = (int)priority;
        var writer = _priorityWriters[priorityIndex];

        if (!await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            completionSource?.TrySetException(new InvalidOperationException("Dispatcher queue unavailable"));
            return false;
        }

        if (!writer.TryWrite(invocationTask))
        {
            completionSource?.TrySetException(new InvalidOperationException("Dispatcher queue full"));
            return false;
        }

        return true;
    }

    private async ValueTask<bool> EnqueueInvocationAsync(ServiceInvocationTask invocationTask, CancellationToken cancellationToken)
    {
        var priorityIndex = (int)invocationTask.Priority;
        var writer = _priorityWriters[priorityIndex];

        if (!await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return writer.TryWrite(invocationTask);
    }

    private async ValueTask<object?> DispatchCallContextAsync(ServiceCallContext callContext, CancellationToken cancellationToken)
    {
        if (!_serviceHandlers.TryGetValue(callContext.ServiceName, out var serviceHandler))
        {
            _logger.LogWarning("未找到服务处理器: {ServiceName}", callContext.ServiceName);
            return new { Success = false, Error = "Handler not found" };
        }

        var completionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var priority = DeterminePriority(callContext);
        var routed = await RouteInvocationAsync(
            callContext,
            serviceHandler,
            priority,
            completionSource,
            cancellationToken).ConfigureAwait(false);

        if (!routed)
        {
            return new { Success = false, Error = "Dispatcher queue full" };
        }

        return await completionSource.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!_shutdownCts.IsCancellationRequested)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        _shutdownCts.Dispose();

        foreach (var channel in _priorityChannels)
        {
            channel.Writer.Complete();
        }
    }
}

/// <summary>
/// 服务调用任务
/// </summary>
internal readonly struct ServiceInvocationTask
{
    public readonly ServiceCallContext CallContext;
    public readonly IServiceHandler ServiceHandler;
    public readonly MessagePriority Priority;
    public readonly DateTime DispatchTime;
    public readonly TaskCompletionSource<object?>? CompletionSource;

    public ServiceInvocationTask(
        ServiceCallContext callContext,
        IServiceHandler serviceHandler,
        MessagePriority priority,
        DateTime dispatchTime,
        TaskCompletionSource<object?>? completionSource = null)
    {
        CallContext = callContext;
        ServiceHandler = serviceHandler;
        Priority = priority;
        DispatchTime = dispatchTime;
        CompletionSource = completionSource;
    }
}

/// <summary>
/// 服务处理器接口
/// </summary>
public interface IServiceHandler
{
    /// <summary>
    /// 处理服务调用
    /// </summary>
    Task<object?> HandleAsync(ServiceCallContext callContext);
}

/// <summary>
/// 消息处理完成事件参数
/// </summary>
public sealed class MessageProcessedEventArgs : EventArgs
{
    public ServiceCallContext CallContext { get; }
    public object? Result { get; }
    public TimeSpan ProcessingTime { get; }
    public int DispatcherId { get; }
    public bool Success { get; }
    public Exception? Exception { get; }

    public MessageProcessedEventArgs(
        ServiceCallContext callContext,
        object? result,
        TimeSpan processingTime,
        int dispatcherId,
        bool success,
        Exception? exception = null)
    {
        CallContext = callContext;
        Result = result;
        ProcessingTime = processingTime;
        DispatcherId = dispatcherId;
        Success = success;
        Exception = exception;
    }
}

/// <summary>
/// 服务统计信息
/// </summary>
internal sealed class ServiceStatistics
{
    private long _totalCalls;
    private long _successfulCalls;
    private long _totalProcessingTimeMs;

    public string ServiceName { get; }
    public long TotalCalls => _totalCalls;
    public long SuccessfulCalls => _successfulCalls;
    public long FailedCalls => _totalCalls - _successfulCalls;
    public double AverageProcessingTimeMs => _totalCalls > 0 ? (double)_totalProcessingTimeMs / _totalCalls : 0;
    public double SuccessRate => _totalCalls > 0 ? (double)_successfulCalls / _totalCalls : 0;

    public ServiceStatistics(string serviceName, TimeSpan processingTime, bool success)
    {
        ServiceName = serviceName;
        _totalCalls = 1;
        _successfulCalls = success ? 1 : 0;
        _totalProcessingTimeMs = (long)processingTime.TotalMilliseconds;
    }

    public ServiceStatistics Update(TimeSpan processingTime, bool success)
    {
        Interlocked.Increment(ref _totalCalls);
        if (success)
        {
            Interlocked.Increment(ref _successfulCalls);
        }

        var currentTotal = Interlocked.Read(ref _totalProcessingTimeMs);
        Interlocked.Exchange(ref _totalProcessingTimeMs, currentTotal + (long)processingTime.TotalMilliseconds);

        return this;
    }
}

/// <summary>
/// 调度器配置选项
/// </summary>
public sealed class DispatcherOptions
{
    /// <summary>
    /// 调度器线程数量
    /// </summary>
    public int DispatcherThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 每个优先级通道的容量
    /// </summary>
    public int ChannelCapacity { get; set; } = 10000;

    /// <summary>
    /// 是否启用负载均衡
    /// </summary>
    public bool EnableLoadBalancing { get; set; } = true;

    /// <summary>
    /// 是否启用统计收集
    /// </summary>
    public bool EnableStatistics { get; set; } = true;
}
