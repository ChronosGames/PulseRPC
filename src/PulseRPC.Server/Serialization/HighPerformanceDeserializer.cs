using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Network;

namespace PulseRPC.Server.Serialization;

/// <summary>
/// 高性能消息反序列化器接口
/// </summary>
public interface IMessageDeserializer : IDisposable
{
    /// <summary>
    /// 启动反序列化器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止反序列化器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理解析的消息包
    /// </summary>
    ValueTask ProcessMessagePacketAsync(MessageParsedEventArgs eventArgs);

    /// <summary>
    /// 消息反序列化完成事件
    /// </summary>
    event EventHandler<MessageDeserializedEventArgs> MessageDeserialized;
}

/// <summary>
/// 高性能消息反序列化器实现
/// 使用类型缓存和零分配设计
/// </summary>
internal sealed class HighPerformanceDeserializer : IMessageDeserializer
{
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILogger<HighPerformanceDeserializer> _logger;
    private readonly DeserializerOptions _options;

    // 高性能通道用于消息传递
    private readonly Channel<MessageDeserializationTask> _deserializationChannel;
    private readonly ChannelWriter<MessageDeserializationTask> _deserializationWriter;
    private readonly ChannelReader<MessageDeserializationTask> _deserializationReader;

    // 序列化器缓存 - 按服务名+方法名缓存
    private readonly ConcurrentDictionary<MethodKey, ISerializer> _serializerCache = new();

    // 处理任务
    private Task[]? _processingTasks;
    private readonly CancellationTokenSource _shutdownCts = new();

    public event EventHandler<MessageDeserializedEventArgs>? MessageDeserialized;

    public HighPerformanceDeserializer(
        ISerializerProvider? serializerProvider = null,
        DeserializerOptions? options = null,
        ILogger<HighPerformanceDeserializer>? logger = null)
    {
        _serializerProvider = serializerProvider ?? PulseRPCSerializerProvider.Instance;
        _options = options ?? new DeserializerOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HighPerformanceDeserializer>.Instance;

        // 创建有界高性能通道
        var channelOptions = new BoundedChannelOptions(_options.ChannelCapacity)
        {
            SingleReader = false, // 多个反序列化器线程
            SingleWriter = false, // 多个网络处理器线程写入
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait // 背压控制
        };

        _deserializationChannel = Channel.CreateBounded<MessageDeserializationTask>(channelOptions);
        _deserializationWriter = _deserializationChannel.Writer;
        _deserializationReader = _deserializationChannel.Reader;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("启动高性能反序列化器，处理器线程数: {ProcessorCount}", _options.ProcessorThreadCount);

        // 启动多个反序列化器线程
        _processingTasks = new Task[_options.ProcessorThreadCount];

        for (int i = 0; i < _options.ProcessorThreadCount; i++)
        {
            var processorId = i;
            _processingTasks[i] = Task.Run(async () => await ProcessDeserializationTasksAsync(processorId, _shutdownCts.Token), cancellationToken);
        }

        _logger.LogInformation("反序列化器启动完成");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("停止反序列化器");

        // 标记写入完成
        _deserializationWriter.Complete();

        // 取消所有处理任务
        await _shutdownCts.CancelAsync();

        // 等待所有处理任务完成
        if (_processingTasks != null)
        {
            try
            {
                await Task.WhenAll(_processingTasks).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("反序列化器停止超时");
            }
        }

        _logger.LogInformation("反序列化器停止完成");
    }

    /// <summary>
    /// 处理消息包 - 高性能入口点
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask ProcessMessagePacketAsync(MessageParsedEventArgs eventArgs)
    {
        // 创建反序列化任务
        var task = new MessageDeserializationTask(
            eventArgs.ConnectionId,
            eventArgs.MessagePacket,
            eventArgs.ReceivedTime,
            eventArgs.ProcessorId);

        // 异步写入通道，提供背压控制
        if (!await _deserializationWriter.WaitToWriteAsync(_shutdownCts.Token))
        {
            _logger.LogWarning("反序列化通道已关闭");
            return;
        }

        if (!_deserializationWriter.TryWrite(task))
        {
            _logger.LogWarning("无法写入反序列化任务到通道，连接: {ConnectionId}", eventArgs.ConnectionId);
        }
    }

    /// <summary>
    /// 处理反序列化任务的主循环
    /// </summary>
    private async Task ProcessDeserializationTasksAsync(int processorId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("反序列化处理器 #{ProcessorId} 启动", processorId);

        try
        {
            await foreach (var task in _deserializationReader.ReadAllAsync(cancellationToken))
            {
                await ProcessDeserializationTaskAsync(task, processorId);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "反序列化处理器 #{ProcessorId} 发生异常", processorId);
        }

        _logger.LogDebug("反序列化处理器 #{ProcessorId} 停止", processorId);
    }

