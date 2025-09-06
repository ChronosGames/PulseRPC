using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PulseRPC.Transport;

// 无锁环形缓冲区 - 支持IO线程零等待投递
public class LockFreeRingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _mask;
    private long _writeIndex;
    private long _readIndex;

    public LockFreeRingBuffer(int capacity)
    {
        // 容量必须是2的幂，支持位运算优化
        capacity = NextPowerOfTwo(capacity);
        _buffer = new T[capacity];
        _mask = capacity - 1;
        _writeIndex = 0;
        _readIndex = 0;
    }

    public int Capacity => _buffer.Length;

    public long WriteIndex => Volatile.Read(ref _writeIndex);

    public long ReadIndex => Volatile.Read(ref _readIndex);

    public int Count => (int)(WriteIndex - ReadIndex);

    public bool IsEmpty => WriteIndex == ReadIndex;

    public bool IsFull => Count >= _buffer.Length;

    // IO线程调用 - 无锁快速投递
    public bool TryEnqueue(T item, TimeSpan timeout = default)
    {
        long currentWriteIndex = Volatile.Read(ref _writeIndex);
        long currentReadIndex = Volatile.Read(ref _readIndex);

        // 检查缓冲区是否已满
        if (currentWriteIndex - currentReadIndex >= _buffer.Length)
        {
            if (timeout == TimeSpan.Zero)
                return false;

            // 使用高精度计时器和智能自旋等待
            var spinWait = new SpinWait();
            var stopwatch = Stopwatch.StartNew();
            long timeoutTicks = (long)(timeout.TotalSeconds * Stopwatch.Frequency);

            do
            {
                spinWait.SpinOnce();
                currentReadIndex = Volatile.Read(ref _readIndex);
            }
            while (currentWriteIndex - currentReadIndex >= _buffer.Length &&
                   stopwatch.ElapsedTicks < timeoutTicks);

            if (currentWriteIndex - currentReadIndex >= _buffer.Length)
                return false;
        }

        // 写入数据 - 使用内存屏障确保写入操作不会被重排序
        _buffer[currentWriteIndex & _mask] = item;
        Thread.MemoryBarrier(); // 确保数据写入完成后再更新索引

        // 原子更新写索引
        Interlocked.Increment(ref _writeIndex);

        return true;
    }

    // 批量写入方法 - 更高效的批量操作
    public int TryEnqueueBatch(T[] items, int offset, int count)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (offset < 0 || offset >= items.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > items.Length) throw new ArgumentOutOfRangeException(nameof(count));

        long currentWriteIndex = Volatile.Read(ref _writeIndex);
        long currentReadIndex = Volatile.Read(ref _readIndex);

        int availableSpace = _buffer.Length - (int)(currentWriteIndex - currentReadIndex);
        if (availableSpace == 0)
            return 0;

        int actualCount = Math.Min(availableSpace, count);

        // 计算写入位置
        int writePos = (int)(currentWriteIndex & _mask);
        int firstSegmentLength = Math.Min(actualCount, _buffer.Length - writePos);

        // 复制第一段数据
        Array.Copy(items, offset, _buffer, writePos, firstSegmentLength);

        // 如果数据需要环绕缓冲区
        if (firstSegmentLength < actualCount)
        {
            Array.Copy(items, offset + firstSegmentLength, _buffer, 0, actualCount - firstSegmentLength);
        }

        // 内存屏障确保数据写入完成
        Thread.MemoryBarrier();

        // 原子更新写索引
        Interlocked.Add(ref _writeIndex, actualCount);

        return actualCount;
    }

    // 处理线程调用 - 批量读取
    public int TryDequeueBatch(T[] items, int offset, int maxCount)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (offset < 0 || offset >= items.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (maxCount < 0 || offset + maxCount > items.Length) throw new ArgumentOutOfRangeException(nameof(maxCount));

        long currentWriteIndex = Volatile.Read(ref _writeIndex);
        long currentReadIndex = Volatile.Read(ref _readIndex);

        int availableCount = (int)(currentWriteIndex - currentReadIndex);
        if (availableCount == 0)
            return 0;

        int actualCount = Math.Min(availableCount, maxCount);

        // 计算读取位置
        int readPos = (int)(currentReadIndex & _mask);
        int firstSegmentLength = Math.Min(actualCount, _buffer.Length - readPos);

        // 复制第一段数据
        Array.Copy(_buffer, readPos, items, offset, firstSegmentLength);

        // 如果数据需要环绕缓冲区
        if (firstSegmentLength < actualCount)
        {
            Array.Copy(_buffer, 0, items, offset + firstSegmentLength, actualCount - firstSegmentLength);
        }

        // 内存屏障确保数据读取完成
        Thread.MemoryBarrier();

        // 原子更新读索引
        Interlocked.Add(ref _readIndex, actualCount);

        return actualCount;
    }

    public int TryDequeueBatch(T[] items, int maxCount)
    {
        return TryDequeueBatch(items, 0, maxCount);
    }

    public bool TryDequeue(out T? item)
    {
        var items = new T[1];
        var count = TryDequeueBatch(items, 1);

        item = count > 0 ? items[0] : default;
        return count > 0;
    }

    // 清空缓冲区
    public void Clear()
    {
        Volatile.Write(ref _readIndex, Volatile.Read(ref _writeIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextPowerOfTwo(int value)
    {
        if (value < 2) return 2;

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}
