using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CS0618 // Legacy helper types in this file reference the experimental pool constants.

namespace PulseRPC.Server.Processing.Memory;

/// <summary>
/// 分层内存池 - 高性能多级缓存内存管理器
/// 替代原有NetworkBufferPool，提供L0(线程本地)→L1(NUMA感知)→L2(全局)三级缓存架构
/// </summary>
[Obsolete("Experimental standalone component. It is not used by the fixed-shard message engine.", false)]
public sealed class TieredMemoryPool : IDisposable
{
    #region 技术规格常量

    /// <summary>
    /// 池层级规格
    /// </summary>
    public static class TierSpecs
    {
        // L0: 线程本地缓存
        public const int L0_MAX_CACHED_BUFFERS = 16;     // 每线程最大缓存16个
        public const int L0_MAX_BUFFER_SIZE = 8192;      // 最大缓冲区8KB

        // L1: NUMA感知池
        public const int L1_POOLS_PER_NUMA_NODE = 12;   // 每NUMA节点12个池
        public const int L1_MAX_BUFFERS_PER_POOL = 128; // 每池最大128个缓冲区

        // L2: 全局大缓冲区池
        public const int L2_MIN_BUFFER_SIZE = 16384;     // 最小缓冲区16KB
        public const int L2_MAX_BUFFERS = 64;           // 最大64个大缓冲区

        public const int CACHE_LINE_SIZE = 64;          // 缓存行大小
        public const int ALIGNMENT_SIZE = 16;           // 内存对齐大小
    }

    /// <summary>
    /// 预定义缓冲区大小 (字节)
    /// </summary>
    public static readonly int[] BUFFER_SIZES = {
        128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144
    };

    #endregion

    #region 字段和属性

    private readonly ILogger<TieredMemoryPool> _logger;

    // L0: 线程本地缓存管理器
    [ThreadStatic] private static ThreadLocalBufferCache? _threadLocalCache;

    // L1: NUMA感知缓冲区池数组 [NUMA节点][池索引]
    private readonly NumaAwareBufferPool[] _numaNodes;
    private readonly int _numaNodeCount;

    // L2: 全局大缓冲区池
    private readonly ConcurrentDictionary<int, ConcurrentQueue<byte[]>> _globalLargeBuffers;

    // 池统计信息
    private readonly PoolStatistics _statistics = new();

    // 单例实例
    private static TieredMemoryPool? _instance;
    private static readonly Lock _instanceLock = new();

    #endregion

    #region 单例模式