    /// <summary>
    /// 处理单个反序列化任务
    /// </summary>
    private async Task ProcessDeserializationTaskAsync(MessageDeserializationTask task, int processorId)
    {
        try
        {
            var messagePacket = task.MessagePacket;
            var header = messagePacket.Header;

            // 验证消息类型
            if (header.Type != MessageType.Request && header.Type != MessageType.OneWay)
            {
                _logger.LogWarning("不支持的消息类型: {MessageType}, 连接: {ConnectionId}",
                    header.Type, task.ConnectionId);
                return;
            }

            // 获取缓存的序列化器
            var methodKey = new MethodKey(header.ServiceName, header.MethodName);
            var serializer = GetCachedSerializer(methodKey);

            // 反序列化请求数据
            var requestData = await DeserializeRequestAsync(serializer, messagePacket.Payload);

            // 创建服务调用上下文
            var callContext = new ServiceCallContext(
                connectionId: task.ConnectionId,
                messageId: header.MessageId,
                serviceName: header.ServiceName,
                methodName: header.MethodName,
                requestData: requestData,
                messageType: header.Type,
                receivedTime: task.ReceivedTime,
                processorId: processorId,
                flags: header.Flags);

            // 触发反序列化完成事件
            var eventArgs = new MessageDeserializedEventArgs(callContext);
            MessageDeserialized?.Invoke(this, eventArgs);

            _logger.LogTrace("反序列化完成: 服务={ServiceName}, 方法={MethodName}, 消息ID={MessageId}, 处理器={ProcessorId}",
                header.ServiceName, header.MethodName, header.MessageId, processorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "反序列化任务失败: 连接={ConnectionId}, 消息ID={MessageId}, 处理器={ProcessorId}",
                task.ConnectionId, task.MessagePacket.Header.MessageId, processorId);
        }
    }

    /// <summary>
    /// 获取缓存的序列化器
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ISerializer GetCachedSerializer(MethodKey methodKey)
    {
        return _serializerCache.GetOrAdd(methodKey, static (key, provider) =>
        {
            // 这里可以根据服务名和方法名选择特定的序列化器
            // 目前使用默认的 MemoryPack 序列化器
            return provider.Create(MethodType.Unary, null);
        }, _serializerProvider);
    }

    /// <summary>
    /// 反序列化请求数据
    /// </summary>
    private Task<object?> DeserializeRequestAsync(ISerializer serializer, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return Task.FromResult<object?>(null);

        // 使用内存池避免大对象分配
        using var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);

        var sequence = new ReadOnlySequence<byte>(owner.Memory[..payload.Length]);

        // 这里需要根据实际的服务接口类型进行反序列化
        // 为了演示，我们返回原始字节数据
        // 实际实现中应该通过源码生成器生成具体的反序列化逻辑
        return Task.FromResult<object?>(payload.ToArray());
    }

    public void Dispose()
    {
        if (!_shutdownCts.IsCancellationRequested)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        _shutdownCts.Dispose();
        _serializerCache.Clear();
    }
}

/// <summary>
/// 反序列化任务结构
/// </summary>
internal class MessageDeserializationTask
{
    public readonly string ConnectionId;
    public readonly MessagePacketHolder MessagePacket;
    public readonly DateTime ReceivedTime;
    public readonly int NetworkProcessorId;

    public MessageDeserializationTask(
        string connectionId,
        MessagePacketHolder messagePacket,
        DateTime receivedTime,
        int networkProcessorId)
    {
        ConnectionId = connectionId;
        MessagePacket = messagePacket;
        ReceivedTime = receivedTime;
        NetworkProcessorId = networkProcessorId;
    }
}

/// <summary>
/// 方法键 - 用于序列化器缓存
/// </summary>
internal readonly struct MethodKey : IEquatable<MethodKey>
{
    private readonly string _serviceName;
    private readonly string _methodName;
    private readonly int _hashCode;

    public MethodKey(string serviceName, string methodName)
    {
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _methodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        _hashCode = HashCode.Combine(_serviceName, _methodName);
    }

    public bool Equals(MethodKey other)
    {
        return _serviceName == other._serviceName && _methodName == other._methodName;
    }

    public override bool Equals(object? obj)
    {
        return obj is MethodKey other && Equals(other);
    }

    public override int GetHashCode() => _hashCode;
}

/// <summary>
/// 服务调用上下文
/// </summary>
public sealed class ServiceCallContext
{
    public string ConnectionId { get; }
    public Guid MessageId { get; }
    public string ServiceName { get; }
    public string MethodName { get; }
    public object? RequestData { get; }
    public MessageType MessageType { get; }
    public DateTime ReceivedTime { get; }
    public int ProcessorId { get; }
    public MessageFlags Flags { get; }
    public DateTime DeserializedTime { get; }

    public ServiceCallContext(
        string connectionId,
        Guid messageId,
        string serviceName,
        string methodName,
        object? requestData,
        MessageType messageType,
        DateTime receivedTime,
        int processorId,
        MessageFlags flags)
    {
        ConnectionId = connectionId;
        MessageId = messageId;
        ServiceName = serviceName;
        MethodName = methodName;
        RequestData = requestData;
        MessageType = messageType;
        ReceivedTime = receivedTime;
        ProcessorId = processorId;
        Flags = flags;
        DeserializedTime = DateTime.UtcNow;
    }
}

/// <summary>
/// 消息反序列化完成事件参数
/// </summary>
public sealed class MessageDeserializedEventArgs : EventArgs
{
    public ServiceCallContext CallContext { get; }

    public MessageDeserializedEventArgs(ServiceCallContext callContext)
    {
        CallContext = callContext ?? throw new ArgumentNullException(nameof(callContext));
    }
}

/// <summary>
/// 反序列化器配置选项
/// </summary>
public sealed class DeserializerOptions
{
    /// <summary>
    /// 处理器线程数量
    /// </summary>
    public int ProcessorThreadCount { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// 通道容量
    /// </summary>
    public int ChannelCapacity { get; set; } = 10000;

    /// <summary>
    /// 序列化器缓存大小
    /// </summary>
    public int SerializerCacheSize { get; set; } = 1000;
}
