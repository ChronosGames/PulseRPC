using PulseRPC.Server.Engine;
using PulseRPC.Server.Scheduling;
using PulseRPC.Transport;

namespace PulseRPC.Server.Memory;

/// <summary>
/// 消息信封
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

