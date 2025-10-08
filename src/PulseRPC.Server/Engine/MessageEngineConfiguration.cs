using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;
using PulseRPC.Serialization;
using PulseRPC.Messaging;
using MemoryPack;
using PulseRPC.Server.Scheduling;

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
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
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
public class GeneratedMessageDispatcher : IMessageDispatcher
{
    private readonly ILogger<GeneratedMessageDispatcher> _logger;
    private readonly IMessageSerializer _messageSerializer;
    private readonly StaticGenericMessageDispatcher _staticDispatcher;
    private readonly bool _isInitialized;
    private readonly ISerializerProvider _serializerProvider;
    private AbstractCompiledMessageDispatcher? _compiledDispatcher;
    private readonly IServiceProvider _serviceProvider;
    private volatile bool _lazyInitialized;

    public GeneratedMessageDispatcher(
        IServiceProvider serviceProvider,
        ILogger<GeneratedMessageDispatcher> logger,
        IMessageSerializer? messageSerializer = null,
        ISerializerProvider? serializerProvider = null)
    {
        _logger = logger;
        _messageSerializer = messageSerializer ?? new DefaultMessageSerializer();
        _serializerProvider = serializerProvider ?? PulseRPCSerializerProvider.Instance;
        _serviceProvider = serviceProvider;

        // 创建静态泛型分发器
        var staticLogger = (ILogger<StaticGenericMessageDispatcher>?)serviceProvider.GetService(typeof(ILogger<StaticGenericMessageDispatcher>)) ??
                          Microsoft.Extensions.Logging.Abstractions.NullLogger<StaticGenericMessageDispatcher>.Instance;
        _staticDispatcher = new StaticGenericMessageDispatcher(staticLogger);

        // 尝试创建编译的分发器实例（延迟初始化，避免循环依赖）
        _compiledDispatcher = TryCreateCompiledDispatcher();

        if (_compiledDispatcher != null)
        {
            // 延迟初始化：在第一次使用时初始化服务，避免构造函数中的循环依赖
            // _compiledDispatcher.InitializeServices(serviceProvider); // 移到 LazyInitialize 方法
            // _compiledDispatcher.RegisterHandlers(_staticDispatcher);  // 移到 LazyInitialize 方法

            _isInitialized = true; // 标记为可用，但尚未完全初始化
        }
        else
        {
            // 回退到反射方式
            TryInitializeGeneratedServices(serviceProvider);
            _isInitialized = TryRegisterGeneratedHandlers();
        }

        if (_isInitialized)
        {
            _logger.LogInformation("成功注册 {HandlerCount} 个生成的消息处理器", _staticDispatcher.HandlerCount);
        }
        else
        {
            _logger.LogWarning("未找到生成的处理器，将使用回退实现");
        }
    }

