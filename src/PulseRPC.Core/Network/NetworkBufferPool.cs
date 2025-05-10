using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 自定义内存池管理器 - 用于降低GC压力
/// </summary>
public class NetworkBufferPool : IDisposable
{
    // 小块缓冲区 - 按照2^n大小分类，范围从16B到32KB
    private readonly ConcurrentBag<byte[]>[] _smallPools;

    // 大块缓冲区 - 超过32KB的特殊缓冲区
    private readonly ConcurrentDictionary<int, ConcurrentBag<byte[]>> _largePools = new();

    // 单例实例
    private static NetworkBufferPool? _instance;

    // 线程本地缓存 - 避免过多的线程争用
    [ThreadStatic] private static Dictionary<int, byte[]>? _threadLocalCache;

    // 调试统计信息
    private long _totalAllocated;
    private long _missCount;
    private long _hitCount;

    /// <summary>
    /// 获取单例实例
    /// </summary>
    public static NetworkBufferPool Instance => _instance ??= new NetworkBufferPool();

    /// <summary>
    /// 构造函数
    /// </summary>
    private NetworkBufferPool()
    {
        // 初始化11个小块缓冲区池 (2^4 到 2^14) 16B到16KB
        _smallPools = new ConcurrentBag<byte[]>[11];
        for (var i = 0; i < _smallPools.Length; i++)
        {
            _smallPools[i] = new ConcurrentBag<byte[]>();
        }

        // 预分配一些常用大小的缓冲区
        PreallocateBuffers();
    }

    /// <summary>
    /// 预分配一些常用大小的缓冲区
    /// </summary>
    private void PreallocateBuffers()
    {
        // 预分配一些常用大小的缓冲区
        // 例如: 1KB, 4KB, 8KB
        const int preAllocCount = 32; // 每种大小预分配32个

        int[] commonSizes = { 1024, 4096, 8192 };

        foreach (var size in commonSizes)
        {
            var sizeIndex = GetSizeIndex(size);
            if (sizeIndex < 0 || sizeIndex >= _smallPools.Length)
            {
                continue;
            }

            for (var i = 0; i < preAllocCount; i++)
            {
                _smallPools[sizeIndex].Add(new byte[1 << (sizeIndex + 4)]);
            }
        }
    }

