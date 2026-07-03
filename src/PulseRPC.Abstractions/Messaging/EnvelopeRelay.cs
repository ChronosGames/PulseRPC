using System;
using System.Buffers.Binary;
using MemoryPack;

namespace PulseRPC.Messaging;

/// <summary>
/// 只读信封头「地址中转」原语 —— 供网关（Gateway）作纯中转。
/// </summary>
/// <remarks>
/// <para>
/// 本类型让网关能够：
/// </para>
/// <list type="number">
/// <item><description>仅解析信封头（<c>Hub</c> / <c>Key</c> / <c>MethodId</c>），<strong>不反序列化消息体（body）</strong>；</description></item>
/// <item><description>据此决定目标节点/实例，然后转发原始帧（或改写头部后重新组帧，body 原样拼接）。</description></item>
/// </list>
/// <para>
/// 线格式与 <see cref="MessagePacket"/> 完全一致：
/// <c>[4 字节小端 headerLen][MemoryPack(MessageHeader)][body(opaque)]</c>。
/// </para>
/// <para>
/// <strong>纯转发</strong>：若无需改写头部，网关直接把收到的原始帧字节转发给目标节点即可（零拷贝、零反序列化 body）。
/// 需要改写寻址（如规范化实例键）时，使用 <see cref="ReadOnlyEnvelopeHeader.WithKey"/> 得到新头，再用
/// <see cref="TryWriteFrame"/> / <see cref="WriteFrame"/> 重新组帧，body 不被触碰。
/// </para>
/// </remarks>
/// <seealso cref="ReadOnlyEnvelopeHeader"/>
public static class EnvelopeRelay
{
    private static readonly MemoryPackSerializerOptions s_headerOptions =
        MemoryPackSerializerOptions.Default;

    /// <summary>
    /// 帧头长度前缀所占字节数（小端 <see cref="int"/>）。
    /// </summary>
    public const int HeaderLengthPrefixSize = 4;

    /// <summary>
    /// 仅解析信封头；消息体（<paramref name="body"/>）以 opaque 切片返回，<strong>不做反序列化</strong>。
    /// </summary>
    /// <param name="frame">完整帧字节：<c>[4 字节 headerLen][header][body]</c>。</param>
    /// <param name="header">解析出的只读信封头。</param>
    /// <param name="body">消息体的原始切片（未反序列化，可能为空）。</param>
    /// <returns>成功返回 <c>true</c>；帧不完整/损坏返回 <c>false</c>。</returns>
    public static bool TryReadHeader(
        ReadOnlySpan<byte> frame,
        out ReadOnlyEnvelopeHeader header,
        out ReadOnlySpan<byte> body)
    {
        header = default;
        body = default;

        if (!MessagePacket.TryReadFrom(frame, out var packet))
        {
            return false;
        }

        header = ReadOnlyEnvelopeHeader.FromHeader(packet.Header);
        body = packet.Payload;
        return true;
    }

    /// <summary>
    /// 仅解析信封头（<see cref="ReadOnlyMemory{T}"/> 版，便于在异步转发时保留 body 的内存句柄）；
    /// 消息体以 opaque 切片返回，<strong>不做反序列化</strong>。
    /// </summary>
    /// <param name="frame">完整帧字节：<c>[4 字节 headerLen][header][body]</c>。</param>
    /// <param name="header">解析出的只读信封头。</param>
    /// <param name="body">消息体的原始内存切片（未反序列化，可能为空）。</param>
    /// <returns>成功返回 <c>true</c>；帧不完整/损坏返回 <c>false</c>。</returns>
    public static bool TryReadHeader(
        ReadOnlyMemory<byte> frame,
        out ReadOnlyEnvelopeHeader header,
        out ReadOnlyMemory<byte> body)
    {
        header = default;
        body = default;

        var span = frame.Span;
        if (!MessagePacket.TryReadFrom(span, out var packet))
        {
            return false;
        }

        // TryReadFrom 已校验 headerLength 的合法性，这里复用同一长度前缀切出 body 的 Memory 视图。
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(span);
        header = ReadOnlyEnvelopeHeader.FromHeader(packet.Header);
        body = frame.Slice(HeaderLengthPrefixSize + headerLength);
        return true;
    }

    /// <summary>
    /// 计算用 <paramref name="header"/> + 指定 <paramref name="bodyLength"/> 重新组帧所需的精确字节数。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bodyLength"/> 为负。</exception>
    public static int GetFrameSize(in ReadOnlyEnvelopeHeader header, int bodyLength)
    {
        if (bodyLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bodyLength));
        }

        var headerBytes = MemoryPackSerializer.Serialize(header.ToHeader(), s_headerOptions);
        return HeaderLengthPrefixSize + headerBytes.Length + bodyLength;
    }

    /// <summary>
    /// 用（可改写的）只读头 + 原样 <paramref name="body"/> 重新组帧写入 <paramref name="destination"/>，
    /// <strong>不触碰 body 内容</strong>。
    /// </summary>
    /// <param name="header">要写入的只读信封头（可来自 <see cref="ReadOnlyEnvelopeHeader.WithKey"/> 等改写）。</param>
    /// <param name="body">原样拼接的消息体字节。</param>
    /// <param name="destination">目标缓冲区。</param>
    /// <param name="bytesWritten">实际写入的字节数。</param>
    /// <returns>缓冲区足够并写入成功返回 <c>true</c>；否则返回 <c>false</c>（不写入）。</returns>
    public static bool TryWriteFrame(
        in ReadOnlyEnvelopeHeader header,
        ReadOnlySpan<byte> body,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;

        var headerBytes = MemoryPackSerializer.Serialize(header.ToHeader(), s_headerOptions);
        var total = HeaderLengthPrefixSize + headerBytes.Length + body.Length;
        if (destination.Length < total)
        {
            return false;
        }

        BinaryPrimitives.WriteInt32LittleEndian(destination, headerBytes.Length);
        headerBytes.CopyTo(destination.Slice(HeaderLengthPrefixSize));
        body.CopyTo(destination.Slice(HeaderLengthPrefixSize + headerBytes.Length));
        bytesWritten = total;
        return true;
    }

    /// <summary>
    /// 便捷方法：用（可改写的）只读头 + 原样 <paramref name="body"/> 重新组帧为新数组，
    /// <strong>不触碰 body 内容</strong>。
    /// </summary>
    public static byte[] WriteFrame(in ReadOnlyEnvelopeHeader header, ReadOnlySpan<byte> body)
    {
        var headerBytes = MemoryPackSerializer.Serialize(header.ToHeader(), s_headerOptions);
        var buffer = new byte[HeaderLengthPrefixSize + headerBytes.Length + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, headerBytes.Length);
        headerBytes.CopyTo(buffer.AsSpan(HeaderLengthPrefixSize));
        body.CopyTo(buffer.AsSpan(HeaderLengthPrefixSize + headerBytes.Length));
        return buffer;
    }
}
