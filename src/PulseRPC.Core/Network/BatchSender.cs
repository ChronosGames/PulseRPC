using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Compression;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 批处理发送器
/// </summary>
public class BatchSender
{
    private readonly PipeWriter _writer;
    private readonly MessageCompressor _compressor;
    private readonly ArrayPool<byte> _arrayPool;
    private readonly int _compressionThreshold;

    public BatchSender(PipeWriter writer, MessageCompressorOptions? compressorOptions = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _compressor = new MessageCompressor(compressorOptions);
        _arrayPool = ArrayPool<byte>.Shared;
        _compressionThreshold = compressorOptions?.CompressionThreshold ?? 1024;
    }

    /// <summary>
    /// 发送消息批次
    /// </summary>
    public async ValueTask SendBatchAsync(List<MessageBatch> batches, CancellationToken cancellationToken = default)
    {
        if (batches.Count == 0)
            return;

        // 计算总大小
        var totalSize = 0;
        foreach (var batch in batches)
        {
            totalSize += batch.Data.Length + 12; // 每个消息头12字节
        }

        var buffer = _arrayPool.Rent(totalSize);

        try
        {
            var offset = 0;
            var flags = MessageFlags.Batched;

            // 写入批次数量
            BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), batches.Count);
            offset += 4;

            foreach (var batch in batches)
            {
                var messageFlags = flags;
                var data = batch.Data;

                // 检查是否需要压缩
                if (data.Length > _compressionThreshold)
                {
                    data = _compressor.Compress(data);
                    messageFlags |= MessageFlags.Compressed;
                }

                // 写入消息头
                BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), data.Length);
                offset += 4;
                BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), (int)messageFlags);
                offset += 4;
                BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), batch.MessageId);
                offset += 4;

                // 写入消息数据
                data.CopyTo(buffer.AsSpan(offset));
                offset += data.Length;
            }

            // 写入到管道
            var memory = _writer.GetMemory(offset);
            buffer.AsSpan(0, offset).CopyTo(memory.Span);
            _writer.Advance(offset);

            var result = await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsCompleted)
            {
                throw new InvalidOperationException("管道已关闭");
            }
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    /// <summary>
    /// 发送单个消息
    /// </summary>
    public ValueTask SendMessageAsync(MessageBatch message, CancellationToken cancellationToken = default)
    {
        return SendBatchAsync(new List<MessageBatch> { message }, cancellationToken);
    }
}
