using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Engine;

/// <summary>
/// TieredMessageProcessor适配器 - 提供与现有ServerHighThroughputMessageProcessor兼容的接口
/// 将现有的三级缓冲架构无缝迁移到新的TieredMessageProcessor
/// </summary>
public sealed class TieredMessageProcessorAdapter : IAsyncDisposable
{
    private readonly string _connectionId;
    private readonly IServerChannel _serverChannel;
    private readonly IMessageHandlerRegistry _handlerRegistry;
    private readonly ILogger<TieredMessageProcessorAdapter> _logger;
    
    // 核心组件
    private readonly TieredMessageProcessor _tieredProcessor;
    private readonly MessageProcessorAdapter _messageAdapter;
    
    // 兼容性统计
    private long _totalAdapterMessages;
    private long _totalConversions;

    public TieredMessageProcessorAdapter(
        string connectionId,
        IServerChannel serverChannel,
        IMessageHandlerRegistry handlerRegistry,
        IOptions<HighThroughputProcessorOptions> options,
        ILogger<TieredMessageProcessorAdapter> logger)
    {
        _connectionId = connectionId;
        _serverChannel = serverChannel;
        _handlerRegistry = handlerRegistry;
        _logger = logger;

        _messageAdapter = new MessageProcessorAdapter(handlerRegistry, logger);

        // 将原有的HighThroughputProcessorOptions转换为TieredProcessorOptions
        var tieredOptions = ConvertToTieredOptions(options.Value);
        
        // 创建TieredMessageProcessor实例
        _tieredProcessor = new TieredMessageProcessor(
            connectionId,
            tieredOptions,
            _messageAdapter.HandleMessageAsync,
            logger.CreateLogger<TieredMessageProcessor>());

        _logger.LogInformation("TieredMessageProcessor适配器已初始化：ConnectionId={ConnectionId}", _connectionId);
    }

    /// <summary>
    /// 将HighThroughputProcessorOptions转换为TieredProcessorOptions
    /// </summary>
    private TieredProcessorOptions ConvertToTieredOptions(HighThroughputProcessorOptions originalOptions)
    {
        return new TieredProcessorOptions
        {
            // L1缓冲区配置
            L1BufferSize = originalOptions.L1BufferSize,
            L1BackpressureThreshold = (int)(originalOptions.L1BufferSize * 0.8),
            
            // L2批处理配置
            L2MaxBatchSize = originalOptions.MaxBatchSize,
            L2QueueCapacity = originalOptions.L2QueueCapacity,
            L2BatchIntervalMs = originalOptions.BatchIntervalMs,
            
            // L3内存池配置
            L3SmallPoolSize = 1024,
            L3MediumPoolSize = 256,
            L3LargePoolSize = 64,
            L3MaxPooledBufferSize = 1024 * 1024, // 1MB
            
            // 背压策略配置
            NormalMessageDropRate = 0.8,
            CriticalMessageTimeoutMs = originalOptions.CriticalMessageTimeoutUs / 1000,
            L2BackpressureWaitMs = originalOptions.L2BackpressureWaitMs,
            
            // 性能监控配置
            EnablePerformanceMonitoring = true,
            EnableDetailedLogging = originalOptions.EnableDetailedLogging,
            PerformanceCheckFrequency = originalOptions.PerformanceCheckFrequency,
            BatchSoftTimeoutMs = originalOptions.BatchSoftTimeoutMs
        };
    }

    /// <summary>
    /// 尝试将消息入队 - 兼容原有的ServerHighThroughputMessageProcessor接口
    /// </summary>
    public bool TryEnqueueMessage(ServerMessage message)
    {
        System.Threading.Interlocked.Increment(ref _totalAdapterMessages);
        
        // 将ServerMessage转换为ReadOnlyMemory<byte>
        var messageData = _messageAdapter.SerializeMessage(message);
        var priority = ConvertToPriority(message.Priority);
        
        System.Threading.Interlocked.Increment(ref _totalConversions);
        
        // 委托给TieredMessageProcessor处理
        var success = _tieredProcessor.TryEnqueueMessage(messageData, priority);
        
        if (!success && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("消息入队失败：ConnectionId={ConnectionId}, SequenceId={SequenceId}, Priority={Priority}",
                _connectionId, message.SequenceId, message.Priority);
        }
        
        return success;
    }

    /// <summary>
    /// 将MessagePriority转换为新的优先级类型
    /// </summary>
    private MessagePriority ConvertToPriority(Processing.MessagePriority originalPriority)
    {
        return originalPriority switch
        {
            Processing.MessagePriority.Critical => MessagePriority.Critical,
            Processing.MessagePriority.Normal => MessagePriority.Normal,
            Processing.MessagePriority.Low => MessagePriority.Low,
            _ => MessagePriority.Normal
        };
    }

    /// <summary>
    /// 获取队列统计信息 - 兼容原有接口
    /// </summary>
    public ProcessorStats GetStats()
    {
        var tieredMetrics = _tieredProcessor.GetMetrics();
        
        return new ProcessorStats
        {
            MessagesInL1 = tieredMetrics.L1MessagesEnqueued.Value - tieredMetrics.L1MessagesDequeued.Value,
            MessagesInL2 = tieredMetrics.BatchesCreated.Value - tieredMetrics.BatchesProcessed.Value,
            MessagesInL3 = tieredMetrics.MessagesProcessed.Value - tieredMetrics.MessagesDropped.Value,
            TotalProcessed = tieredMetrics.MessagesProcessed.Value,
            TotalDropped = tieredMetrics.MessagesDropped.Value,
            TotalCriticalForced = tieredMetrics.ForcedEnqueues.Value
        };
    }

