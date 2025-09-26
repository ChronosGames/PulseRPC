using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using PulseRPC.Transport.Tcp;

namespace PulseRPC.Transport;

/// <summary>
/// 帮助类：在异步上下文中安全使用 Span<T>
/// 通过将 Span 操作封装在同步方法中来避免 async/await 限制
/// </summary>
internal static class AsyncSpanHelper
{
    /// <summary>
    /// 准备消息头和块头的组合缓冲区
    /// </summary>
    public static int PrepareChunkHeaders(byte[] buffer, MessageHeader messageHeader, ChunkHeader chunkHeader)
    {
        // 使用同步方法操作 Span，避免在 async 上下文中直接使用 Span
        WriteMessageHeaderSync(buffer.AsSpan(0, MessageHeader.Size), messageHeader);
        WriteChunkHeaderSync(buffer.AsSpan(MessageHeader.Size, ChunkHeader.Size), chunkHeader);
        return MessageHeader.Size + ChunkHeader.Size;
    }

    /// <summary>
    /// 准备单独的消息头
    /// </summary>
    public static int PrepareMessageHeader(byte[] buffer, MessageHeader messageHeader)
    {
        WriteMessageHeaderSync(buffer.AsSpan(0, MessageHeader.Size), messageHeader);
        return MessageHeader.Size;
    }

    /// <summary>
    /// 异步发送分片数据的优化版本
    /// </summary>
    public static async Task<bool> SendChunkAsync(
        Stream stream,
        byte[] headerBuffer,
        ReadOnlyMemory<byte> chunkData,
        MessageHeader messageHeader,
        ChunkHeader chunkHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            // 在同步上下文中准备头部数据
            var headerSize = PrepareChunkHeaders(headerBuffer, messageHeader, chunkHeader);

            // 异步发送头部
            await stream.WriteAsync(headerBuffer, 0, headerSize, cancellationToken);

            // 异步发送分片数据
            await stream.WriteAsync(chunkData, cancellationToken);

            // 异步刷新
            await stream.FlushAsync(cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 异步发送小包数据的优化版本
    /// </summary>
    public static async Task<bool> SendSmallPacketAsync(
        Stream stream,
        byte[] headerBuffer,
        ReadOnlyMemory<byte> data,
        MessageHeader messageHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            // 在同步上下文中准备头部数据
            var headerSize = PrepareMessageHeader(headerBuffer, messageHeader);

            // 异步发送头部
            await stream.WriteAsync(headerBuffer, 0, headerSize, cancellationToken);

            // 异步发送数据
            await stream.WriteAsync(data, cancellationToken);

            // 异步刷新
            await stream.FlushAsync(cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从缓冲区读取消息头 - 同步版本
    /// </summary>
    public static MessageHeader ReadMessageHeaderSync(ReadOnlySpan<byte> buffer)
    {
        var length = BitConverter.ToInt32(buffer[..4]);
        var messageId = BitConverter.ToUInt16(buffer.Slice(4, 2));
        var flags = BitConverter.ToUInt16(buffer.Slice(6, 2));
        return new MessageHeader(length, messageId, flags);
    }

    /// <summary>
    /// 从缓冲区读取块头 - 同步版本
    /// </summary>
    public static ChunkHeader ReadChunkHeaderSync(ReadOnlySpan<byte> buffer)
    {
        var chunkId = BitConverter.ToInt32(buffer[..4]);
        var chunkIndex = BitConverter.ToInt32(buffer.Slice(4, 4));
        var totalChunks = BitConverter.ToInt32(buffer.Slice(8, 4));
        var chunkSize = BitConverter.ToInt32(buffer.Slice(12, 4));
        return new ChunkHeader(chunkId, chunkIndex, totalChunks, chunkSize);
    }

    #region 私有同步方法

    private static void WriteMessageHeaderSync(Span<byte> buffer, MessageHeader header)
    {
        BitConverter.GetBytes(header.Length).CopyTo(buffer);
        BitConverter.GetBytes(header.MessageId).CopyTo(buffer[4..]);
        BitConverter.GetBytes(header.Flags).CopyTo(buffer[6..]);
    }

    private static void WriteChunkHeaderSync(Span<byte> buffer, ChunkHeader header)
    {
        BitConverter.GetBytes(header.ChunkId).CopyTo(buffer);
        BitConverter.GetBytes(header.ChunkIndex).CopyTo(buffer[4..]);
        BitConverter.GetBytes(header.TotalChunks).CopyTo(buffer[8..]);
        BitConverter.GetBytes(header.ChunkSize).CopyTo(buffer[12..]);
    }

    #endregion
}
