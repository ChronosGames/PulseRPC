using System;
using System.Buffers;
using MemoryPack;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// 消息包装类，用于统一消息格式
/// </summary>
[MemoryPackable]
public partial class MessagePacket
{
    /// <summary>
    /// 消息头
    /// </summary>
    [MemoryPackable]
    public partial struct MessageHeader
    {
        /// <summary>
        /// 协议版本号
        /// </summary>
        public byte Version { get; set; }

        /// <summary>
        /// 消息ID
        /// </summary>
        public int MessageId { get; set; }

        /// <summary>
        /// 消息标志位
        /// </summary>
        public MessageFlags Flags { get; set; }

        /// <summary>
        /// 消息序列号（用于请求-响应匹配）
        /// </summary>
        public int SequenceId { get; set; }
    }

    /// <summary>
    /// 消息头
    /// </summary>
    public MessageHeader Header { get; set; }

    /// <summary>
    /// 消息体
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
