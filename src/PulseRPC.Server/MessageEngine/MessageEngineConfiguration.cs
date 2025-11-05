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
using PulseRPC.Server.MessageEngine;

namespace PulseRPC.Server.MessageEngine;

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
    /// 普通消息丢弃阈值，默认0.8
    /// </summary>
    public double NormalMessageDropThreshold { get; set; } = 0.8;

    /// <summary>
    /// 负载均衡模式，默认轮询
    /// </summary>
    public LoadBalancingMode LoadBalancingMode { get; set; } = LoadBalancingMode.RoundRobin;

    /// <summary>
    /// 启用回退处理路径，默认true
    /// </summary>
    public bool EnableFallbackProcessing { get; set; } = true;

    /// <summary>
    /// 背压阻塞超时（毫秒），默认10ms
    /// </summary>
    public int BackpressureBlockTimeoutMs { get; set; } = 10;

    /// <summary>
    /// 最大重试次数，默认3次
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
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