    /// <summary>
    /// 租用指定大小的缓冲区
    /// </summary>
    /// <param name="minSize">最小所需大小</param>
    /// <returns>至少为minSize大小的缓冲区</returns>
    public byte[] Rent(int minSize)
    {
        if (minSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSize));
        }

        // 0. 初始化线程本地缓存(如果需要)
        _threadLocalCache ??= new Dictionary<int, byte[]>();

        // 1. 检查线程本地缓存
        var actualSize = CalculateSize(minSize);
        if (_threadLocalCache.Remove(actualSize, out var localBuffer))
        {
            Interlocked.Increment(ref _hitCount);
            return localBuffer;
        }

        // 2. 确定缓冲区大小和索引
        var sizeIndex = GetSizeIndex(minSize);

        // 3. 从相应池获取缓冲区
        if (sizeIndex >= 0 && sizeIndex < _smallPools.Length)
        {
            // 小块缓冲区 (16B-16KB)
            if (_smallPools[sizeIndex].TryTake(out var buffer))
            {
                Interlocked.Increment(ref _hitCount);
                return buffer;
            }

            // 如果池中没有，创建新的缓冲区
            Interlocked.Increment(ref _missCount);
            Interlocked.Add(ref _totalAllocated, 1 << (sizeIndex + 4));
            return new byte[1 << (sizeIndex + 4)];
        }
        else
        {
            // 大块缓冲区 (>16KB)
            if (_largePools.TryGetValue(actualSize, out var largePool) &&
                largePool.TryTake(out var buffer))
            {
                Interlocked.Increment(ref _hitCount);
                return buffer;
            }

            // 如果池中没有，创建新的缓冲区
            Interlocked.Increment(ref _missCount);
            Interlocked.Add(ref _totalAllocated, actualSize);
            return new byte[actualSize];
        }
    }

    /// <summary>
    /// 返回缓冲区到池中
    /// </summary>
    public void Return(byte[] buffer)
    {
        // 1. 检查是否可以放入线程本地缓存
        if (_threadLocalCache != null && _threadLocalCache.Count < 8) // 限制本地缓存大小
        {
            var size = buffer.Length;
            if (!_threadLocalCache.ContainsKey(size))
            {
                _threadLocalCache[size] = buffer;
                return;
            }
        }

        // 2. 确定缓冲区索引
        var sizeIndex = GetSizeIndex(buffer.Length);

        // 3. 返回到相应池
        if (sizeIndex >= 0 && sizeIndex < _smallPools.Length)
        {
            // 检查是否是2的幂大小 (防止返回不符合规格的缓冲区)
            var expectedSize = 1 << (sizeIndex + 4);
            if (buffer.Length == expectedSize)
            {
                _smallPools[sizeIndex].Add(buffer);
            }
        }
        else
        {
            // 大块缓冲区
            var largePool = _largePools.GetOrAdd(buffer.Length, _ => new ConcurrentBag<byte[]>());
            largePool.Add(buffer);
        }
    }

    /// <summary>
    /// 清理不需要的缓冲区，释放内存
    /// </summary>
    public void Trim()
    {
        // 小块缓冲区保留少量，大块缓冲区可以全部清理
        _largePools.Clear();

        // 对于小块缓冲区，保留一定数量，清除多余
        for (var i = 0; i < _smallPools.Length; i++)
        {
            var maxCount = 32 >> i; // 较小的缓冲区保留更多

            var tempPool = _smallPools[i];
            _smallPools[i] = new ConcurrentBag<byte[]>();

            // 只保留maxCount个缓冲区
            var kept = 0;
            while (kept < maxCount && tempPool.TryTake(out var buffer))
            {
                _smallPools[i].Add(buffer);
                kept++;
            }
        }
    }

    /// <summary>
    /// 计算需要分配的实际大小 (调整到2的幂或特殊大小)
    /// </summary>
    private int CalculateSize(int minSize)
    {
        // 1. 对于小块缓冲区，使用2的幂
        if (minSize <= 16384) // 16KB
        {
            var power = 4; // 从2^4=16字节开始
            var size = 1 << power;

            while (size < minSize && power < 14)
            {
                power++;
                size = 1 << power;
            }

            return size;
        }

        // 2. 对于大块缓冲区，使用4KB的倍数
        const int blockSize = 4096;
        return ((minSize + blockSize - 1) / blockSize) * blockSize;
    }

    /// <summary>
    /// 获取给定大小对应的小块缓冲区索引
    /// </summary>
    private static int GetSizeIndex(int size)
    {
        return size switch
        {
            // 对于16B到16KB，使用对应的小块索引
            <= 16 => 0,
            <= 32 => 1,
            <= 64 => 2,
            <= 128 => 3,
            <= 256 => 4,
            <= 512 => 5,
            <= 1024 => 6,
            <= 2048 => 7,
            <= 4096 => 8,
            <= 8192 => 9,
            <= 16384 => 10,
            _ => -1
        };

        // 大于16KB的使用大块缓冲区
    }

    /// <summary>
    /// 获取缓冲区使用状态
    /// </summary>
    public (long TotalAllocated, long HitCount, long MissCount, double HitRatio) GetStats()
    {
        var hits = Interlocked.Read(ref _hitCount);
        var misses = Interlocked.Read(ref _missCount);
        var hitRatio = (hits + misses) > 0 ? (double)hits / (hits + misses) : 0;

        return (Interlocked.Read(ref _totalAllocated), hits, misses, hitRatio);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        Trim();
    }
}
