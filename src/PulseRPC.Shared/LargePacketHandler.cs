using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Shared.Tcp;

namespace PulseRPC.Shared;

/// <summary>
/// 优化的大包分片处理器 - 减少内存碎片化和拷贝
/// 兼容 .NET Standard 2.1
/// </summary>
public sealed class LargePacketHandler : IDisposable
{
    private readonly NetworkBufferPool _bufferPool;
    private readonly ArrayPool<byte> _arrayPool;
    private readonly int _maxConcurrentPackets;
    private readonly Dictionary<int, StreamingPacketState> _activePackets;
    private readonly object _packetsLock = new object();
    private volatile bool _disposed;

    public LargePacketHandler(NetworkBufferPool? bufferPool = null, int maxConcurrentPackets = 64)
    {
        _bufferPool = bufferPool ?? NetworkBufferPool.Instance;
        _arrayPool = ArrayPool<byte>.Shared;
        _maxConcurrentPackets = maxConcurrentPackets;
        _activePackets = new Dictionary<int, StreamingPacketState>(maxConcurrentPackets);
    }

    /// <summary>
    /// 处理接收到的分片数据
    /// </summary>
    public bool ProcessChunk(ChunkHeader chunkHeader, ReadOnlySpan<byte> chunkData, out ReadOnlyMemory<byte> completeData)
    {
        completeData = default;

        if (_disposed)
            return false;

        lock (_packetsLock)
        {
            // 检查并发包数量限制
            if (_activePackets.Count >= _maxConcurrentPackets && !_activePackets.ContainsKey(chunkHeader.ChunkId))
            {
                return false; // 拒绝新的大包，防止内存溢出
            }

            // 获取或创建包状态
            if (!_activePackets.TryGetValue(chunkHeader.ChunkId, out var packetState))
            {
                packetState = new StreamingPacketState(chunkHeader.ChunkId, chunkHeader.TotalChunks, _bufferPool);
                _activePackets[chunkHeader.ChunkId] = packetState;
            }

            // 添加分片
            var isComplete = packetState.AddChunk(chunkHeader.ChunkIndex, chunkData);

            if (isComplete)
            {
                // 获取完整数据。注意：GetCompleteData 返回的是池化缓冲区的视图，
                // 必须在归还池（Dispose）之前复制到独立缓冲，否则会出现 use-after-free：
                // 缓冲被归还后可能被其它租用者复用，导致 completeData 数据被覆盖。
                var completed = packetState.GetCompleteData();
                completeData = completed.ToArray();
                _activePackets.Remove(chunkHeader.ChunkId);
                packetState.Dispose();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 清理过期的包状态
    /// </summary>
    public void CleanupExpiredPackets(TimeSpan maxAge)
    {
        if (_disposed)
            return;

        var cutoffTime = DateTime.UtcNow - maxAge;
        var expiredIds = new List<int>();

        lock (_packetsLock)
        {
            foreach (var kvp in _activePackets)
            {
                if (kvp.Value.StartTime < cutoffTime)
                {
                    expiredIds.Add(kvp.Key);
                }
            }

            foreach (var id in expiredIds)
            {
                if (_activePackets.TryGetValue(id, out var state))
                {
                    _activePackets.Remove(id);
                    state.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 获取当前统计信息
    /// </summary>
    public PacketHandlerStatistics GetStatistics()
    {
        lock (_packetsLock)
        {
            return new PacketHandlerStatistics
            {
                ActivePackets = _activePackets.Count,
                MaxConcurrentPackets = _maxConcurrentPackets
            };
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_packetsLock)
        {
            foreach (var state in _activePackets.Values)
            {
                state.Dispose();
            }
            _activePackets.Clear();
        }
    }
}

/// <summary>
/// 流式包状态 - 使用预分配缓冲区避免多次拷贝
/// </summary>
internal sealed class StreamingPacketState : IDisposable
{
    private readonly int _chunkId;
    private readonly int _totalChunks;
    private readonly NetworkBufferPool _bufferPool;
    private readonly BitArray _receivedChunks;
    private readonly object _lock = new object();

    // 使用预分配的大缓冲区直接写入，避免临时存储
    private byte[]? _buffer;
    private readonly int[] _chunkOffsets;
    private readonly int[] _chunkSizes;
    private int _receivedCount;
    private int _totalSize;
    private bool _disposed;

    public DateTime StartTime { get; }

    public StreamingPacketState(int chunkId, int totalChunks, NetworkBufferPool bufferPool)
    {
        _chunkId = chunkId;
        _totalChunks = totalChunks;
        _bufferPool = bufferPool;
        _receivedChunks = new BitArray(totalChunks);
        _chunkOffsets = new int[totalChunks];
        _chunkSizes = new int[totalChunks];
        _receivedCount = 0;
        StartTime = DateTime.UtcNow;
    }

    public bool AddChunk(int chunkIndex, ReadOnlySpan<byte> chunkData)
    {
        if (_disposed || chunkIndex < 0 || chunkIndex >= _totalChunks)
            return false;

        lock (_lock)
        {
            // 检查是否已经接收过这个分片
            if (_receivedChunks[chunkIndex])
                return _receivedCount == _totalChunks;

            // 延迟分配缓冲区，直到收到第一个分片时才估算总大小
            if (_buffer == null)
            {
                // 基于第一个分片大小估算总缓冲区大小
                var estimatedSize = chunkData.Length * _totalChunks;
                _buffer = _bufferPool.Rent(estimatedSize);
            }

            // 扩展缓冲区（如果需要）
            var requiredSize = _totalSize + chunkData.Length;
            if (requiredSize > _buffer.Length)
            {
                var newBuffer = _bufferPool.Rent(requiredSize * 2); // 预留空间避免频繁扩展
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _totalSize);
                _bufferPool.Return(_buffer);
                _buffer = newBuffer;
            }

            // 记录分片信息
            _chunkOffsets[chunkIndex] = _totalSize;
            _chunkSizes[chunkIndex] = chunkData.Length;

            // 直接写入到目标位置，避免临时存储
            chunkData.CopyTo(_buffer.AsSpan(_totalSize));
            _totalSize += chunkData.Length;

            // 标记已接收
            _receivedChunks[chunkIndex] = true;
            _receivedCount++;

            return _receivedCount == _totalChunks;
        }
    }

    public ReadOnlyMemory<byte> GetCompleteData()
    {
        lock (_lock)
        {
            if (_receivedCount != _totalChunks || _buffer == null)
                return ReadOnlyMemory<byte>.Empty;

            // 需要重新排序分片（如果乱序接收）
            if (NeedsReordering())
            {
                var orderedBuffer = _bufferPool.Rent(_totalSize);
                var offset = 0;

                for (int i = 0; i < _totalChunks; i++)
                {
                    if (_receivedChunks[i])
                    {
                        var chunkOffset = _chunkOffsets[i];
                        var chunkSize = _chunkSizes[i];
                        Buffer.BlockCopy(_buffer, chunkOffset, orderedBuffer, offset, chunkSize);
                        offset += chunkSize;
                    }
                }

                _bufferPool.Return(_buffer);
                _buffer = orderedBuffer;
            }

            // 返回精确大小的数据，避免多余的尾部空间
            return new ReadOnlyMemory<byte>(_buffer, 0, _totalSize);
        }
    }

    private bool NeedsReordering()
    {
        // 检查分片是否按顺序接收
        var expectedOffset = 0;
        for (int i = 0; i < _totalChunks; i++)
        {
            if (_receivedChunks[i])
            {
                if (_chunkOffsets[i] != expectedOffset)
                    return true;
                expectedOffset += _chunkSizes[i];
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_lock)
        {
            if (_buffer != null)
            {
                _bufferPool.Return(_buffer);
                _buffer = null;
            }
        }
    }
}

/// <summary>
/// 位数组实现 - .NET Standard 2.1 兼容
/// </summary>
internal sealed class BitArray
{
    private readonly uint[] _bits;
    private readonly int _length;

    public BitArray(int length)
    {
        _length = length;
        _bits = new uint[(length + 31) / 32]; // 每个uint存储32位
    }

    public bool this[int index]
    {
        get
        {
            if (index < 0 || index >= _length)
                throw new ArgumentOutOfRangeException(nameof(index));

            var wordIndex = index / 32;
            var bitIndex = index % 32;
            return (_bits[wordIndex] & (1u << bitIndex)) != 0;
        }
        set
        {
            if (index < 0 || index >= _length)
                throw new ArgumentOutOfRangeException(nameof(index));

            var wordIndex = index / 32;
            var bitIndex = index % 32;

            if (value)
                _bits[wordIndex] |= (1u << bitIndex);
            else
                _bits[wordIndex] &= ~(1u << bitIndex);
        }
    }
}

/// <summary>
/// 包处理器统计信息
/// </summary>
public struct PacketHandlerStatistics
{
    public int ActivePackets;
    public int MaxConcurrentPackets;
}
