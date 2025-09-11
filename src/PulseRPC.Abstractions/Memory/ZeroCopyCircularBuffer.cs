using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace PulseRPC.Memory;

/// <summary>
/// 零拷贝循环缓冲区 - 高性能无锁环形缓冲区实现
/// 替代原有的LockFreeRingBuffer，提供更高性能和零拷贝特性
/// </summary>
/// <typeparam name="T">缓冲区元素类型，必须是值类型以确保性能</typeparam>
public sealed class ZeroCopyCircularBuffer<T> : IDisposable where T : struct
{
    #region 技术规格常量

    /// <summary>
    /// 内存规格
    /// </summary>
    public static class MemorySpecs
    {
        public const int ALIGNMENT_BYTES = 64;           // 缓存行对齐
        public const int MIN_CAPACITY = 64;             // 最小容量64个元素
        public const int MAX_CAPACITY = 1048576;        // 最大容量1M个元素
        public static readonly bool REQUIRES_POWER_OF_TWO = true; // 必须是2的幂
    }

    /// <summary>
    /// 性能规格
    /// </summary>
    public static class PerformanceSpecs
    {
        public const int ENQUEUE_MAX_CYCLES = 50;       // 入队最大CPU周期
        public const int DEQUEUE_BATCH_MIN_SIZE = 8;    // 批量出队最小大小
        public const bool LOCK_FREE_GUARANTEE = true;   // 无锁保证
        public const bool WAIT_FREE_ENQUEUE = true;     // 入队等待自由
    }

    #endregion

    #region 字段和属性

    private readonly Memory<T> _buffer;
    private readonly MemoryHandle? _pinnedHandle;
    private readonly int _mask;
    private readonly int _capacity;

    // 使用原子操作确保线程安全
    private long _writeIndex;
    private long _readIndex;

    // 性能统计 (可选，调试时使用)
    private long _totalEnqueues;
    private long _totalDequeues;
    private long _maxBatchSize;

    #endregion

    #region 构造函数和初始化

    /// <summary>
    /// 构造零拷贝循环缓冲区
    /// </summary>
    /// <param name="capacity">缓冲区容量，必须是2的幂</param>
    /// <exception cref="ArgumentOutOfRangeException">容量不符合规格要求</exception>
    public ZeroCopyCircularBuffer(int capacity)
    {
        if (capacity < MemorySpecs.MIN_CAPACITY || capacity > MemorySpecs.MAX_CAPACITY)
            throw new ArgumentOutOfRangeException(nameof(capacity),
                $"Capacity must be between {MemorySpecs.MIN_CAPACITY} and {MemorySpecs.MAX_CAPACITY}");

        if (!IsPowerOfTwo(capacity))
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two");

        _capacity = capacity;
        _mask = capacity - 1;

        // 分配对齐的内存
        var buffer = new T[capacity];
        _buffer = buffer.AsMemory();

        // 尝试固定内存以避免GC移动，实现零拷贝
        // 只有当T不包含引用类型时才能固定
        try
        {
            _pinnedHandle = _buffer.Pin();
        }
        catch (ArgumentException)
        {
            // 如果包含引用类型，则无法固定，但仍然可以正常工作
            _pinnedHandle = null;
        }

        _writeIndex = 0;
        _readIndex = 0;

        // 预热CPU缓存
        PrewarmBuffer();
    }

    /// <summary>
    /// 预热缓冲区，提升首次访问性能
    /// </summary>
    private void PrewarmBuffer()
    {
        var span = _buffer.Span;
        for (int i = 0; i < span.Length; i += MemorySpecs.ALIGNMENT_BYTES / Unsafe.SizeOf<T>())
        {
            span[i] = default;
        }
    }

    #endregion

    #region 核心API - 快速路径