    /// <summary>
    /// 延迟初始化编译的分发器，避免构造函数中的循环依赖
    /// </summary>
    private void LazyInitialize()
    {
        if (_lazyInitialized || _compiledDispatcher == null)
            return;

        lock (this)
        {
            if (_lazyInitialized)
                return;

            try
            {
                // 初始化服务
                _compiledDispatcher.InitializeServices(_serviceProvider);

                // 注册处理器
                _compiledDispatcher.RegisterHandlers(_staticDispatcher);

                _lazyInitialized = true;
                _logger.LogDebug("编译的分发器延迟初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "延迟初始化编译的分发器失败");
            }
        }
    }

    public async ValueTask<object?> DispatchAsync(
        object message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 延迟初始化（仅在第一次调用时执行）
            LazyInitialize();

            // 处理消息反序列化
            var deserializedMessage = await DeserializeMessageIfNeeded(message);

            if (_isInitialized)
            {
                // 如果有编译的分发器且原始消息是字节数组，优先使用字节流处理
                if (_compiledDispatcher != null && _lazyInitialized && message is byte[] messageBytes)
                {
                    try
                    {
                        // 尝试解析消息包以获取服务名和方法名
                        if (MessagePacket.TryReadFrom(messageBytes, out var packet))
                        {
                            var serviceName = packet.Header.ServiceName ?? "Unknown";
                            var methodName = packet.Header.MethodName ?? "Unknown";

                            _logger.LogDebug("解析消息包成功: ServiceName={ServiceName}, MethodName={MethodName}",
                                serviceName, methodName);

                            // 从字节流直接分发消息，使用解析出的服务名和方法名
                            var result = await _compiledDispatcher.DispatchFromBytesAsync(
                                serviceName,
                                methodName,
                                packet.Payload.ToArray(),
                                cancellationToken);

                            if (result != null)
                            {
                                return result;
                            }
                        }
                        else
                        {
                            _logger.LogDebug("无法解析消息包，回退到传统方式");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "编译分发器字节流处理失败，回退到静态分发器");
                    }
                }

                // 使用高性能静态泛型分发器（零反射）
                var staticResult = await _staticDispatcher.TryDispatchAsync(deserializedMessage, cancellationToken);
                if (staticResult != null)
                {
                    return staticResult;
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
        // 如果是字节数组，尝试智能反序列化
        if (message is byte[] messageBytes)
        {
            return await TrySmartDeserializeAsync(messageBytes);
        }

        // 如果是内存块，转换为字节数组后反序列化
        if (message is Memory<byte> messageMemory)
        {
            return await TrySmartDeserializeAsync(messageMemory.ToArray());
        }

        // 如果已经是对象，直接返回
        return message;
    }

    /// <summary>
    /// 智能反序列化 - 尝试从静态分发器已注册的类型中猜测
    /// </summary>
    private async ValueTask<object> TrySmartDeserializeAsync(byte[] messageBytes)
    {
        try
        {
            var readOnlySequence = new ReadOnlySequence<byte>(messageBytes);
            var serializer = _serializerProvider.Create(MethodType.Unary, null);

            // 从静态分发器获取已注册的处理器类型
            var registeredTypes = _staticDispatcher.RegisteredTypes;

            foreach (var type in registeredTypes)
            {
                try
                {
                    // 使用反射调用泛型 Deserialize<T> 方法
                    var method = typeof(ISerializer).GetMethod("Deserialize")!.MakeGenericMethod(type);
                    var result = method.Invoke(serializer, new object[] { readOnlySequence });

                    if (result != null)
                    {
                        _logger.LogDebug("成功反序列化为类型: {TypeName}", type.Name);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "反序列化为类型 {TypeName} 失败", type.Name);
                    // 继续尝试下一个类型
                }
            }

            // 如果都失败了，返回原始字节数组
            _logger.LogDebug("无法反序列化消息到任何已注册类型，返回原始字节数组");
            return messageBytes;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "智能反序列化失败，返回原始字节数组");
            return messageBytes;
        }
    }

    /// <summary>
    /// 尝试创建编译的分发器实例
    /// </summary>
    private AbstractCompiledMessageDispatcher? TryCreateCompiledDispatcher()
    {
        try
        {
            var generatedType = FindGeneratedDispatcherType();
            if (generatedType != null)
            {
                var instance = Activator.CreateInstance(generatedType) as AbstractCompiledMessageDispatcher;
                if (instance != null)
                {
                    _logger.LogTrace("成功创建编译的分发器实例: {TypeName}", generatedType.Name);
                    return instance;
                }
            }

            _logger.LogDebug("未找到或无法创建编译的分发器实例");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建编译的分发器实例时发生错误: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 查找生成的 CompiledMessageDispatcher 类型
    /// </summary>
    private static Type? FindGeneratedDispatcherType()
    {
        try
        {
            // 方法1：直接通过类型名查找
            var type = Type.GetType("PulseRPC.Generated.CompiledMessageDispatcher");
            if (type != null) return type;

            // 方法2：在当前程序集中查找
            var currentAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            type = currentAssembly.GetType("PulseRPC.Generated.CompiledMessageDispatcher");
            if (type != null) return type;

            // 方法3：在调用程序集中查找（生成的代码通常在这里）
            var callingAssembly = System.Reflection.Assembly.GetCallingAssembly();
            type = callingAssembly.GetType("PulseRPC.Generated.CompiledMessageDispatcher");
            if (type != null) return type;

            // 方法4：遍历所有已加载的程序集
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType("PulseRPC.Generated.CompiledMessageDispatcher");
                    if (type != null) return type;
                }
                catch
                {
                    // 忽略无法访问的程序集
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 尝试初始化生成的分发器服务
    /// </summary>
    private void TryInitializeGeneratedServices(IServiceProvider serviceProvider)
    {
        try
        {
            // 尝试通过反射调用生成的初始化方法
            var generatedType = FindGeneratedDispatcherType();
            if (generatedType != null)
            {
                var initMethod = generatedType.GetMethod("InitializeServices",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(IServiceProvider) },
                    null);

                if (initMethod != null)
                {
                    initMethod.Invoke(null, new object[] { serviceProvider });
                    _logger.LogTrace("成功初始化生成的分发器服务");
                    return;
                }
            }

            _logger.LogDebug("未找到生成的服务初始化方法");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "初始化生成的分发器服务时发生错误: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// 尝试使用生成的分发器直接处理字节流消息
    /// </summary>
    private async ValueTask<(bool Success, object? Result)> TryDispatchWithGeneratedDispatcherAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> payloadBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            // 尝试通过反射调用生成的直接分发方法
            var generatedType = FindGeneratedDispatcherType();
            if (generatedType != null)
            {
                var dispatchMethod = generatedType.GetMethod("DispatchFromBytesAsync",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(string), typeof(ReadOnlyMemory<byte>), typeof(CancellationToken) },
                    null);

                if (dispatchMethod != null)
                {
                    var task = (ValueTask<object?>)dispatchMethod.Invoke(null, new object[] { serviceName, methodName, payloadBytes, cancellationToken })!;
                    var result = await task;
                    _logger.LogTrace("生成的分发器成功处理消息: {ServiceName}.{MethodName}", serviceName, methodName);
                    return (true, result);
                }
            }

            _logger.LogDebug("未找到生成的分发方法: {ServiceName}.{MethodName}", serviceName, methodName);
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "生成的分发器处理消息时发生错误: {ServiceName}.{MethodName}, 错误: {Error}",
                serviceName, methodName, ex.Message);
            return (false, null);
        }
    }

    /// <summary>
    /// 尝试注册生成的处理器到静态分发器
    /// </summary>
    private bool TryRegisterGeneratedHandlers()
    {
        try
        {
            // 尝试通过反射调用生成的注册方法
            var generatedType = FindGeneratedDispatcherType();
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

    /// <summary>
    /// 直接从字节流分发消息 - 核心优化方法
    /// </summary>
    public async ValueTask<object?> DispatchFromBytesAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> payloadBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 优先使用编译的分发器实例
            if (_compiledDispatcher != null && _compiledDispatcher.IsInitialized)
            {
                var result = await _compiledDispatcher.DispatchFromBytesAsync(serviceName, methodName, payloadBytes, cancellationToken);
                _logger.LogTrace("编译的分发器成功处理消息: {ServiceName}.{MethodName}", serviceName, methodName);
                return result;
            }

            // 回退：尝试使用反射方式的生成分发器
            var reflectionResult = await TryDispatchWithGeneratedDispatcherAsync(serviceName, methodName, payloadBytes, cancellationToken);
            if (reflectionResult.Success)
            {
                return reflectionResult.Result;
            }

            // 回退到传统处理方式
            var messageType = GetMessageTypeForMethod(serviceName, methodName);
            if (messageType == null)
            {
                _logger.LogWarning("未找到消息类型: 服务={ServiceName}, 方法={MethodName}", serviceName, methodName);
                return new { Success = false, Error = $"未找到消息类型: {serviceName}.{methodName}" };
            }

            // 直接反序列化为目标类型
            var deserializedMessage = await DeserializeToTypeAsync(messageType, payloadBytes);

            // 使用静态分发器处理消息
            if (_isInitialized)
            {
                var staticResult = await _staticDispatcher.TryDispatchAsync(deserializedMessage, cancellationToken);
                if (staticResult != null)
                {
                    return staticResult;
                }
            }

            // 最终回退处理
            _logger.LogDebug("静态分发器无法处理消息类型 {MessageType}", messageType.Name);
            return new { Success = false, Error = "未找到处理器" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从字节流分发消息失败: 服务={ServiceName}, 方法={MethodName}", serviceName, methodName);
            return new { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 根据服务名和方法名获取消息类型
    /// </summary>
    private Type? GetMessageTypeForMethod(string serviceName, string methodName)
    {
        try
        {
            // 尝试通过反射调用生成的方法
            var generatedType = Type.GetType("PulseRPC.Generated.CompiledMessageDispatcher");
            if (generatedType != null)
            {
                var getTypeMethod = generatedType.GetMethod("GetMessageType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);

                if (getTypeMethod != null)
                {
                    return (Type?)getTypeMethod.Invoke(null, new object[] { serviceName, methodName });
                }
            }

            _logger.LogDebug("未找到生成的 GetMessageType 方法");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取消息类型时发生错误: 服务={ServiceName}, 方法={MethodName}", serviceName, methodName);
            return null;
        }
    }

    /// <summary>
    /// 将字节数组反序列化为指定类型
    /// </summary>
    private async ValueTask<object> DeserializeToTypeAsync(Type messageType, ReadOnlyMemory<byte> bytes)
    {
        // 使用泛型方法进行强类型反序列化
        var deserializeMethod = typeof(GeneratedMessageDispatcher)
            .GetMethod(nameof(DeserializeToTypeGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(messageType);

        var task = (ValueTask<object>)deserializeMethod.Invoke(this, new object[] { bytes })!;
        return await task;
    }

    /// <summary>
    /// 泛型反序列化方法
    /// </summary>
    private ValueTask<object> DeserializeToTypeGeneric<T>(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var sequence = new ReadOnlySequence<byte>(bytes);
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var result = serializer.Deserialize<T>(sequence);
            return ValueTask.FromResult<object>(result!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "反序列化类型 {TypeName} 失败", typeof(T).Name);
            throw;
        }
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
        CancellationToken cancellationToken = default)
    {
        var messageType = message.GetType();

        // 快速路径：直接类型匹配
        if (_handlers.TryGetValue(messageType, out var handler))
        {
            return await handler(message, cancellationToken);
        }

        // 慢速路径：继承类型匹配
        foreach (var kvp in _handlers)
        {
            if (kvp.Key.IsAssignableFrom(messageType))
            {
                return await kvp.Value(message, cancellationToken);
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
