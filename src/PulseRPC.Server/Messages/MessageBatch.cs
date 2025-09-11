using System;

namespace PulseRPC.Server.Messages;

/// <summary>
/// 消息批次基类 - 统一的消息批处理抽象
/// </summary>
public abstract class MessageBatchBase
{
    /// <summary>
    /// 批次唯一标识符
    /// </summary>
    public string BatchId { get; set; } = "";
    
    /// <summary>
    /// 批次创建时间戳
    /// </summary>
    public long CreateTime { get; set; }
    
    /// <summary>
    /// 连接标识符
    /// </summary>
    public string ConnectionId { get; set; } = "";
    
    /// <summary>
    /// 批次大小（消息数量）
    /// </summary>
    public abstract int Count { get; }
}