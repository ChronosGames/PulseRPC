using System;
using System.Threading;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>
/// 分层消息处理器性能指标
/// </summary>
public sealed class TieredProcessorMetrics
{
    // L1 层指标
    public PerformanceCounter L1MessagesEnqueued { get; } = new();
    public PerformanceCounter L1MessagesDequeued { get; } = new();
    public PerformanceCounter L1BackpressureEvents { get; } = new();

    // L2 层指标
    public PerformanceCounter BatchesCreated { get; } = new();
    public PerformanceCounter BatchesProcessed { get; } = new();
    public PerformanceCounter BatchesErrored { get; } = new();

    // L3 层指标
    public PerformanceCounter MessagesProcessed { get; } = new();
    public PerformanceCounter MessagesErrored { get; } = new();
    public PerformanceCounter MessagesDropped { get; } = new();
    public PerformanceCounter ForcedEnqueues { get; } = new();

    // 延迟指标
    private readonly RingBuffer<TimeSpan> _batchProcessingTimes = new(1000);
    private readonly Lock _latencyLock = new();

    // 吞吐量计算
    private long _lastThroughputCalculation = Environment.TickCount64;
    private long _lastProcessedCount = 0;
    private double _currentThroughput = 0;

    /// <summary>
    /// 当前L1缓冲区利用率
    /// </summary>
    public double CurrentL1Utilization { get; set; }

    /// <summary>
    /// 记录批处理时间
    /// </summary>
    public void RecordBatchProcessingTime(TimeSpan processingTime)
    {
        lock (_latencyLock)
        {
            _batchProcessingTimes.Add(processingTime);
        }
    }

    /// <summary>
    /// 获取平均批处理时间
    /// </summary>
    public TimeSpan GetAverageBatchProcessingTime()
    {
        lock (_latencyLock)
        {
            if (_batchProcessingTimes.Count == 0)
                return TimeSpan.Zero;

            var total = TimeSpan.Zero;
            for (int i = 0; i < _batchProcessingTimes.Count; i++)
            {
                total += _batchProcessingTimes[i];
            }

            return TimeSpan.FromTicks(total.Ticks / _batchProcessingTimes.Count);
        }
    }

    /// <summary>
    /// 获取P95批处理时间
    /// </summary>
    public TimeSpan GetP95BatchProcessingTime()
    {
        lock (_latencyLock)
        {
            if (_batchProcessingTimes.Count == 0)
                return TimeSpan.Zero;

            var times = new TimeSpan[_batchProcessingTimes.Count];
            for (int i = 0; i < _batchProcessingTimes.Count; i++)
            {
                times[i] = _batchProcessingTimes[i];
            }

            Array.Sort(times);
            int p95Index = (int)(times.Length * 0.95);
            return times[Math.Min(p95Index, times.Length - 1)];
        }
    }

    /// <summary>
    /// 获取当前吞吐量 (messages/second)
    /// </summary>
    public double GetCurrentThroughput()
    {
        var now = Environment.TickCount64;
        var currentProcessed = MessagesProcessed.Value;

        if (now - _lastThroughputCalculation >= 1000) // 每秒更新一次
        {
            var timeDelta = (now - _lastThroughputCalculation) / 1000.0;
            var messageDelta = currentProcessed - _lastProcessedCount;

            _currentThroughput = messageDelta / timeDelta;
            _lastThroughputCalculation = now;
            _lastProcessedCount = currentProcessed;
        }

        return _currentThroughput;
    }

    /// <summary>
    /// 重置所有指标
    /// </summary>
    public void Reset()
    {
        L1MessagesEnqueued.Reset();
        L1MessagesDequeued.Reset();
        L1BackpressureEvents.Reset();
        BatchesCreated.Reset();
        BatchesProcessed.Reset();
        BatchesErrored.Reset();
        MessagesProcessed.Reset();
        MessagesErrored.Reset();
        MessagesDropped.Reset();
        ForcedEnqueues.Reset();

        lock (_latencyLock)
        {
            _batchProcessingTimes.Clear();
        }

        _lastThroughputCalculation = Environment.TickCount64;
        _lastProcessedCount = 0;
        _currentThroughput = 0;
    }

    /// <summary>
    /// 获取性能摘要
    /// </summary>
    public PerformanceSummary GetSummary()
    {
        return new PerformanceSummary
        {
            TotalMessagesProcessed = MessagesProcessed.Value,
            TotalMessagesDropped = MessagesDropped.Value,
            TotalBatchesProcessed = BatchesProcessed.Value,
            CurrentThroughput = GetCurrentThroughput(),
            AverageBatchProcessingTime = GetAverageBatchProcessingTime(),
            P95BatchProcessingTime = GetP95BatchProcessingTime(),
            L1BackpressureRate = L1MessagesEnqueued.Value > 0 ?
                (double)L1BackpressureEvents.Value / L1MessagesEnqueued.Value : 0,
            MessageErrorRate = MessagesProcessed.Value > 0 ?
                (double)MessagesErrored.Value / MessagesProcessed.Value : 0
        };
    }
}

/// <summary>
/// 性能计数器
/// </summary>
public sealed class PerformanceCounter
{
    private long _value;

    public long Value => _value;

    public void Increment() => Interlocked.Increment(ref _value);

    public void Add(long amount) => Interlocked.Add(ref _value, amount);

    public void Reset() => Interlocked.Exchange(ref _value, 0);
}

/// <summary>
/// 环形缓冲区用于存储最近的测量值
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public RingBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }

    public int Count => _count;

    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;

        if (_count < _buffer.Length)
            _count++;
    }

    public T this[int index]
    {
        get
        {
            if (index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var actualIndex = (_head - _count + index + _buffer.Length) % _buffer.Length;
            return _buffer[actualIndex];
        }
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
        Array.Clear(_buffer, 0, _buffer.Length);
    }
}

/// <summary>
/// 性能摘要
/// </summary>
public class PerformanceSummary
{
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public long TotalBatchesProcessed { get; set; }
    public double CurrentThroughput { get; set; }
    public TimeSpan AverageBatchProcessingTime { get; set; }
    public TimeSpan P95BatchProcessingTime { get; set; }
    public double L1BackpressureRate { get; set; }
    public double MessageErrorRate { get; set; }
}
