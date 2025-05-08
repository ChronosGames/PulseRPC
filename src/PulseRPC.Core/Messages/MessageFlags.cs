using System;

namespace PulseRPC.Protocol.Messages;

/// <summary>
/// 消息标志位
/// </summary>
[Flags]
public enum MessageFlags : byte
{
    /// <summary>
    /// 无标志
    /// </summary>
    None = 0,

    /// <summary>
    /// 心跳消息
    /// </summary>
    Heartbeat = 1 << 0,

    /// <summary>
    /// 已压缩
    /// </summary>
    Compressed = 1 << 1,

    /// <summary>
    /// 分片消息
    /// </summary>
    Fragmented = 1 << 2,

    /// <summary>
    /// 批处理消息
    /// </summary>
    Batched = 1 << 3,

    /// <summary>
    /// 系统消息
    /// </summary>
    System = 1 << 4,

    /// <summary>
    /// 请求消息
    /// </summary>
    Request = 1 << 5,

    /// <summary>
    /// 响应消息
    /// </summary>
    Response = 1 << 6,

    /// <summary>
    /// 错误响应
    /// </summary>
    Error = 1 << 7
}
