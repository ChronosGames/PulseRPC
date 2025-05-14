using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 支持零复制的消息帧处理器
/// </summary>
public class FrameProcessor
{
    // 帧头部大小
    private const int FrameHeaderSize = 8;

    // 用于解析接收到的帧
    private readonly PipeReader _reader;

    // 处理帧的回调
    private readonly Func<ReadOnlySequence<byte>, bool, Task> _frameHandler;

    // 取消令牌
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// 构造函数
    /// </summary>
    public FrameProcessor(PipeReader reader, Func<ReadOnlySequence<byte>, bool, Task> frameHandler,
        CancellationToken cancellationToken)
    {
        _reader = reader;
        _frameHandler = frameHandler;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// 开始处理帧
    /// </summary>
    public async Task ProcessFramesAsync()
    {
        try
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                var result = await _reader.ReadAsync(_cancellationToken);
                var buffer = result.Buffer;

                var consumed = buffer.Start;

                try
                {
                    if (result.IsCanceled)
                    {
                        break;
                    }

                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        break;
                    }

                    while (TryReadFrame(ref buffer, out var frameData, out var isCompressed))
                    {
                        await _frameHandler(frameData, isCompressed);
                        consumed = buffer.Start;
                    }
                }
                finally
                {
                    _reader.AdvanceTo(consumed, buffer.End);
                }

                if (result.IsCompleted)
                    break;
            }
        }
        finally
        {
            await _reader.CompleteAsync();
        }
    }

    /// <summary>
    /// 尝试从缓冲区读取一个帧
    /// </summary>
    private static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frameData,
        out bool isCompressed)
    {
        frameData = default;
        isCompressed = false;

        // 确保有足够数据读取帧头
        if (buffer.Length < FrameHeaderSize)
        {
            return false;
        }

        // 读取帧头
        Span<byte> headerSpan = stackalloc byte[FrameHeaderSize];
        buffer.Slice(0, FrameHeaderSize).CopyTo(headerSpan);

        // 提取帧长度和标志
        var frameLength = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan);
        var frameType = headerSpan[2];
        var frameFlags = headerSpan[3];

        // 检查长度有效性
        if (frameLength < FrameHeaderSize)
        {
            throw new InvalidDataException($"Invalid frame length: {frameLength}");
        }

        // 确保有完整帧数据
        if (buffer.Length < frameLength)
        {
            return false;
        }

        // 提取帧数据(不包括头部)
        frameData = buffer.Slice(FrameHeaderSize, frameLength - FrameHeaderSize);

        // 检查是否压缩
        isCompressed = (frameFlags & (byte)PacketFlags.Compressed) != 0;

        // 更新缓冲区位置
        buffer = buffer.Slice(frameLength);

        return true;
    }

    /// <summary>
    /// 使用零复制API发送数据帧
    /// </summary>
    public static ValueTask<FlushResult> WriteMarshaledFrameAsync(PipeWriter writer, ReadOnlyMemory<byte> data,
        bool compress, MessageType type, uint sequenceId, uint? requestId = null)
    {
        // 计算总长度
        var headerSize = PacketHeader.GetHeaderSize(type);
        var totalLength = headerSize + data.Length;
        if (totalLength > ushort.MaxValue)
        {
            throw new InvalidDataException($"Frame size exceeds maximum limit: {totalLength} > {ushort.MaxValue}.");
        }

        // 获取足够的内存
        var memory = writer.GetMemory(totalLength);

        // 写入帧头
        var span = memory.Span;
        BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)totalLength);
        span[2] = (byte)type;
        span[3] = compress ? (byte)PacketFlags.Compressed : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], sequenceId);

        if (requestId.HasValue)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[8..], requestId.Value);
        }

        // 复制数据体 - 这是唯一的复制操作
        data.Span.CopyTo(span[headerSize..]);

        // 前进写入器位置
        writer.Advance(totalLength);

        // 刷新并返回结果
        return writer.FlushAsync();
    }
}
