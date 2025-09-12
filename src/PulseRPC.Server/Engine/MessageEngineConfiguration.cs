using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;
using PulseRPC.Serialization;
using MemoryPack;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 消息引擎配置 - 替代 HighThroughputProcessorOptions
/// 根据优化计划书 5.1.1 传输层重命名策略
/// </summary>
public class MessageEngineConfiguration
{
    /// <summary>
    /// L1缓冲区大小，默认4096
    /// </summary>
    public int L1BufferSize { get; set; } = 4096;

    /// <summary>
    /// L2队列容量，默认256
    /// </summary>
    public int L2QueueCapacity { get; set; } = 256;

    /// <summary>
    /// L3队列容量，默认128
    /// </summary>
    public int L3QueueCapacity { get; set; } = 128;

    /// <summary>
    /// 最大批处理大小，默认64
    /// </summary>
    public int MaxBatchSize { get; set; } = 64;

    /// <summary>
    /// 批处理间隔（毫秒），默认5ms
    /// </summary>
    public int BatchIntervalMs { get; set; } = 5;

    /// <summary>
    /// 启用详细日志，默认false
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// 普通消息丢弃率，默认0.8
    /// </summary>
    public double NormalMessageDropRate { get; set; } = 0.8;

    /// <summary>
    /// 关键消息超时（微秒），默认100
    /// </summary>
    public int CriticalMessageTimeoutUs { get; set; } = 100;

    /// <summary>
    /// L2背压等待时间（毫秒），默认1ms
    /// </summary>
    public int L2BackpressureWaitMs { get; set; } = 1;

    /// <summary>
    /// 性能检查频率，默认10
    /// </summary>
    public int PerformanceCheckFrequency { get; set; } = 10;

    /// <summary>
    /// 批处理软超时（毫秒），默认50ms
    /// </summary>
    public int BatchSoftTimeoutMs { get; set; } = 50;

    /// <summary>
    /// 启用自适应批处理，默认true
    /// </summary>
    public bool EnableAdaptiveBatching { get; set; } = true;

    /// <summary>
    /// 启用零拷贝优化，默认true
    /// </summary>
    public bool EnableZeroCopy { get; set; } = true;

    /// <summary>
    /// 启用分层内存池，默认true
    /// </summary>
    public bool EnableTieredMemoryPool { get; set; } = true;

    /// <summary>
    /// 是否启用高性能消息引擎，默认true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 普通消息丢弃阈值，默认0.8
    /// </summary>
    public double NormalMessageDropThreshold { get; set; } = 0.8;

    /// <summary>
    /// 负载均衡模式，默认轮询
    /// </summary>
    public LoadBalancingMode LoadBalancingMode { get; set; } = LoadBalancingMode.RoundRobin;
}

/// <summary>
/// 负载均衡模式
/// </summary>
public enum LoadBalancingMode
{
    RoundRobin,
    LeastConnections,
    WeightedRoundRobin,
    Random
}

/// <summary>
/// 服务端消息基类 - 兼容性定义
/// </summary>
public abstract class ServerMessage : ClientMessage
{
    /// <summary>
    /// 服务端处理时间戳
    /// </summary>
    public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 处理优先级
    /// </summary>
    public new MessagePriority Priority { get; set; } = MessagePriority.Normal;
}

