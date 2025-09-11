using PulseRPC.Server.Engine;
using PulseRPC.Transport;

namespace PulseRPC.Server.Memory;

/// <summary>
/// 消息信封 - 替代 MessageSlot
/// 根据优化计划书 5.1.2 缓冲管理重命名策略：MessageSlot → MessageEnvelope
/// </summary>
public struct MessageEnvelope
{
    public ServerMessage Message;
    public long SequenceId;
    public long EnqueueTime;
    public MessageStatus Status;
    public MessagePriority Priority;
    public ReadOnlyMemory<byte> Data;

    /// <summary>
    /// 消息大小（字节）
    /// </summary>
    public int Size => Data.Length;

    /// <summary>
    /// 是否为关键消息
    /// </summary>
    public bool IsCritical => Priority == MessagePriority.Critical;
}

/// <summary>
/// 消息状态枚举
/// </summary>
public enum MessageStatus : byte
{
    /// <summary>
    /// 等待处理
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 正在处理
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 处理完成
    /// </summary>
    Completed = 2,

    /// <summary>
    /// 处理失败
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 关键消息
    /// </summary>
    Critical = 4
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
/// 处理器统计信息 - 兼容性类型
/// 
/// REMOVEME: 此兼容性类型将在下一个主要版本中移除
/// </summary>
public class ProcessorStats
{
    /// <summary>
    /// L1缓冲区中的消息数
    /// </summary>
    public long MessagesInL1 { get; set; }
    
    /// <summary>
    /// L2队列中的消息数
    /// </summary>
    public long MessagesInL2 { get; set; }
    
    /// <summary>
    /// L3队列中的消息数
    /// </summary>
    public long MessagesInL3 { get; set; }
    
    /// <summary>
    /// 总处理消息数
    /// </summary>
    public long TotalProcessed { get; set; }
    
    /// <summary>
    /// 总丢弃消息数
    /// </summary>
    public long TotalDropped { get; set; }
    
    /// <summary>
    /// 关键消息强制入队数
    /// </summary>
    public long TotalCriticalForced { get; set; }
    
    /// <summary>
    /// 当前吞吐量（消息/秒）
    /// </summary>
    public double Throughput { get; set; }
    
    /// <summary>
    /// 当前吞吐量（消息/秒）- 兼容性属性
    /// </summary>
    public double CurrentThroughput { get => Throughput; set => Throughput = value; }
    
    /// <summary>
    /// 平均延迟
    /// </summary>
    public TimeSpan AverageLatency { get; set; }
    
    /// <summary>
    /// 平均延迟（毫秒）- 兼容性属性
    /// </summary>
    public double AverageLatencyMs { get => AverageLatency.TotalMilliseconds; set => AverageLatency = TimeSpan.FromMilliseconds(value); }
    
    /// <summary>
    /// P95延迟
    /// </summary>
    public TimeSpan P95Latency { get; set; }
}