    /// <summary>
    /// 获取单例实例
    /// </summary>
    public static TieredMemoryPool Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new TieredMemoryPool();
                }
            }
            return _instance;
        }
    }

    #endregion

    #region 构造函数和初始化

    /// <summary>
    /// 构造分层内存池
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    private TieredMemoryPool(ILogger<TieredMemoryPool>? logger = null)
    {
        _logger = logger ?? new NullLogger<TieredMemoryPool>();

        // 检测NUMA拓扑
        _numaNodeCount = DetectNumaNodes();
        _numaNodes = new NumaAwareBufferPool[_numaNodeCount];

        // 初始化NUMA感知池
        for (int i = 0; i < _numaNodeCount; i++)
        {
            _numaNodes[i] = new NumaAwareBufferPool(i, _logger);
        }

        // 初始化全局大缓冲区池
        _globalLargeBuffers = new ConcurrentDictionary<int, ConcurrentQueue<byte[]>>();

        // 预热常用大小的缓冲区
        PrewarmBuffers();

        _logger.LogInformation("分层内存池已初始化，NUMA节点数：{NumaNodes}", _numaNodeCount);
    }

    /// <summary>
    /// 检测NUMA节点数
    /// </summary>
    private static int DetectNumaNodes()
    {
        try
        {
            // 简化实现：使用处理器组数作为NUMA节点估计
            return Math.Max(1, Environment.ProcessorCount / 8);
        }
        catch
        {
            return 1; // 回退到单节点
        }
    }

    /// <summary>
    /// 预热缓冲区池
    /// </summary>
    private void PrewarmBuffers()
    {
        var commonSizes = new[] { 1024, 4096, 8192 }; // 1KB, 4KB, 8KB

        foreach (var size in commonSizes)
        {
            for (int i = 0; i < 16; i++) // 每个大小预分配16个
            {
                var buffer = new byte[size];
                var poolIndex = GetBufferSizeIndex(size);
                if (poolIndex >= 0)
                {
                    var numaNode = i % _numaNodeCount;
                    _numaNodes[numaNode].ReturnBuffer(buffer, poolIndex);
                }
            }
        }

        _logger.LogDebug("缓冲区预热完成，预分配了 {SizeCount} 种大小的缓冲区", commonSizes.Length);
    }

    #endregion

    #region 核心租用和归还API

    /// <summary>
    /// 租用指定大小的缓冲区
    /// </summary>
    /// <param name="minimumLength">最小所需大小</param>
    /// <returns>至少为minimumLength大小的缓冲区</returns>
    public byte[] Rent(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumLength);

        Interlocked.Increment(ref _statistics._totalRents);

        // L0: 尝试从线程本地缓存获取
        if (TryGetFromThreadLocal(minimumLength, out var buffer))
        {
            Interlocked.Increment(ref _statistics._l0Hits);
            return buffer;
        }

        // L1: 从NUMA感知池获取
        if (TryGetFromNumaPool(minimumLength, out buffer))
        {
            Interlocked.Increment(ref _statistics._l1Hits);
            return buffer;
        }

        // L2: 从全局大缓冲区池获取
        if (minimumLength >= TierSpecs.L2_MIN_BUFFER_SIZE &&
            TryGetFromGlobalPool(minimumLength, out buffer))
        {
            Interlocked.Increment(ref _statistics._l2Hits);
            return buffer;
        }

        // 所有缓存未命中，分配新缓冲区
        var actualSize = CalculateBufferSize(minimumLength);
        buffer = new byte[actualSize];

        Interlocked.Increment(ref _statistics._allocations);
        Interlocked.Add(ref _statistics._totalAllocatedBytes, actualSize);

        _logger.LogTrace("分配新缓冲区：请求大小={RequestedSize}, 实际大小={ActualSize}",
            minimumLength, actualSize);

        return buffer;
    }

    /// <summary>
    /// 归还缓冲区到池中
    /// </summary>
    /// <param name="buffer">要归还的缓冲区</param>
    /// <param name="clearArray">是否清除数组内容</param>
    public void Return(byte[] buffer, bool clearArray = false)
    {
        if (buffer == null) return;

        Interlocked.Increment(ref _statistics._totalReturns);

        if (clearArray)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        var size = buffer.Length;

        // L0: 尝试放入线程本地缓存
        if (size <= TierSpecs.L0_MAX_BUFFER_SIZE && TryReturnToThreadLocal(buffer))
        {
            Interlocked.Increment(ref _statistics._l0Returns);
            return;
        }

        // L1: 放入NUMA感知池
        if (TryReturnToNumaPool(buffer))
        {
            Interlocked.Increment(ref _statistics._l1Returns);
            return;
        }

        // L2: 放入全局大缓冲区池
        if (size >= TierSpecs.L2_MIN_BUFFER_SIZE && TryReturnToGlobalPool(buffer))
        {
            Interlocked.Increment(ref _statistics._l2Returns);
            return;
        }

        // 无法缓存，让GC回收
        Interlocked.Increment(ref _statistics._discards);
    }

    #endregion

    #region L0层：线程本地缓存操作

    /// <summary>
    /// 尝试从线程本地缓存获取
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetFromThreadLocal(int minimumLength, [NotNullWhen(true)] out byte[]? buffer)
    {
        _threadLocalCache ??= new ThreadLocalBufferCache();
        return _threadLocalCache.TryRent(minimumLength, out buffer);
    }

    /// <summary>
    /// 尝试归还到线程本地缓存
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReturnToThreadLocal(byte[] buffer)
    {
        _threadLocalCache ??= new ThreadLocalBufferCache();
        return _threadLocalCache.TryReturn(buffer);
    }

    #endregion

    #region L1层：NUMA感知池操作

    /// <summary>
    /// 尝试从NUMA池获取缓冲区
    /// </summary>
    private bool TryGetFromNumaPool(int minimumLength, [NotNullWhen(true)] out byte[]? buffer)
    {
        var poolIndex = GetBufferSizeIndex(minimumLength);
        if (poolIndex < 0)
        {
            buffer = null;
            return false;
        }

        // 优先从当前NUMA节点获取
        var currentNode = GetCurrentNumaNode();
        if (_numaNodes[currentNode].TryRent(poolIndex, out buffer))
            return true;

        // 从其他NUMA节点尝试获取
        for (int i = 0; i < _numaNodeCount; i++)
        {
            if (i != currentNode && _numaNodes[i].TryRent(poolIndex, out buffer))
                return true;
        }

        buffer = null;
        return false;
    }

    /// <summary>
    /// 尝试归还到NUMA池
    /// </summary>
    private bool TryReturnToNumaPool(byte[] buffer)
    {
        var poolIndex = GetBufferSizeIndex(buffer.Length);
        if (poolIndex < 0) return false;

        var currentNode = GetCurrentNumaNode();
        return _numaNodes[currentNode].TryReturn(buffer, poolIndex);
    }

    /// <summary>
    /// 获取当前线程的NUMA节点
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetCurrentNumaNode()
    {
        // 简化实现：基于线程ID分散到不同NUMA节点
        return Thread.CurrentThread.ManagedThreadId % _numaNodeCount;
    }

    #endregion

    #region L2层：全局大缓冲区池操作

    /// <summary>
    /// 尝试从全局池获取大缓冲区
    /// </summary>
    private bool TryGetFromGlobalPool(int minimumLength, [NotNullWhen(true)] out byte[]? buffer)
    {
        var actualSize = CalculateBufferSize(minimumLength);

        if (_globalLargeBuffers.TryGetValue(actualSize, out var queue) &&
            queue.TryDequeue(out buffer))
        {
            return true;
        }

        buffer = null;
        return false;
    }

    /// <summary>
    /// 尝试归还到全局池
    /// </summary>
    private bool TryReturnToGlobalPool(byte[] buffer)
    {
        var size = buffer.Length;
        var queue = _globalLargeBuffers.GetOrAdd(size, _ => new ConcurrentQueue<byte[]>());

        // 限制全局池大小
        var currentCount = 0;
        var temp = new List<byte[]>();

        // 计算当前队列大小
        while (queue.TryDequeue(out var item))
        {
            temp.Add(item);
            currentCount++;
        }

        // 重新入队，保持大小限制
        foreach (var item in temp.Take(TierSpecs.L2_MAX_BUFFERS - 1))
        {
            queue.Enqueue(item);
        }

        if (currentCount < TierSpecs.L2_MAX_BUFFERS)
        {
            queue.Enqueue(buffer);
            return true;
        }

        return false;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 计算实际缓冲区大小
    /// </summary>
    private int CalculateBufferSize(int minimumLength)
    {
        // 对于标准大小，直接查表
        foreach (var size in BUFFER_SIZES)
        {
            if (size >= minimumLength)
                return size;
        }

        // 大于预定义大小，使用页面对齐
        const int pageSize = 4096;
        return ((minimumLength + pageSize - 1) / pageSize) * pageSize;
    }

    /// <summary>
    /// 获取缓冲区大小对应的池索引
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetBufferSizeIndex(int size)
    {
        for (int i = 0; i < BUFFER_SIZES.Length; i++)
        {
            if (BUFFER_SIZES[i] >= size)
                return i;
        }
        return -1; // 超出预定义范围
    }

    #endregion

    #region 清理和调优

    /// <summary>
    /// 清理多余的缓冲区，释放内存
    /// </summary>
    public void Trim()
    {
        // 清理全局大缓冲区池
        _globalLargeBuffers.Clear();

        // 清理NUMA池
        foreach (var numaNode in _numaNodes)
        {
            numaNode.Trim();
        }

        // 重置线程本地缓存（下次使用时重新创建）
        _threadLocalCache = null;

        _logger.LogInformation("内存池清理完成");
    }

    /// <summary>
    /// 获取池统计信息
    /// </summary>
    public MemoryPoolStatistics GetStatistics()
    {
        return new MemoryPoolStatistics
        {
            TotalRents = _statistics._totalRents,
            TotalReturns = _statistics._totalReturns,
            TotalAllocations = _statistics._allocations,
            TotalAllocatedBytes = _statistics._totalAllocatedBytes,
            L0Hits = _statistics._l0Hits,
            L1Hits = _statistics._l1Hits,
            L2Hits = _statistics._l2Hits,
            L0Returns = _statistics._l0Returns,
            L1Returns = _statistics._l1Returns,
            L2Returns = _statistics._l2Returns,
            Discards = _statistics._discards,
            NumaNodes = _numaNodeCount,
            CacheHitRatio = _statistics.TotalHits > 0 ?
                (double)_statistics.TotalHits / _statistics._totalRents : 0
        };
    }

    #endregion

    #region IDisposable实现

    private bool _disposed;

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        Trim();

        foreach (var numaNode in _numaNodes)
        {
            numaNode.Dispose();
        }

        _disposed = true;
        _logger.LogInformation("分层内存池已释放");
    }

    #endregion
}