/// <summary>
/// 消息分发器接口
/// </summary>
public interface IMessageDispatcher
{
    ValueTask<object?> DispatchAsync(
        object message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 高性能消息处理委托
/// </summary>
public delegate ValueTask<object?> MessageHandlerDelegate(
    object message,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken);

/// <summary>
/// 静态泛型消息分发器注册接口
/// </summary>
public interface IStaticMessageDispatcher
{
    /// <summary>
    /// 注册消息处理器
    /// </summary>
    void RegisterHandler<T>(MessageHandlerDelegate handler);

    /// <summary>
    /// 注册消息处理器（通过类型）
    /// </summary>
    void RegisterHandler(Type messageType, MessageHandlerDelegate handler);

    /// <summary>
    /// 尝试分发消息
    /// </summary>
    ValueTask<object?> TryDispatchAsync(
        object message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查是否可以处理指定类型的消息
    /// </summary>
    bool CanHandle(Type messageType);

    /// <summary>
    /// 检查是否可以处理指定类型的消息
    /// </summary>
    bool CanHandle<T>() where T : class;
}

/// <summary>
/// 消息序列化器接口 - 用于消息的序列化和反序列化
/// </summary>
public interface IMessageSerializer
{
    ValueTask<object> DeserializeAsync(byte[] messageBytes);
    ValueTask<byte[]> SerializeAsync(object message);
}

/// <summary>
/// 默认消息序列化器 - 基于MemoryPack
/// </summary>
public class DefaultMessageSerializer : IMessageSerializer
{
    private readonly ISerializerProvider _serializerProvider;
    private readonly ISerializer _serializer;

    public DefaultMessageSerializer()
    {
        _serializerProvider = PulseRPCSerializerProvider.Instance;
        _serializer = _serializerProvider.Create(MethodType.Unary, null);
    }

    public ValueTask<object> DeserializeAsync(byte[] messageBytes)
    {
        try
        {
            // 这里需要知道目标类型，但我们现在只能做通用处理
            // 先尝试作为动态类型反序列化，如果失败则直接返回字节数组
            var readOnlySequence = new ReadOnlySequence<byte>(messageBytes);

            // TODO: 这里应该根据消息头或其他信息确定具体的消息类型
            // 目前返回字节数组，让生成的分发器处理类型转换
            return ValueTask.FromResult<object>(messageBytes);
        }
        catch (Exception)
        {
            // 如果反序列化失败，返回原始字节数组
            return ValueTask.FromResult<object>(messageBytes);
        }
    }

    public ValueTask<byte[]> SerializeAsync(object message)
    {
        try
        {
            var writer = new ArrayBufferWriter<byte>();
            _serializer.Serialize(writer, message);
            return ValueTask.FromResult(writer.WrittenMemory.ToArray());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法序列化消息类型 {message?.GetType().Name}", ex);
        }
    }
}

/// <summary>
/// 编译时消息分发器
/// </summary>
public class CompiledMessageDispatcher : IMessageDispatcher
{
    private readonly ILogger<CompiledMessageDispatcher> _logger;
    private readonly IMessageSerializer _messageSerializer;
    private readonly StaticGenericMessageDispatcher _staticDispatcher;
    private readonly bool _isInitialized;

    public CompiledMessageDispatcher(
        IServiceProvider serviceProvider,
        ILogger<CompiledMessageDispatcher> logger,
        IMessageSerializer? messageSerializer = null)
    {
        _logger = logger;
        _messageSerializer = messageSerializer ?? new DefaultMessageSerializer();

        // 创建静态泛型分发器
        var staticLogger = (ILogger<StaticGenericMessageDispatcher>?)serviceProvider.GetService(typeof(ILogger<StaticGenericMessageDispatcher>)) ??
                          Microsoft.Extensions.Logging.Abstractions.NullLogger<StaticGenericMessageDispatcher>.Instance;
        _staticDispatcher = new StaticGenericMessageDispatcher(staticLogger);

        // 尝试注册生成的处理器
        _isInitialized = TryRegisterGeneratedHandlers();

        if (_isInitialized)
        {
            _logger.LogInformation("成功注册 {HandlerCount} 个生成的消息处理器", _staticDispatcher.HandlerCount);
        }
        else
        {
            _logger.LogWarning("未找到生成的处理器，将使用回退实现");
        }
    }

    public async ValueTask<object?> DispatchAsync(
        object message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 处理消息反序列化
            var deserializedMessage = await DeserializeMessageIfNeeded(message);

            if (_isInitialized)
            {
                // 使用高性能静态泛型分发器（零反射）
                var result = await _staticDispatcher.TryDispatchAsync(deserializedMessage, serviceProvider, cancellationToken);
                if (result != null)
                {
                    return result;
                }

                // 如果静态分发器无法处理，回退到传统实现
                _logger.LogDebug("静态分发器无法处理消息类型 {MessageType}，回退到传统实现", deserializedMessage?.GetType().Name);
            }

            // 回退到运行时实现
            return new { Success = false, Error = "未找到处理器" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息分发失败: {MessageType}", message?.GetType().Name);
            return new { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 如果需要，反序列化消息
    /// </summary>
    private async ValueTask<object> DeserializeMessageIfNeeded(object message)
    {
        // 如果是字节数组，尝试反序列化
        if (message is byte[] messageBytes)
        {
            return await _messageSerializer.DeserializeAsync(messageBytes);
        }

        // 如果是内存块，转换为字节数组后反序列化
        if (message is Memory<byte> messageMemory)
        {
            return await _messageSerializer.DeserializeAsync(messageMemory.ToArray());
        }

        // 如果已经是对象，直接返回
        return message;
    }

    /// <summary>
    /// 尝试注册生成的处理器到静态分发器
    /// </summary>
    private bool TryRegisterGeneratedHandlers()
    {
        try
        {
            // 尝试通过反射调用生成的注册方法
            var generatedType = Type.GetType("PulseRPC.Generated.CompiledMessageDispatcher");
            if (generatedType != null)
            {
                var registerMethod = generatedType.GetMethod("RegisterHandlers",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(IStaticMessageDispatcher) },
                    null);

                if (registerMethod != null)
                {
                    registerMethod.Invoke(null, new object[] { _staticDispatcher });
                    _logger.LogTrace("成功调用生成的注册方法");
                    return true;
                }
            }

            _logger.LogDebug("未找到生成的处理器注册方法");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "注册生成的处理器时发生错误: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 获取已注册的消息类型（用于调试）
    /// </summary>
    public IEnumerable<Type> GetRegisteredMessageTypes()
    {
        return _staticDispatcher.RegisteredTypes;
    }

    /// <summary>
    /// 获取处理器统计信息（用于监控）
    /// </summary>
    public int GetHandlerCount()
    {
        return _staticDispatcher.HandlerCount;
    }

    /// <summary>
    /// 检查是否可以处理指定类型的消息
    /// </summary>
    public bool CanHandle<T>() where T : class
    {
        return _staticDispatcher.CanHandle<T>();
    }

    /// <summary>
    /// 检查是否可以处理指定类型的消息
    /// </summary>
    public bool CanHandle(Type messageType)
    {
        return _staticDispatcher.CanHandle(messageType);
    }
}

/// <summary>
/// 高性能静态泛型消息分发器实现
/// 使用ConcurrentDictionary和委托实现零反射分发
/// </summary>
public class StaticGenericMessageDispatcher : IStaticMessageDispatcher
{
    private readonly ConcurrentDictionary<Type, MessageHandlerDelegate> _handlers;
    private readonly ILogger<StaticGenericMessageDispatcher> _logger;

    public StaticGenericMessageDispatcher(ILogger<StaticGenericMessageDispatcher> logger)
    {
        _handlers = new ConcurrentDictionary<Type, MessageHandlerDelegate>();
        _logger = logger;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterHandler<T>(MessageHandlerDelegate handler)
    {
        RegisterHandler(typeof(T), handler);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterHandler(Type messageType, MessageHandlerDelegate handler)
    {
        if (_handlers.TryAdd(messageType, handler))
        {
            _logger.LogDebug("注册消息处理器: {MessageType}", messageType.Name);
        }
        else
        {
            _logger.LogWarning("消息处理器已存在: {MessageType}", messageType.Name);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<object?> TryDispatchAsync(
        object message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var messageType = message.GetType();

        // 快速路径：直接类型匹配
        if (_handlers.TryGetValue(messageType, out var handler))
        {
            return await handler(message, serviceProvider, cancellationToken);
        }

        // 慢速路径：继承类型匹配
        foreach (var kvp in _handlers)
        {
            if (kvp.Key.IsAssignableFrom(messageType))
            {
                return await kvp.Value(message, serviceProvider, cancellationToken);
            }
        }

        return null; // 未找到处理器
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanHandle<T>() where T : class
    {
        return CanHandle(typeof(T));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanHandle(Type messageType)
    {
        return _handlers.ContainsKey(messageType) ||
               _handlers.Keys.Any(t => t.IsAssignableFrom(messageType));
    }

    /// <summary>
    /// 获取处理器统计信息
    /// </summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>
    /// 获取已注册的消息类型
    /// </summary>
    public IEnumerable<Type> RegisteredTypes => _handlers.Keys;
}