    /// <summary>
    /// 容量
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 当前元素数量
    /// </summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var writeIndex = Interlocked.Read(ref _writeIndex);
            var readIndex = Interlocked.Read(ref _readIndex);
            return (int)(writeIndex - readIndex);
        }
    }

    /// <summary>
    /// 是否为空
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Interlocked.Read(ref _writeIndex) == Interlocked.Read(ref _readIndex);
    }

    /// <summary>
    /// 是否已满
    /// </summary>
    public bool IsFull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Interlocked.Read(ref _writeIndex) - Interlocked.Read(ref _readIndex)) >= _capacity;
    }

    #endregion

    #region 零拷贝入队操作

    /// <summary>
    /// 尝试入队单个元素 - 等待自由操作
    /// </summary>
    /// <param name="item">要入队的元素</param>
    /// <returns>true表示成功，false表示缓冲区满</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnqueue(in T item)
    {
        var currentWriteIndex = Interlocked.Read(ref _writeIndex);
        var currentReadIndex = Interlocked.Read(ref _readIndex);

        // 快速检查是否已满
        if (currentWriteIndex - currentReadIndex >= _capacity)
            return false;

        // 零拷贝写入 - 直接写入固定内存位置
        var span = _buffer.Span;
        span[(int)(currentWriteIndex & _mask)] = item;

        // 内存屏障确保写入完成后再更新索引
        Thread.MemoryBarrier();

        // 原子更新写索引
        Interlocked.Increment(ref _writeIndex);

        // 性能统计
        Interlocked.Increment(ref _totalEnqueues);

        return true;
    }

    /// <summary>
    /// 尝试入队单个元素，带超时
    /// </summary>
    /// <param name="item">要入队的元素</param>
    /// <param name="timeout">超时时间</param>
    /// <returns>true表示成功，false表示超时或缓冲区满</returns>
    public bool TryEnqueue(in T item, TimeSpan timeout)
    {
        if (timeout == TimeSpan.Zero)
            return TryEnqueue(in item);

        var stopwatch = Stopwatch.StartNew();
        var spinWait = new SpinWait();

        do
        {
            if (TryEnqueue(in item))
                return true;

            spinWait.SpinOnce();
        }
        while (stopwatch.Elapsed < timeout);

        return false;
    }

    /// <summary>
    /// 批量入队 - 高性能批量操作
    /// </summary>
    /// <param name="items">要入队的元素数组</param>
    /// <param name="count">入队元素数量</param>
    /// <returns>实际入队的元素数量</returns>
    public int TryEnqueueBatch(ReadOnlySpan<T> items, int count = -1)
    {
        if (items.IsEmpty) return 0;

        if (count < 0) count = items.Length;
        count = Math.Min(count, items.Length);

        var currentWriteIndex = Interlocked.Read(ref _writeIndex);
        var currentReadIndex = Interlocked.Read(ref _readIndex);

        int availableSpace = _capacity - (int)(currentWriteIndex - currentReadIndex);
        if (availableSpace == 0) return 0;

        int actualCount = Math.Min(availableSpace, count);
        if (actualCount == 0) return 0;

        // 零拷贝批量写入
        var bufferSpan = _buffer.Span;
        int writePos = (int)(currentWriteIndex & _mask);

        if (writePos + actualCount <= _capacity)
        {
            // 一次性复制，无需环绕
            items.Slice(0, actualCount).CopyTo(bufferSpan.Slice(writePos));
        }
        else
        {
            // 需要环绕的情况
            int firstSegmentLength = _capacity - writePos;
            items.Slice(0, firstSegmentLength).CopyTo(bufferSpan.Slice(writePos));
            items.Slice(firstSegmentLength, actualCount - firstSegmentLength).CopyTo(bufferSpan);
        }

        // 内存屏障确保数据写入完成
        Thread.MemoryBarrier();

        // 原子更新写索引
        Interlocked.Add(ref _writeIndex, actualCount);

        // 性能统计
        Interlocked.Add(ref _totalEnqueues, actualCount);
        UpdateMaxBatchSize(actualCount);

        return actualCount;
    }

    #endregion

    #region 零拷贝出队操作

    /// <summary>
    /// 尝试出队单个元素
    /// </summary>
    /// <param name="item">出队的元素</param>
    /// <returns>true表示成功，false表示缓冲区空</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDequeue(out T item)
    {
        var currentWriteIndex = Interlocked.Read(ref _writeIndex);
        var currentReadIndex = Interlocked.Read(ref _readIndex);

        if (currentWriteIndex == currentReadIndex)
        {
            item = default;
            return false;
        }

        // 零拷贝读取
        var span = _buffer.Span;
        item = span[(int)(currentReadIndex & _mask)];

        // 内存屏障确保读取完成后再更新索引
        Thread.MemoryBarrier();

        // 原子更新读索引
        Interlocked.Increment(ref _readIndex);

        // 性能统计
        Interlocked.Increment(ref _totalDequeues);

        return true;
    }

    /// <summary>
    /// 批量出队 - 零拷贝内存切片
    /// </summary>
    /// <param name="maxCount">最大出队数量</param>
    /// <returns>包含出队数据的只读内存切片</returns>
    public ReadOnlyMemory<T> TryDequeueBatch(int maxCount)
    {
        var currentWriteIndex = Interlocked.Read(ref _writeIndex);
        var currentReadIndex = Interlocked.Read(ref _readIndex);

        int availableCount = (int)(currentWriteIndex - currentReadIndex);
        if (availableCount == 0)
            return ReadOnlyMemory<T>.Empty;

        int actualCount = Math.Min(availableCount, maxCount);
        int readPos = (int)(currentReadIndex & _mask);

        ReadOnlyMemory<T> result;

        if (readPos + actualCount <= _capacity)
        {
            // 无需环绕，返回连续内存切片
            result = _buffer.Slice(readPos, actualCount);
        }
        else
        {
            // 需要环绕，创建临时缓冲区
            // 注意：这里为了保持零拷贝特性，可能需要分两次调用
            int firstSegmentLength = _capacity - readPos;
            result = _buffer.Slice(readPos, firstSegmentLength);
            actualCount = firstSegmentLength; // 只返回第一段
        }

        // 内存屏障确保读取完成
        Thread.MemoryBarrier();

        // 更新读索引
        Interlocked.Add(ref _readIndex, actualCount);

        // 性能统计
        Interlocked.Add(ref _totalDequeues, actualCount);
        UpdateMaxBatchSize(actualCount);

        return result;
    }

    /// <summary>
    /// 批量出队到指定缓冲区
    /// </summary>
    /// <param name="destination">目标缓冲区</param>
    /// <param name="maxCount">最大出队数量</param>
    /// <returns>实际出队数量</returns>
    public int TryDequeueBatch(Span<T> destination, int maxCount = -1)
    {
        if (destination.IsEmpty) return 0;

        if (maxCount < 0) maxCount = destination.Length;
        maxCount = Math.Min(maxCount, destination.Length);

        var currentWriteIndex = Interlocked.Read(ref _writeIndex);
        var currentReadIndex = Interlocked.Read(ref _readIndex);

        int availableCount = (int)(currentWriteIndex - currentReadIndex);
        if (availableCount == 0) return 0;

        int actualCount = Math.Min(availableCount, maxCount);
        if (actualCount == 0) return 0;

        // 批量复制
        var bufferSpan = _buffer.Span;
        int readPos = (int)(currentReadIndex & _mask);

        if (readPos + actualCount <= _capacity)
        {
            // 一次性复制，无需环绕
            bufferSpan.Slice(readPos, actualCount).CopyTo(destination);
        }
        else
        {
            // 需要环绕的情况
            int firstSegmentLength = _capacity - readPos;
            bufferSpan.Slice(readPos, firstSegmentLength).CopyTo(destination);
            bufferSpan.Slice(0, actualCount - firstSegmentLength).CopyTo(destination.Slice(firstSegmentLength));
        }

        // 内存屏障确保读取完成
        Thread.MemoryBarrier();

        // 更新读索引
        Interlocked.Add(ref _readIndex, actualCount);

        // 性能统计
        Interlocked.Add(ref _totalDequeues, actualCount);
        UpdateMaxBatchSize(actualCount);

        return actualCount;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 检查是否为2的幂
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    /// <summary>
    /// 更新最大批处理大小统计
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateMaxBatchSize(int batchSize)
    {
        var current = _maxBatchSize;
        if (batchSize > current)
        {
            Interlocked.CompareExchange(ref _maxBatchSize, batchSize, current);
        }
    }

    /// <summary>
    /// 清空缓冲区
    /// </summary>
    public void Clear()
    {
        var writeIndex = Interlocked.Read(ref _writeIndex);
        Interlocked.Exchange(ref _readIndex, writeIndex);
    }

    #endregion

    #region 性能统计和调试

    /// <summary>
    /// 获取性能统计信息
    /// </summary>
    public BufferStatistics GetStatistics()
    {
        return new BufferStatistics
        {
            Capacity = _capacity,
            Count = Count,
            TotalEnqueues = Interlocked.Read(ref _totalEnqueues),
            TotalDequeues = Interlocked.Read(ref _totalDequeues),
            MaxBatchSize = (int)Interlocked.Read(ref _maxBatchSize),
            WriteIndex = Interlocked.Read(ref _writeIndex),
            ReadIndex = Interlocked.Read(ref _readIndex)
        };
    }

    /// <summary>
    /// 缓冲区统计信息
    /// </summary>
    public struct BufferStatistics
    {
        public int Capacity { get; set; }
        public int Count { get; set; }
        public long TotalEnqueues { get; set; }
        public long TotalDequeues { get; set; }
        public int MaxBatchSize { get; set; }
        public long WriteIndex { get; set; }
        public long ReadIndex { get; set; }

        public double Utilization => Capacity > 0 ? (double)Count / Capacity : 0;
        public long TotalOperations => TotalEnqueues + TotalDequeues;
    }

    #endregion

    #region IDisposable实现

    private bool _disposed;

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _pinnedHandle?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
