using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PulseRPC.Server.Threading;

/// <summary>
/// 工作窃取队列 - 无锁双端队列实现
///
/// 特性:
/// - 本地线程从底部入队和出队（快速路径）
/// - 其他线程从顶部窃取（窃取路径）
/// - 使用CAS操作保证线程安全
/// - 支持动态扩容
/// </summary>
/// <typeparam name="T">队列元素类型</typeparam>
public sealed class WorkStealingQueue<T> where T : struct
{
    private const int InitialCapacity = 256;
    private const int MaxCapacity = 1024 * 1024; // 1M elements

    private volatile T[] _array;
    private volatile int _mask;
    private volatile int _headIndex;
    private volatile int _tailIndex;

    // 锁，仅在扩容时使用
    private readonly Lock _expandLock = new();

    public WorkStealingQueue()
    {
        _array = new T[InitialCapacity];
        _mask = InitialCapacity - 1;
        _headIndex = 0;
        _tailIndex = 0;
    }

    /// <summary>
    /// 获取队列中元素的近似数量
    /// </summary>
    public int Count
    {
        get
        {
            int head = _headIndex;
            int tail = _tailIndex;
            return tail - head;
        }
    }

    /// <summary>
    /// 检查队列是否为空
    /// </summary>
    public bool IsEmpty => Count <= 0;

    /// <summary>
    /// 本地入队（从底部添加）
    /// 只应该由拥有此队列的线程调用
    /// </summary>
    public bool TryEnqueue(T item)
    {
        int tail = _tailIndex;
        int head = _headIndex;

        // 检查是否需要扩容
        if (tail - head >= _mask)
        {
            if (!TryExpand())
            {
                return false; // 扩容失败
            }
        }

        // 存储元素
        _array[tail & _mask] = item;

        // 更新尾指针（内存屏障）
        Volatile.Write(ref _tailIndex, tail + 1);

        return true;
    }

    /// <summary>
    /// 本地出队（从底部移除）
    /// 只应该由拥有此队列的线程调用
    /// </summary>
    public bool TryDequeue(out T item)
    {
        int tail = _tailIndex - 1;
        Volatile.Write(ref _tailIndex, tail);

        int head = _headIndex;

        if (head <= tail)
        {
            // 队列非空，获取元素
            item = _array[tail & _mask];

            if (head != tail)
            {
                // 还有其他元素，成功
                return true;
            }

            // 只有一个元素，需要与窃取操作竞争
            if (Interlocked.CompareExchange(ref _headIndex, head + 1, head) == head)
            {
                // 成功获取最后一个元素
                return true;
            }
            else
            {
                // 被其他线程窃取了
                item = default;
                return false;
            }
        }
        else
        {
            // 队列为空
            _tailIndex = tail + 1; // 恢复尾指针
            item = default;
            return false;
        }
    }

    /// <summary>
    /// 窃取操作（从顶部移除）
    /// 可以被任何线程调用
    /// </summary>
    public bool TrySteal(out T item)
    {
        int head = _headIndex;

        // 内存屏障，确保读取顺序
        Thread.MemoryBarrier();

        int tail = _tailIndex;

        if (head < tail)
        {
            // 队列非空，尝试窃取
            item = _array[head & _mask];

            // 原子更新头指针
            if (Interlocked.CompareExchange(ref _headIndex, head + 1, head) == head)
            {
                // 窃取成功
                return true;
            }
        }

        // 窃取失败
        item = default;
        return false;
    }

    /// <summary>
    /// 尝试扩容
    /// </summary>
    private bool TryExpand()
    {
        lock (_expandLock)
        {
            // 双重检查，避免重复扩容
            int tail = _tailIndex;
            int head = _headIndex;

            if (tail - head < _mask)
            {
                return true; // 已经被其他线程扩容了
            }

            int currentCapacity = _array.Length;
            if (currentCapacity >= MaxCapacity)
            {
                return false; // 达到最大容量
            }

            // 创建新数组
            int newCapacity = Math.Min(currentCapacity * 2, MaxCapacity);
            T[] newArray = new T[newCapacity];
            int newMask = newCapacity - 1;

            // 复制现有元素
            int count = tail - head;
            for (int i = 0; i < count; i++)
            {
                newArray[i] = _array[(head + i) & _mask];
            }

            // 更新索引
            _headIndex = 0;
            _tailIndex = count;
            _mask = newMask;

            // 更新数组引用（原子操作）
            Volatile.Write(ref _array, newArray);

            return true;
        }
    }

    /// <summary>
    /// 获取队列统计信息
    /// </summary>
    public QueueStatistics GetStatistics()
    {
        return new QueueStatistics
        {
            Capacity = _array.Length,
            Count = Count,
            Utilization = (double)Count / _array.Length,
            HeadIndex = _headIndex,
            TailIndex = _tailIndex
        };
    }

    /// <summary>
    /// 清空队列
    /// 注意：这个操作不是线程安全的，只应该在单线程环境下调用
    /// </summary>
    public void Clear()
    {
        _headIndex = 0;
        _tailIndex = 0;
        Array.Clear(_array, 0, _array.Length);
    }
}

/// <summary>
/// 队列统计信息
/// </summary>
public class QueueStatistics
{
    public int Capacity { get; set; }
    public int Count { get; set; }
    public double Utilization { get; set; }
    public int HeadIndex { get; set; }
    public int TailIndex { get; set; }
}