    /// <summary>
    /// 获取扩展统计信息
    /// </summary>
    public AdapterStatistics GetAdapterStatistics()
    {
        var tieredMetrics = _tieredProcessor.GetMetrics();
        var summary = tieredMetrics.GetSummary();
        
        return new AdapterStatistics
        {
            ConnectionId = _connectionId,
            
            // 适配器统计
            TotalAdapterMessages = System.Threading.Interlocked.Read(ref _totalAdapterMessages),
            TotalConversions = System.Threading.Interlocked.Read(ref _totalConversions),
            
            // TieredProcessor统计
            TieredProcessorSummary = summary,
            
            // 性能指标
            CurrentThroughput = summary.CurrentThroughput,
            AverageBatchProcessingTime = summary.AverageBatchProcessingTime,
            P95BatchProcessingTime = summary.P95BatchProcessingTime,
            L1BackpressureRate = summary.L1BackpressureRate,
            MessageErrorRate = summary.MessageErrorRate
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _tieredProcessor.DisposeAsync();
        _messageAdapter.Dispose();
        
        _logger.LogInformation("TieredMessageProcessor适配器已释放：ConnectionId={ConnectionId}, " +
            "总消息数={TotalMessages}, 总转换数={TotalConversions}",
            _connectionId, 
            System.Threading.Interlocked.Read(ref _totalAdapterMessages),
            System.Threading.Interlocked.Read(ref _totalConversions));
    }
}

/// <summary>
/// 消息处理适配器 - 在新旧消息处理接口之间进行转换
/// </summary>
internal sealed class MessageProcessorAdapter : IDisposable
{
    private readonly IMessageHandlerRegistry _handlerRegistry;
    private readonly ILogger _logger;
    private readonly MessageSerializer _serializer;

    public MessageProcessorAdapter(IMessageHandlerRegistry handlerRegistry, ILogger logger)
    {
        _handlerRegistry = handlerRegistry;
        _logger = logger;
        _serializer = new MessageSerializer();
    }

    /// <summary>
    /// 处理消息的适配方法
    /// </summary>
    public async ValueTask<ReadOnlyMemory<byte>> HandleMessageAsync(ReadOnlyMemory<byte> messageData)
    {
        try
        {
            // 反序列化消息
            var message = _serializer.DeserializeMessage(messageData);
            
            // 调用原有的处理逻辑
            var result = await _handlerRegistry.HandleAsync(message);
            
            // 序列化响应
            return _serializer.SerializeResponse(new MessageResponse
            {
                SequenceId = message.SequenceId,
                Success = true,
                Data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息处理适配器异常");
            
            // 序列化错误响应
            return _serializer.SerializeResponse(new MessageResponse
            {
                SequenceId = 0, // 无法获取序列号时使用0
                Success = false,
                ErrorCode = "ADAPTER_ERROR",
                ErrorMessage = ex.Message
            });
        }
    }

    /// <summary>
    /// 序列化消息
    /// </summary>
    public ReadOnlyMemory<byte> SerializeMessage(ServerMessage message)
    {
        return _serializer.SerializeMessage(message);
    }

    public void Dispose()
    {
        _serializer.Dispose();
    }
}

/// <summary>
/// 简化的消息序列化器
/// </summary>
internal sealed class MessageSerializer : IDisposable
{
    /// <summary>
    /// 序列化消息
    /// </summary>
    public ReadOnlyMemory<byte> SerializeMessage(ServerMessage message)
    {
        // 这里应该实现具体的序列化逻辑
        // 当前返回简化的字节数组作为占位符
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            MessageId = message.MessageId,
            SequenceId = message.SequenceId,
            Priority = message.Priority.ToString(),
            ServerTimestamp = message.ServerTimestamp,
            Type = message.GetType().Name
        });
        
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// 反序列化消息
    /// </summary>
    public ServerMessage DeserializeMessage(ReadOnlyMemory<byte> data)
    {
        // 这里应该实现具体的反序列化逻辑
        // 当前返回简化的消息对象作为占位符
        var json = System.Text.Encoding.UTF8.GetString(data.Span);
        var messageInfo = System.Text.Json.JsonSerializer.Deserialize<MessageInfo>(json);
        
        return new AdapterServerMessage
        {
            MessageId = messageInfo?.MessageId ?? Guid.NewGuid().ToString(),
            SequenceId = messageInfo?.SequenceId ?? 0,
            Priority = Enum.TryParse<Processing.MessagePriority>(messageInfo?.Priority, out var priority) 
                ? priority 
                : Processing.MessagePriority.Normal,
            ServerTimestamp = messageInfo?.ServerTimestamp ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// 序列化响应
    /// </summary>
    public ReadOnlyMemory<byte> SerializeResponse(MessageResponse response)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(response);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public void Dispose()
    {
        // 清理序列化资源
    }

    private class MessageInfo
    {
        public string? MessageId { get; set; }
        public long SequenceId { get; set; }
        public string? Priority { get; set; }
        public DateTime ServerTimestamp { get; set; }
        public string? Type { get; set; }
    }
}

/// <summary>
/// 适配器专用的服务端消息
/// </summary>
internal sealed class AdapterServerMessage : ServerMessage
{
    // 继承基类的所有属性
}

/// <summary>
/// 消息响应
/// </summary>
public class MessageResponse
{
    public long SequenceId { get; set; }
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

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