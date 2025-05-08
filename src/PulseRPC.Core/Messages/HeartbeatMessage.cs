using MemoryPack;
using PulseRPC.Protocol.Attributes;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Protocol.Messages;

/// <summary>
/// 心跳消息
/// </summary>
[Message(1, MessageType.Internal)]
[MemoryPackable]
public partial class HeartbeatMessage : IMessage, IResettable
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 序列号
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// 重置对象状态
    /// </summary>
    public void Reset()
    {
        Timestamp = 0;
        Sequence = 0;
    }
}

/// <summary>
/// 心跳响应消息
/// </summary>
[Message(2, MessageType.Internal)]
[MemoryPackable]
public partial class HeartbeatResponse : IMessage, IResettable
{
    /// <summary>
    /// 原始时间戳
    /// </summary>
    public long OriginalTimestamp { get; set; }

    /// <summary>
    /// 响应时间戳
    /// </summary>
    public long ResponseTimestamp { get; set; }

    /// <summary>
    /// 序列号
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// 重置对象状态
    /// </summary>
    public void Reset()
    {
        OriginalTimestamp = 0;
        ResponseTimestamp = 0;
        Sequence = 0;
    }
}
