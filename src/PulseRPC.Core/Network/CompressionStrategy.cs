using System;
using System.IO.Compression;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 智能压缩策略
/// </summary>
public static class CompressionStrategy
{
    // 用于简单熵检测的样本大小
    private const int EntropySampleSize = 1024;

    /// <summary>
    /// 智能决定是否应该压缩数据
    /// </summary>
    public static bool ShouldCompress(byte[] data, NetworkOptions options)
    {
        // 太小的消息不值得压缩
        if (data.Length < options.CompressionThreshold)
        {
            return false;
        }

        switch (data.Length)
        {
            // 超过32KB的消息一定压缩
            case > 32 * 1024:
                return true;
            // 对中等大小的消息，进行简单熵检测
            case >= EntropySampleSize:
            {
                // 进行简单的熵检测
                var entropy = CalculateEntropy(data.AsSpan(0, Math.Min(EntropySampleSize, data.Length)));

                // 熵值低表示更可压缩
                return entropy < 0.8;
            }
            default:
                // 默认对中等大小的消息进行压缩
                return data.Length >= 2 * 1024; // 2KB
        }
    }

    /// <summary>
    /// 计算数据的熵 - 衡量数据的随机性/可压缩性
    /// </summary>
    private static double CalculateEntropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return 0;
        }

        // 计算每个字节值出现的频率
        var frequency = new int[256];
        foreach (var t in data)
        {
            frequency[t]++;
        }

        // 计算熵
        double entropy = 0;
        for (var i = 0; i < 256; i++)
        {
            if (frequency[i] > 0)
            {
                var probability = frequency[i] / (double)data.Length;
                entropy -= probability * Math.Log(probability, 256);
            }
        }

        return entropy;
    }

    /// <summary>
    /// 选择最适合给定数据大小的压缩级别
    /// </summary>
    public static CompressionLevel SelectCompressionLevel(int dataSize)
    {
        // 小型消息使用快速压缩
        if (dataSize < 16 * 1024) // 16KB
            return CompressionLevel.Fastest;

        // 大型消息使用平衡压缩
        if (dataSize < 128 * 1024) // 128KB
            return CompressionLevel.Optimal;

        // 超大消息使用快速压缩以避免过长处理时间
        return CompressionLevel.Fastest;
    }
}
