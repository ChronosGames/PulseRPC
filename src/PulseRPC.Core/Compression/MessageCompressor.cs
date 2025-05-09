using System;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.IO;
using System.IO.Compression;

namespace PulseRPC.Protocol.Compression;

/// <summary>
/// 消息压缩器，使用 Brotli 算法
/// </summary>
public static class MessageCompressor
{
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// 压缩消息
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <param name="compressionLevel">压缩等级</param>
    /// <returns>压缩后的数据</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        using var outputStream = new MemoryStream();
        using var compressor = new BrotliStream(outputStream, compressionLevel);

        // 写入原始大小
        var sizeBytes = new byte[4];
        BitConverter.TryWriteBytes(sizeBytes, data.Length);
        outputStream.Write(sizeBytes, 0, sizeBytes.Length);

        // 压缩数据
        var buffer = _arrayPool.Rent(data.Length);
        try
        {
            data.CopyTo(buffer);
            compressor.Write(buffer, 0, data.Length);
            compressor.Flush();
            return outputStream.ToArray();
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    /// <summary>
    /// 解压缩消息
    /// </summary>
    /// <param name="compressedData">压缩数据</param>
    /// <returns>解压后的数据</returns>
    public static byte[] Decompress(ReadOnlySpan<byte> compressedData)
    {
        // 读取原始大小
        var originalSize = BitConverter.ToInt32(compressedData[..4]);

        // 分配缓冲区
        var buffer = _arrayPool.Rent(originalSize);
        try
        {
            using var inputStream = new MemoryStream(compressedData[4..].ToArray());
            using var decompressor = new BrotliStream(inputStream, CompressionMode.Decompress);

            var bytesRead = decompressor.Read(buffer, 0, originalSize);
            if (bytesRead != originalSize)
            {
                throw new InvalidDataException("解压缩后的数据大小不匹配");
            }

            var result = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, result, 0, bytesRead);
            return result;
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    /// <summary>
    /// 异步压缩消息
    /// </summary>
    public static async ValueTask<byte[]> CompressAsync(ReadOnlyMemory<byte> data, CompressionLevel compressionLevel = CompressionLevel.Fastest, CancellationToken cancellationToken = default)
    {
        using var outputStream = new MemoryStream();
        await using var compressor = new BrotliStream(outputStream, compressionLevel);

        // 写入原始大小
        var sizeBytes = new byte[4];
        BitConverter.TryWriteBytes(sizeBytes, data.Length);
        await outputStream.WriteAsync(sizeBytes, 0, sizeBytes.Length, cancellationToken).ConfigureAwait(false);

        // 压缩数据
        var buffer = _arrayPool.Rent(data.Length);
        try
        {
            data.CopyTo(buffer);
            await compressor.WriteAsync(buffer, 0, data.Length, cancellationToken).ConfigureAwait(false);
            await compressor.FlushAsync(cancellationToken).ConfigureAwait(false);
            return outputStream.ToArray();
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    /// <summary>
    /// 异步解压缩消息
    /// </summary>
    public static async ValueTask<byte[]> DecompressAsync(ReadOnlyMemory<byte> compressedData, CancellationToken cancellationToken = default)
    {
        // 读取原始大小
        var originalSize = BitConverter.ToInt32(compressedData.Span[..4]);

        // 分配缓冲区
        var buffer = _arrayPool.Rent(originalSize);
        try
        {
            using var inputStream = new MemoryStream(compressedData[4..].ToArray());
            await using var decompressor = new BrotliStream(inputStream, CompressionMode.Decompress);

            var bytesRead = await decompressor.ReadAsync(buffer, 0, originalSize, cancellationToken).ConfigureAwait(false);
            if (bytesRead != originalSize)
            {
                throw new InvalidDataException("解压缩后的数据大小不匹配");
            }

            var result = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, result, 0, bytesRead);
            return result;
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
}
