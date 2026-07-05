using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using MemoryPack;
using PulseRPC.Shared;

namespace PulseRPC.Messaging;

/// <summary>
/// 消息包持有者 —— 承载一条已解析消息的头部与载荷，供跨线程的消息处理管道消费。
/// <para>
/// <strong>所有权/生命周期（P1-8）</strong>：由 <see cref="MessagePacket"/> 构造时，载荷会被复制进
/// <see cref="System.Buffers.MemoryPool{T}"/> 池化缓冲（取代旧的 <c>Payload.ToArray()</c> 每消息堆分配），
/// 并由本对象持有其 <see cref="System.Buffers.IMemoryOwner{T}"/>。调用方在**处理完成后必须调用 <see cref="Dispose"/>**
/// 以将缓冲归还池；<see cref="Dispose"/> 幂等（内部用 <see cref="System.Threading.Interlocked"/> 保护），
/// 重复调用安全、不会二次归还。
/// </para>
/// <para>
/// <strong>访问约束</strong>：<see cref="Payload"/> 仅在 <see cref="Dispose"/> 之前有效；处理器必须在
/// 消息处理（handler 调用）期间完成对载荷的消费（如反序列化），不得在 <see cref="Dispose"/> 后或
/// 处理返回后仍引用 <see cref="Payload"/>。
/// </para>
/// </summary>
public sealed class MessagePacketHolder : IDisposable
{
    public readonly MessageHeader Header;
    public readonly string? ConnectionId;

    private readonly IMemoryOwner<byte>? _owner;
    private readonly ReadOnlyMemory<byte> _payload;
    private int _disposed;

    /// <summary>
    /// 消息载荷。生命周期受本持有者管辖：<see cref="Dispose"/> 之后不得访问。
    /// </summary>
    public ReadOnlyMemory<byte> Payload => _payload;

    /// <summary>
    /// 从 <see cref="MessagePacket"/> 构造：将载荷复制进池化缓冲并持有其所有者。
    /// </summary>
    public MessagePacketHolder(MessagePacket packet, string? connectionId = null)
    {
        this.Header = packet.Header;
        this.ConnectionId = connectionId;

        var source = packet.Payload;
        if (source.Length == 0)
        {
            _owner = null;
            _payload = ReadOnlyMemory<byte>.Empty;
        }
        else
        {
            // P1-8：池化取代 ToArray，消除每消息堆分配。
            var owner = MemoryPool<byte>.Shared.Rent(source.Length);
            source.CopyTo(owner.Memory.Span);
            _owner = owner;
            _payload = owner.Memory.Slice(0, source.Length);
        }
    }

    /// <summary>
    /// 从外部已拥有的字节数组构造（非池化路径）：<see cref="Dispose"/> 不归还该数组。
    /// </summary>
    public MessagePacketHolder(MessageHeader header, byte[] payload, string? connectionId = null)
    {
        this.Header = header;
        this.ConnectionId = connectionId;
        _owner = null;
        _payload = payload;
    }

    /// <summary>
    /// 归还池化缓冲。幂等：重复调用安全，不会二次归还。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner?.Dispose();
        }
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
            Flags = MessageFlags.RequireResponse
        };
        return new MessagePacket(header, payload);
    }

    /// <summary>
    /// 创建请求消息包 - 零拷贝版本（源生成器专用）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessagePacket CreateRequestZeroCopy(string serviceName, string methodName, ReadOnlySpan<byte> serializedPayload)
    {
        var header = new MessageHeader(MessageType.Request, serviceName, methodName)
        {
            MessageId = Guid.NewGuid(),
            Flags = MessageFlags.RequireResponse
        };
        return new MessagePacket(header, serializedPayload);
    }

    /// <summary>
    /// 创建命令消息包 - 零拷贝版本（源生成器专用）
    /// 命令/OneWay 消息无需服务器响应
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessagePacket CreateCommandZeroCopy(string serviceName, string methodName, ReadOnlySpan<byte> serializedPayload)
    {
        var header = new MessageHeader(MessageType.OneWay, serviceName, methodName)
        {
            MessageId = Guid.NewGuid(),
            Flags = MessageFlags.None // 无需响应标志
        };
        return new MessagePacket(header, serializedPayload);
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
        if (headerLength != deserializeSize)
            return false;

        // 3. 验证负载长度
        int payloadSize = buffer.Length - payloadStart;
        if (payloadStart + payloadSize > buffer.Length)
            return false;

        // 4. 提取负载
        var payload = buffer.Slice(payloadStart, payloadSize);

        packet = new MessagePacket(header!, payload);
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
        return Header.ProtocolId != 0;
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
