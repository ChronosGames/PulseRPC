using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryPack;
using PulseRPC.Transport;

namespace PulseRPC.Messaging;

public class MessagePacketHolder
{
    public readonly MessageHeader Header;
    public readonly byte[] Payload;

    public MessagePacketHolder(MessagePacket packet)
    {
        this.Header = packet.Header;
        this.Payload = packet.Payload.ToArray();
    }
}

/// <summary>
/// 高性能消息包装器 - 零分配设计
/// 使用 Span<T> 和 Memory<T> 优化内存操作
/// </summary>
public readonly ref struct MessagePacket
{
    private static readonly MemoryPackSerializerOptions s_headerOptions =
        MemoryPackSerializerOptions.Default;

    public readonly MessageHeader Header;
    public readonly ReadOnlySpan<byte> Payload;

    public MessagePacket(MessageHeader header, ReadOnlySpan<byte> payload)
    {
        Header = header;
        Payload = payload;
    }

    /// <summary>
    /// 创建请求消息包
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessagePacket CreateRequest(string serviceName, string methodName, ReadOnlySpan<byte> payload)
    {
        var header = new MessageHeader(MessageType.Request, serviceName, methodName)
        {
            PayloadLength = payload.Length,
            Flags = MessageFlags.RequireResponse
        };
        return new MessagePacket(header, payload);
    }

    /// <summary>
    /// 创建响应消息包
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessagePacket CreateResponse(Guid messageId, ReadOnlySpan<byte> payload)
    {
        var header = new MessageHeader(MessageType.Response, string.Empty, string.Empty)
        {
            MessageId = messageId,
            PayloadLength = payload.Length
        };
        return new MessagePacket(header, payload);
    }

    /// <summary>
    /// 高性能序列化到缓冲区
    /// 使用预分配缓冲区避免内存分配
    /// </summary>
    public int WriteTo(Span<byte> buffer)
    {
        if (buffer.Length < EstimateSize())
            throw new ArgumentException("Buffer too small", nameof(buffer));

        var writer = new SpanBufferWriter(buffer);

        // 1. 序列化头部到临时缓冲区
        // Span<byte> headerBuffer = stackalloc byte[256]; // 头部通常小于256字节
        // var headerWriter = new SpanBufferWriter(headerBuffer);

        var headerBytes = MemoryPackSerializer.Serialize(Header, s_headerOptions);

        // 2. 写入头部长度 (4字节)
        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), headerBytes.Length);
        writer.Advance(4);

        // 3. 写入头部数据
        headerBytes.CopyTo(writer.GetSpan(headerBytes.Length));
        writer.Advance(headerBytes.Length);

        // 4. 写入负载数据
        Payload.CopyTo(writer.GetSpan(Payload.Length));
        writer.Advance(Payload.Length);

        return writer.WrittenCount;
    }

    /// <summary>
    /// 高性能反序列化
    /// </summary>
    public static bool TryReadFrom(ReadOnlySpan<byte> buffer, out MessagePacket packet)
    {
        packet = default;

        if (buffer.Length < 4)
            return false;

        // 1. 读取头部长度
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        if (headerLength <= 0 || headerLength > buffer.Length - 4)
            return false;

        var headerBuffer = buffer.Slice(4, headerLength);
        var payloadStart = 4 + headerLength;

        // 2. 反序列化头部
        var header = new MessageHeader();
        var deserializeSize = MemoryPackSerializer.Deserialize(headerBuffer, ref header, s_headerOptions);
        if (header!.PayloadLength != deserializeSize)
            return false;

        // 3. 验证负载长度
        if (payloadStart + header.PayloadLength > buffer.Length)
            return false;

        // 4. 提取负载
        var payload = buffer.Slice(payloadStart, header.PayloadLength);

        packet = new MessagePacket(header, payload);
        return true;
    }

    /// <summary>
    /// 估算序列化后的大小
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int EstimateSize()
    {
        // 头部长度字段(4) + 预估头部大小(128) + 负载大小
        return 4 + 128 + Payload.Length;
    }

    /// <summary>
    /// 验证消息包完整性
    /// </summary>
    public bool IsValid()
    {
        return Header.PayloadLength == Payload.Length &&
               !string.IsNullOrEmpty(Header.ServiceName) &&
               !string.IsNullOrEmpty(Header.MethodName);
    }
}

/// <summary>
/// 高性能 Span 缓冲区写入器
/// </summary>
internal ref struct SpanBufferWriter
{
    private readonly Span<byte> _buffer;
    private int _position;

    public SpanBufferWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public readonly int WrittenCount => _position;
    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer[.._position];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint)
    {
        if (_position + sizeHint > _buffer.Length)
            throw new InvalidOperationException("Buffer overflow");

        return _buffer.Slice(_position, sizeHint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count < 0 || _position + count > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        _position += count;
    }
}

// /// <summary>
// /// 消息处理结果
// /// </summary>
// public readonly struct MessageProcessingResult
// {
//     public readonly bool Success;
//     public readonly MessagePacket Packet;
//     public readonly string? ErrorMessage;
//     public readonly int BytesConsumed;
//
//     public MessageProcessingResult(bool success, MessagePacket packet, int bytesConsumed, string? errorMessage = null)
//     {
//         Success = success;
//         Packet = packet;
//         BytesConsumed = bytesConsumed;
//         ErrorMessage = errorMessage;
//     }
//
//     public static MessageProcessingResult Failure(string error) =>
//         new(false, default, 0, error);
//
//     public static MessageProcessingResult Success(MessagePacket packet, int bytesConsumed) =>
//         new(true, packet, bytesConsumed);
// }
