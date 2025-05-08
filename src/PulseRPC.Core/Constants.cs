using System;

namespace PulseRPC.Protocol;

/// <summary>
/// 协议常量定义
/// </summary>
public static class Constants
{
    /// <summary>
    /// 最大消息大小（16MB）
    /// </summary>
    public const int MaxMessageSize = 16 * 1024 * 1024;

    /// <summary>
    /// 最小缓冲区大小（4KB）
    /// </summary>
    public const int MinBufferSize = 4 * 1024;

    /// <summary>
    /// 最优读取大小（8KB）
    /// </summary>
    public const int OptimalReadSize = 8 * 1024;

    /// <summary>
    /// 最大缓冲区大小（64KB）
    /// </summary>
    public const int MaxBufferSize = 64 * 1024;

    /// <summary>
    /// 消息头长度（4字节消息长度 + 4字节消息ID）
    /// </summary>
    public const int HeaderLength = 8;

    /// <summary>
    /// 压缩阈值（1KB），超过此大小的消息将被压缩
    /// </summary>
    public const int CompressionThreshold = 1024;

    /// <summary>
    /// 批处理大小阈值（64KB）
    /// </summary>
    public const int BatchSizeThreshold = 64 * 1024;

    /// <summary>
    /// 最大分片大小（16KB）
    /// </summary>
    public const int MaxFragmentSize = 16 * 1024;

    /// <summary>
    /// 协议版本号
    /// </summary>
    public const byte ProtocolVersion = 1;
}
