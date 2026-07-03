using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PulseRPC.Transport.Kcp;

/// <summary>
/// KCP段命令类型
/// </summary>
public enum KcpCommand : byte
{
    /// <summary>
    /// 数据段
    /// </summary>
    Push = 81, // IKCP_CMD_PUSH

    /// <summary>
    /// 确认段
    /// </summary>
    Ack = 82,  // IKCP_CMD_ACK

    /// <summary>
    /// 窗口探测段
    /// </summary>
    WindowsProbe = 83, // IKCP_CMD_WASK

    /// <summary>
    /// 窗口响应段
    /// </summary>
    WindowsResponse = 84 // IKCP_CMD_WINS
}

/// <summary>
/// KCP段结构
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct KcpSegmentHeader
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public uint Conv;

    /// <summary>
    /// 命令类型
    /// </summary>
    public byte Cmd;

    /// <summary>
    /// 分片编号（Fragment）
    /// </summary>
    public byte Frg;

    /// <summary>
    /// 窗口大小
    /// </summary>
    public ushort Wnd;

    /// <summary>
    /// 时间戳
    /// </summary>
    public uint Ts;

    /// <summary>
    /// 序列号
    /// </summary>
    public uint Sn;

    /// <summary>
    /// 未确认序列号
    /// </summary>
    public uint Una;

    /// <summary>
    /// 数据长度
    /// </summary>
    public uint Len;

    /// <summary>
    /// KCP头部大小
    /// </summary>
    public const int HeaderSize = 24;
}

/// <summary>
/// KCP段实现
/// </summary>
public sealed class KcpSegment : IDisposable
{
    private static readonly ConcurrentQueue<KcpSegment> _pool = new();
    private static int _poolCount = 0;
    private const int MaxPoolSize = 1024;

    /// <summary>
    /// 段头部
    /// </summary>
    public KcpSegmentHeader Header;

    /// <summary>
    /// 数据缓冲区
    /// </summary>
    public Memory<byte> Data;

    /// <summary>
    /// 重传超时时间
    /// </summary>
    public uint ResendTs;

    /// <summary>
    /// 重传次数
    /// </summary>
    public uint FastAck;

    /// <summary>
    /// 传输次数
    /// </summary>
    public uint Xmit;

    private byte[]? _buffer;
    private bool _disposed;

    private KcpSegment()
    {
    }

    /// <summary>
    /// 从对象池获取段实例
    /// </summary>
    public static KcpSegment Rent()
    {
        if (_pool.TryDequeue(out var segment))
        {
            Interlocked.Decrement(ref _poolCount);
            segment._disposed = false;
            return segment;
        }

        return new KcpSegment();
    }

    /// <summary>
    /// 返回段实例到对象池
    /// </summary>
    public static void Return(KcpSegment segment)
    {
        if (segment._disposed)
            return;

        segment.Reset();

        if (_poolCount < MaxPoolSize)
        {
            _pool.Enqueue(segment);
            Interlocked.Increment(ref _poolCount);
        }
    }

    /// <summary>
    /// 设置数据
    /// </summary>
    public void SetData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            Data = Memory<byte>.Empty;
            return;
        }

        if (_buffer == null || _buffer.Length < data.Length)
        {
            if (_buffer != null)
                ArrayPool<byte>.Shared.Return(_buffer);

            _buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        }

        data.CopyTo(_buffer);
        Data = _buffer.AsMemory(0, data.Length);
        Header.Len = (uint)data.Length;
    }

    /// <summary>
    /// 编码段到缓冲区
    /// </summary>
    public int Encode(Span<byte> buffer)
    {
        if (buffer.Length < KcpSegmentHeader.HeaderSize + Header.Len)
            return -1;

        var headerSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Header, 1));
        headerSpan.CopyTo(buffer);

        if (Header.Len > 0 && !Data.IsEmpty)
        {
            Data.Span.CopyTo(buffer.Slice(KcpSegmentHeader.HeaderSize));
        }

        return KcpSegmentHeader.HeaderSize + (int)Header.Len;
    }

    /// <summary>
    /// 从缓冲区解码段
    /// </summary>
    public static KcpSegment? Decode(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < KcpSegmentHeader.HeaderSize)
            return null;

        var segment = Rent();

        // 解码头部
        var headerSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref segment.Header, 1));
        buffer.Slice(0, KcpSegmentHeader.HeaderSize).CopyTo(headerSpan);

        // 解码数据
        if (segment.Header.Len > 0)
        {
            if (buffer.Length < KcpSegmentHeader.HeaderSize + segment.Header.Len)
            {
                Return(segment);
                return null;
            }

            var dataSpan = buffer.Slice(KcpSegmentHeader.HeaderSize, (int)segment.Header.Len);
            segment.SetData(dataSpan);
        }

        return segment;
    }

    /// <summary>
    /// 重置段状态
    /// </summary>
    private void Reset()
    {
        Header = default;
        Data = Memory<byte>.Empty;
        ResendTs = 0;
        FastAck = 0;
        Xmit = 0;

        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Return(this);
    }

    /// <summary>
    /// 获取池统计信息
    /// </summary>
    public static int PoolCount => _poolCount;
}
