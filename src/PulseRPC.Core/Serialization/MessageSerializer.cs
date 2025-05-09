using System;
using MemoryPack;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// 消息序列化器
/// </summary>
public static class MessageSerializer
{
    private const byte ProtocolVersion = 1;

    /// <summary>
    /// 序列化消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="message">消息对象</param>
    /// <param name="flags">消息标志</param>
    /// <param name="sequenceId">消息序列号</param>
    /// <returns>序列化后的字节数组</returns>
    public static byte[] Serialize<T>(T message, MessageFlags flags = MessageFlags.None, int sequenceId = 0) where T : class, IMessage
    {
        // 获取消息ID
        var messageId = PulseRPCFormatterProvider.GetMessageId<T>();

        // 序列化消息体
        var payload = MemoryPackSerializer.Serialize(message);

        // 尝试压缩
        var (compressed, finalPayload) = MessageCompressor.TryCompress(payload);
        if (compressed)
        {
            flags |= MessageFlags.Compressed;
        }

        // 创建消息包
        var packet = new MessagePacket
        {
            Header = new MessagePacket.MessageHeader
            {
                Version = ProtocolVersion,
                MessageId = messageId,
                Flags = flags,
                SequenceId = sequenceId
            },
            Payload = finalPayload
        };

        // 序列化整个消息包
        return MemoryPackSerializer.Serialize(packet);
    }

    /// <summary>
    /// 反序列化消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="data">序列化数据</param>
    /// <returns>消息对象和消息包信息</returns>
    public static (T? Message, MessagePacket.MessageHeader Header) Deserialize<T>(ReadOnlySpan<byte> data) where T : class, IMessage
    {
        // 反序列化消息包
        var packet = MemoryPackSerializer.Deserialize<MessagePacket>(data);
        if (packet == null)
        {
            throw new MessageDeserializationException("无法反序列化消息包");
        }

        // 验证协议版本
        if (packet.Header.Version != ProtocolVersion)
        {
            throw new MessageDeserializationException($"不支持的协议版本: {packet.Header.Version}");
        }

        // 处理压缩
        var messageData = packet.Payload;
        if ((packet.Header.Flags & MessageFlags.Compressed) != 0)
        {
            messageData = MessageCompressor.Decompress(packet.Payload);
        }

        // 反序列化消息体
        var message = MemoryPackSerializer.Deserialize<T>(messageData);

        return (message, packet.Header);
    }

    /// <summary>
    /// 反序列化消息头
    /// </summary>
    /// <param name="data">序列化数据</param>
    /// <returns>消息头信息</returns>
    public static MessagePacket.MessageHeader DeserializeHeader(ReadOnlySpan<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<MessagePacket>(data);
        if (packet == null)
        {
            throw new MessageDeserializationException("无法反序列化消息包");
        }
        return packet.Header;
    }

    /// <summary>
    /// 获取消息ID
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <returns>消息ID</returns>
    public static int GetMessageId<T>() where T : class, IMessage
    {
        return PulseRPCFormatterProvider.GetMessageId<T>();
    }
}

public class MessageDeserializationException : Exception
{
    public MessageDeserializationException(string message) : base(message) { }

    public MessageDeserializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
