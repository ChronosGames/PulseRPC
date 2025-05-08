using System;
using System.Buffers;
using System.IO.Compression;
using System.IO;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// 消息压缩器
/// </summary>
public static class MessageCompressor
{
    private const int CompressionThreshold = 1024; // 1KB
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// 尝试压缩数据
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <returns>(是否已压缩, 处理后的数据)</returns>
    public static (bool Compressed, byte[] Data) TryCompress(ReadOnlySpan<byte> data)
    {
        // 小于阈值的数据不压缩
        if (data.Length < CompressionThreshold)
        {
            return (false, data.ToArray());
        }

        var buffer = _arrayPool.Rent(data.Length);
        try
        {
            using var compressedStream = new MemoryStream(buffer);
            using (var gzipStream = new BrotliStream(compressedStream, CompressionLevel.Fastest))
            {
                gzipStream.Write(data);
            }

            var compressedLength = (int)compressedStream.Position;

            // 如果压缩后更大，则不使用压缩
            if (compressedLength >= data.Length)
            {
                return (false, data.ToArray());
            }

            var result = new byte[compressedLength];
            Buffer.BlockCopy(buffer, 0, result, 0, compressedLength);
            return (true, result);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    /// <summary>
    /// 解压缩数据
    /// </summary>
    /// <param name="compressedData">压缩数据</param>
    /// <returns>解压后的数据</returns>
    public static byte[] Decompress(ReadOnlySpan<byte> compressedData)
    {
        using var compressedStream = new MemoryStream(compressedData.ToArray());
        using var decompressedStream = new MemoryStream();
        using (var brotliStream = new BrotliStream(compressedStream, CompressionMode.Decompress))
        {
            brotliStream.CopyTo(decompressedStream);
        }
        return decompressedStream.ToArray();
    }
}
