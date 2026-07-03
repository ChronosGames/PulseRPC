using System;
using System.Buffers.Binary;
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
    public static int PrepareChunkHeaders(byte[] buffer, FrameHeader messageHeader, ChunkHeader chunkHeader)
    {
        // 使用同步方法操作 Span，避免在 async 上下文中直接使用 Span
        WriteFrameHeaderSync(buffer.AsSpan(0, FrameHeader.Size), messageHeader);
        WriteChunkHeaderSync(buffer.AsSpan(FrameHeader.Size, ChunkHeader.Size), chunkHeader);
        return FrameHeader.Size + ChunkHeader.Size;
    }

    /// <summary>
    /// 准备单独的消息头
    /// </summary>
    public static int PrepareFrameHeader(byte[] buffer, FrameHeader messageHeader)
    {
        WriteFrameHeaderSync(buffer.AsSpan(0, FrameHeader.Size), messageHeader);
        return FrameHeader.Size;
    }

    /// <summary>
    /// 异步发送分片数据的优化版本
    /// </summary>
    public static async Task<bool> SendChunkAsync(
        Stream stream,
        byte[] headerBuffer,
        ReadOnlyMemory<byte> chunkData,
        FrameHeader messageHeader,
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
        FrameHeader messageHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            // 在同步上下文中准备头部数据
            var headerSize = PrepareFrameHeader(headerBuffer, messageHeader);

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
    /// 格式: [Magic:2][Length:4][MessageId:2][Flags:2]
    /// </summary>
    public static FrameHeader ReadFrameHeaderSync(ReadOnlySpan<byte> buffer)
    {
        var magic = BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(2, 4));
        var messageId = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2));
        return new FrameHeader(magic, length, messageId, flags);
    }

    /// <summary>
    /// 从缓冲区读取块头 - 同步版本
    /// </summary>
    public static ChunkHeader ReadChunkHeaderSync(ReadOnlySpan<byte> buffer)
    {
        var chunkId = BinaryPrimitives.ReadInt32LittleEndian(buffer[..4]);
        var chunkIndex = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(4, 4));
        var totalChunks = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(8, 4));
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(12, 4));
        return new ChunkHeader(chunkId, chunkIndex, totalChunks, chunkSize);
    }

    #region 同步方法

    /// <summary>
    /// 将消息头写入缓冲区 - 同步版本
    /// 格式: [Magic:2][Length:4][MessageId:2][Flags:2]
    /// </summary>
    public static void WriteFrameHeaderSync(Span<byte> buffer, FrameHeader header)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, header.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[2..], header.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[6..], header.MessageId);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[8..], header.Flags);
    }

    private static void WriteChunkHeaderSync(Span<byte> buffer, ChunkHeader header)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer, header.ChunkId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], header.ChunkIndex);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], header.TotalChunks);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], header.ChunkSize);
    }

    #endregion
}
