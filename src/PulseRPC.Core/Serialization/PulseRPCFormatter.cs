using MemoryPack;
using System.Buffers;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// PulseRPC 消息格式化器
/// </summary>
public class PulseRPCFormatter<T> : IMemoryPackFormatter<T> where T : class, IMessage
{
    private readonly int _messageId;

    public PulseRPCFormatter(int messageId)
    {
        _messageId = messageId;
    }

    void IMemoryPackFormatter<T>.Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        // 写入消息头
        writer.WriteObjectHeader(2); // 2 个字段：消息ID和消息体
        writer.WriteUnmanaged(_messageId);

        // 写入消息体
        writer.WriteValue(value);
    }

    void IMemoryPackFormatter<T>.Deserialize(ref MemoryPackReader reader, scoped ref T? value)
    {
        // 检查是否为 null
        if (reader.PeekIsNull())
        {
            value = default;
            return;
        }

        // 读取对象头
        if (!reader.TryReadObjectHeader(out var count) || count != 2)
        {
            throw new MemoryPackSerializationException("Invalid message format");
        }

        // 读取消息ID
        var messageId = reader.ReadUnmanaged<int>();
        if (messageId != _messageId)
        {
            throw new MemoryPackSerializationException($"Message ID mismatch. Expected: {_messageId}, Actual: {messageId}");
        }

        // 读取消息体
        value = reader.ReadValue<T>();
    }
}
