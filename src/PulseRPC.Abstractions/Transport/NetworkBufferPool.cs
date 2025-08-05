using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PulseRPC.Transport;

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

/// <summary>
/// 高度优化的网络缓冲区池 - 减少GC压力和内存碎片
/// </summary>
public sealed class OptimizedNetworkBufferPool : IDisposable
{
    // 预定义的缓冲区大小（2的幂）
    private static readonly int[] BufferSizes = {
        128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144
    };

    // 每个大小对应的缓冲区池
    private readonly ConcurrentQueue<byte[]>[] _pools;
    private readonly long[] _poolCounts;
    private readonly int _maxBuffersPerPool;

    // 大缓冲区特殊处理
    private readonly ConcurrentDictionary<int, ConcurrentQueue<byte[]>> _largePools = new();

    // 线程本地缓存 - 避免线程竞争
    [ThreadStatic] private static ThreadLocalCache? _threadLocalCache;

    // 统计信息
    private long _totalRented;
    private long _totalReturned;
    private long _cacheHits;
    private long _cacheMisses;

    // 单例实例
    private static OptimizedNetworkBufferPool? _instance;
    private static readonly object InstanceLock = new();

    public static OptimizedNetworkBufferPool Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (InstanceLock)
                {
                    _instance ??= new OptimizedNetworkBufferPool();
                }
            }
            return _instance;
        }
    }

    private OptimizedNetworkBufferPool(int maxBuffersPerPool = 256)
    {
        _maxBuffersPerPool = maxBuffersPerPool;
        _pools = new ConcurrentQueue<byte[]>[BufferSizes.Length];
        _poolCounts = new long[BufferSizes.Length];

        for (int i = 0; i < BufferSizes.Length; i++)
        {
            _pools[i] = new ConcurrentQueue<byte[]>();
        }

        // 预热常用大小的缓冲区
        PrewarmPools();
    }

    /// <summary>
    /// 预热缓冲区池
    /// </summary>
    private void PrewarmPools()
    {
        // 为常用大小预分配一些缓冲区
        var commonSizes = new[] { 1024, 4096, 8192 }; // 1KB, 4KB, 8KB

        foreach (var size in commonSizes)
        {
            var poolIndex = GetPoolIndex(size);
            if (poolIndex >= 0)
            {
                var actualSize = BufferSizes[poolIndex];
                for (int i = 0; i < 32; i++) // 每个大小预分配32个
                {
                    _pools[poolIndex].Enqueue(new byte[actualSize]);
                    Interlocked.Increment(ref _poolCounts[poolIndex]);
                }
            }
        }
    }

    /// <summary>
    /// 租用缓冲区
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Rent(int minimumLength)
    {
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength));

        Interlocked.Increment(ref _totalRented);

        // 1. 检查线程本地缓存
        _threadLocalCache ??= new ThreadLocalCache();

        if (_threadLocalCache.TryRent(minimumLength, out var cachedBuffer))
        {
            Interlocked.Increment(ref _cacheHits);
            return cachedBuffer;
        }

        // 2. 从全局池租用
        var poolIndex = GetPoolIndex(minimumLength);

        if (poolIndex >= 0)
        {
            // 小缓冲区（<256KB）
            if (_pools[poolIndex].TryDequeue(out var buffer))
            {
                Interlocked.Decrement(ref _poolCounts[poolIndex]);
                Interlocked.Increment(ref _cacheHits);
                return buffer;
            }

            // 池中没有，创建新的
            Interlocked.Increment(ref _cacheMisses);
            return new byte[BufferSizes[poolIndex]];
        }
        else
        {
            // 大缓冲区（>=256KB）
            var actualSize = RoundUpToPowerOfTwo(minimumLength);

            if (_largePools.TryGetValue(actualSize, out var largePool) &&
                largePool.TryDequeue(out var buffer))
            {
                Interlocked.Increment(ref _cacheHits);
                return buffer;
            }

            // 创建新的大缓冲区
            Interlocked.Increment(ref _cacheMisses);
            return new byte[actualSize];
        }
    }

    /// <summary>
    /// 归还缓冲区
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(byte[] buffer)
    {
        Interlocked.Increment(ref _totalReturned);

        var length = buffer.Length;

        // 1. 尝试放入线程本地缓存
        _threadLocalCache ??= new ThreadLocalCache();

        if (_threadLocalCache.TryReturn(buffer))
        {
            return;
        }

        // 2. 放入全局池
        var poolIndex = GetPoolIndexExact(length);

        if (poolIndex >= 0)
        {
            // 检查池是否已满
            if (Interlocked.Read(ref _poolCounts[poolIndex]) < _maxBuffersPerPool)
            {
                _pools[poolIndex].Enqueue(buffer);
                Interlocked.Increment(ref _poolCounts[poolIndex]);
            }
            // 如果池已满，让GC回收缓冲区
        }
        else if (length >= 262144) // 256KB+
        {
            // 大缓冲区
            var largePool = _largePools.GetOrAdd(length, _ => new ConcurrentQueue<byte[]>());
            largePool.Enqueue(buffer);
        }
    }

    /// <summary>
    /// 清理池中多余的缓冲区
    /// </summary>
    public void Trim()
    {
        // 清理大缓冲区池
        _largePools.Clear();

        // 清理小缓冲区池，保留少量
        for (int i = 0; i < _pools.Length; i++)
        {
            var targetCount = Math.Min(32, _maxBuffersPerPool / 4);
            var currentCount = Interlocked.Read(ref _poolCounts[i]);

            if (currentCount > targetCount)
            {
                var toRemove = currentCount - targetCount;
                for (int j = 0; j < toRemove; j++)
                {
                    if (_pools[i].TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref _poolCounts[i]);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public PoolStatistics GetStatistics()
    {
        var totalRented = Interlocked.Read(ref _totalRented);
        var totalReturned = Interlocked.Read(ref _totalReturned);
        var cacheHits = Interlocked.Read(ref _cacheHits);
        var cacheMisses = Interlocked.Read(ref _cacheMisses);

        var hitRate = (cacheHits + cacheMisses) > 0 ? (double)cacheHits / (cacheHits + cacheMisses) : 0;

        return new PoolStatistics
        {
            TotalRented = totalRented,
            TotalReturned = totalReturned,
            Outstanding = totalRented - totalReturned,
            CacheHitRate = hitRate,
            PoolCounts = Array.ConvertAll(_poolCounts, x => (int)x)
        };
    }

    /// <summary>
    /// 获取缓冲区大小对应的池索引
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPoolIndex(int minimumLength)
    {
        // 使用二分查找找到合适的池
        int left = 0, right = BufferSizes.Length - 1;

        while (left <= right)
        {
            int mid = (left + right) / 2;

            if (BufferSizes[mid] >= minimumLength)
            {
                if (mid == 0 || BufferSizes[mid - 1] < minimumLength)
                {
                    return mid;
                }
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }

        return -1; // 超出预定义大小范围
    }

    /// <summary>
    /// 获取精确大小对应的池索引
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPoolIndexExact(int length)
    {
        for (int i = 0; i < BufferSizes.Length; i++)
        {
            if (BufferSizes[i] == length)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 将数字向上取整到最近的2的幂
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RoundUpToPowerOfTwo(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }

    public void Dispose()
    {
        Trim();
    }

    /// <summary>
    /// 线程本地缓存 - 减少跨线程竞争
    /// </summary>
    private sealed class ThreadLocalCache
    {
        private const int MaxCachedBuffers = 8;
        private readonly CachedBuffer[] _cachedBuffers = new CachedBuffer[MaxCachedBuffers];
        private int _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRent(int minimumLength, out byte[] buffer)
        {
            // 从缓存中查找合适大小的缓冲区
            for (int i = 0; i < _count; i++)
            {
                if (_cachedBuffers[i].Buffer.Length >= minimumLength)
                {
                    buffer = _cachedBuffers[i].Buffer;

                    // 移除已使用的缓冲区
                    _count--;
                    if (i < _count)
                    {
                        _cachedBuffers[i] = _cachedBuffers[_count];
                    }

                    return true;
                }
            }

            buffer = null!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReturn(byte[] buffer)
        {
            if (_count >= MaxCachedBuffers)
                return false;

            _cachedBuffers[_count] = new CachedBuffer { Buffer = buffer };
            _count++;
            return true;
        }

        private struct CachedBuffer
        {
            public byte[] Buffer;
        }
    }
}

/// <summary>
/// 池统计信息
/// </summary>
public struct PoolStatistics
{
    public long TotalRented;
    public long TotalReturned;
    public long Outstanding;
    public double CacheHitRate;
    public int[] PoolCounts;
}
