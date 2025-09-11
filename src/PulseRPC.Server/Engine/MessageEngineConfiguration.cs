using Microsoft.Extensions.Logging;
using PulseRPC.Transport;

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
/// 消息分发器接口 - 替代 IMessageHandlerRegistry
/// 根据优化计划书 5.1.3 调度器重命名策略
/// </summary>
public interface IMessageDispatcher
{
    /// <summary>
    /// 处理消息
    /// </summary>
    /// <param name="message">要处理的消息</param>
    /// <returns>处理结果</returns>
    Task<object?> HandleAsync(ServerMessage message);
}

/// <summary>
/// 编译时消息分发器 - 替代 DefaultMessageHandlerRegistry
/// 根据优化计划书 5.1.3 调度器重命名策略
/// </summary>
public class CompiledMessageDispatcher : IMessageDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CompiledMessageDispatcher> _logger;

    public CompiledMessageDispatcher(
        IServiceProvider serviceProvider,
        ILogger<CompiledMessageDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<object?> HandleAsync(ServerMessage message)
    {
        try
        {
            // TODO: 这里将通过Source Generator生成编译时分发逻辑
            // 目前使用临时实现保持兼容性
            _logger.LogDebug("处理消息: {MessageType}", message.GetType().Name);

            // 返回默认响应
            return new { Success = true, Message = "Processed by CompiledMessageDispatcher" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息处理失败: {MessageType}", message.GetType().Name);
            return new { Success = false, Error = ex.Message };
        }
    }
}