#region 支持类型

/// <summary>
/// 线程本地缓冲区缓存
/// </summary>
internal sealed class ThreadLocalBufferCache
{
    private const int MAX_CACHED_BUFFERS = TieredMemoryPool.TierSpecs.L0_MAX_CACHED_BUFFERS;
    private const int MAX_BUFFER_SIZE = TieredMemoryPool.TierSpecs.L0_MAX_BUFFER_SIZE;

    private readonly CachedBuffer[] _cachedBuffers = new CachedBuffer[MAX_CACHED_BUFFERS];
    private int _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRent(int minimumLength, [NotNullWhen(true)] out byte[]? buffer)
    {
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

        buffer = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReturn(byte[] buffer)
    {
        if (_count >= MAX_CACHED_BUFFERS || buffer.Length > MAX_BUFFER_SIZE)
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

/// <summary>
/// NUMA感知缓冲区池
/// </summary>
internal sealed class NumaAwareBufferPool : IDisposable
{
    private readonly ConcurrentQueue<byte[]>[] _pools;
    private readonly long[] _poolCounts;
    private readonly int _numaNodeId;
    private readonly ILogger _logger;

    public NumaAwareBufferPool(int numaNodeId, ILogger logger)
    {
        _numaNodeId = numaNodeId;
        _logger = logger ?? NullLogger.Instance;

        _pools = new ConcurrentQueue<byte[]>[TieredMemoryPool.BUFFER_SIZES.Length];
        _poolCounts = new long[TieredMemoryPool.BUFFER_SIZES.Length];

        for (int i = 0; i < _pools.Length; i++)
        {
            _pools[i] = new ConcurrentQueue<byte[]>();
        }
    }

    public bool TryRent(int poolIndex, [NotNullWhen(true)] out byte[]? buffer)
    {
        if (poolIndex >= 0 && poolIndex < _pools.Length &&
            _pools[poolIndex].TryDequeue(out buffer))
        {
            Interlocked.Decrement(ref _poolCounts[poolIndex]);
            return true;
        }

        buffer = null;
        return false;
    }

    public bool TryReturn(byte[] buffer, int poolIndex)
    {
        if (poolIndex < 0 || poolIndex >= _pools.Length)
            return false;

        var currentCount = Interlocked.Read(ref _poolCounts[poolIndex]);
        if (currentCount >= TieredMemoryPool.TierSpecs.L1_MAX_BUFFERS_PER_POOL)
            return false;

        _pools[poolIndex].Enqueue(buffer);
        Interlocked.Increment(ref _poolCounts[poolIndex]);
        return true;
    }

    public void ReturnBuffer(byte[] buffer, int poolIndex)
    {
        if (poolIndex >= 0 && poolIndex < _pools.Length)
        {
            _pools[poolIndex].Enqueue(buffer);
            Interlocked.Increment(ref _poolCounts[poolIndex]);
        }
    }

    public void Trim()
    {
        for (int i = 0; i < _pools.Length; i++)
        {
            var targetCount = TieredMemoryPool.TierSpecs.L1_MAX_BUFFERS_PER_POOL / 4;
            var currentCount = Interlocked.Read(ref _poolCounts[i]);

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

    public void Dispose()
    {
        Trim();
    }
}

/// <summary>
/// 内存池统计信息
/// </summary>
public struct MemoryPoolStatistics
{
    public long TotalRents { get; set; }
    public long TotalReturns { get; set; }
    public long TotalAllocations { get; set; }
    public long TotalAllocatedBytes { get; set; }
    public long L0Hits { get; set; }
    public long L1Hits { get; set; }
    public long L2Hits { get; set; }
    public long L0Returns { get; set; }
    public long L1Returns { get; set; }
    public long L2Returns { get; set; }
    public long Discards { get; set; }
    public int NumaNodes { get; set; }
    public double CacheHitRatio { get; set; }
}

/// <summary>
/// 池统计计数器
/// </summary>
internal class PoolStatistics
{
    internal long _totalRents;
    internal long _totalReturns;
    internal long _allocations;
    internal long _totalAllocatedBytes;
    internal long _l0Hits;
    internal long _l1Hits;
    internal long _l2Hits;
    internal long _l0Returns;
    internal long _l1Returns;
    internal long _l2Returns;
    internal long _discards;

    public long TotalHits => _l0Hits + _l1Hits + _l2Hits;
}

#endregion
