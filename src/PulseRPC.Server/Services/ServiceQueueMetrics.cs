using System.Diagnostics;

namespace PulseRPC.Server;

/// <summary>
/// Service 消息队列监控指标
/// </summary>
/// <remarks>
/// 提供队列状态和性能指标，用于监控和调试
/// </remarks>
public sealed class ServiceQueueMetrics
{
    private long _totalEnqueued;
    private long _totalProcessed;
    private long _totalDroppedOldest;
    private long _totalDroppedNewest;
    private long _totalRejected;
    private long _totalErrors;
    private long _currentDepth;
    private readonly int _capacity;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="capacity">队列容量</param>
    public ServiceQueueMetrics(int capacity)
    {
        _capacity = capacity;
    }

    /// <summary>
    /// 队列容量
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 当前队列深度
    /// </summary>
    public long CurrentDepth => Interlocked.Read(ref _currentDepth);

    /// <summary>
    /// 队列使用率（0.0 - 1.0）
    /// </summary>
    public double Utilization => (double)CurrentDepth / _capacity;

    /// <summary>
    /// 总入队消息数
    /// </summary>
    public long TotalEnqueued => Interlocked.Read(ref _totalEnqueued);

    /// <summary>
    /// 总处理消息数
    /// </summary>
    public long TotalProcessed => Interlocked.Read(ref _totalProcessed);

    /// <summary>
    /// 总丢弃消息数（DropOldest 策略）
    /// </summary>
    public long TotalDroppedOldest => Interlocked.Read(ref _totalDroppedOldest);

    /// <summary>
    /// 总丢弃消息数（DropNewest 策略）
    /// </summary>
    public long TotalDroppedNewest => Interlocked.Read(ref _totalDroppedNewest);

    /// <summary>
    /// 总拒绝消息数（Reject 策略）
    /// </summary>
    public long TotalRejected => Interlocked.Read(ref _totalRejected);

    /// <summary>
    /// 总错误数
    /// </summary>
    public long TotalErrors => Interlocked.Read(ref _totalErrors);

    /// <summary>
    /// 总丢弃消息数（所有策略）
    /// </summary>
    public long TotalDropped => TotalDroppedOldest + TotalDroppedNewest;

    /// <summary>
    /// 队列运行时间
    /// </summary>
    public TimeSpan Uptime => _uptime.Elapsed;

    /// <summary>
    /// 消息处理速率（消息/秒）
    /// </summary>
    public double ProcessingRate
    {
        get
        {
            var seconds = _uptime.Elapsed.TotalSeconds;
            return seconds > 0 ? TotalProcessed / seconds : 0;
        }
    }

    /// <summary>
    /// 消息丢失率（0.0 - 1.0）
    /// </summary>
    public double DropRate
    {
        get
        {
            var total = TotalEnqueued + TotalRejected;
            return total > 0 ? (double)TotalDropped / total : 0;
        }
    }

    /// <summary>
    /// 消息拒绝率（0.0 - 1.0）
    /// </summary>
    public double RejectRate
    {
        get
        {
            var total = TotalEnqueued + TotalRejected;
            return total > 0 ? (double)TotalRejected / total : 0;
        }
    }

    // ========== 内部方法（供 AuthenticatedServiceMessageQueue 调用） ==========

    /// <summary>
    /// 记录消息入队
    /// </summary>
    internal void RecordEnqueue()
    {
        Interlocked.Increment(ref _totalEnqueued);
        Interlocked.Increment(ref _currentDepth);
    }

    /// <summary>
    /// 记录消息出队
    /// </summary>
    internal void RecordDequeue()
    {
        Interlocked.Decrement(ref _currentDepth);
    }

    /// <summary>
    /// 记录消息处理完成
    /// </summary>
    internal void RecordProcessed()
    {
        Interlocked.Increment(ref _totalProcessed);
    }

    /// <summary>
    /// 记录丢弃最旧消息
    /// </summary>
    internal void RecordDroppedOldest()
    {
        Interlocked.Increment(ref _totalDroppedOldest);
        Interlocked.Decrement(ref _currentDepth);
    }

    /// <summary>
    /// 记录丢弃最新消息
    /// </summary>
    internal void RecordDroppedNewest()
    {
        Interlocked.Increment(ref _totalDroppedNewest);
    }

    /// <summary>
    /// 记录拒绝消息
    /// </summary>
    internal void RecordRejected()
    {
        Interlocked.Increment(ref _totalRejected);
    }

    /// <summary>
    /// 记录处理错误
    /// </summary>
    internal void RecordError()
    {
        Interlocked.Increment(ref _totalErrors);
    }

    /// <summary>
    /// 获取快照（线程安全）
    /// </summary>
    public ServiceQueueMetricsSnapshot GetSnapshot()
    {
        return new ServiceQueueMetricsSnapshot
        {
            Capacity = _capacity,
            CurrentDepth = CurrentDepth,
            Utilization = Utilization,
            TotalEnqueued = TotalEnqueued,
            TotalProcessed = TotalProcessed,
            TotalDroppedOldest = TotalDroppedOldest,
            TotalDroppedNewest = TotalDroppedNewest,
            TotalRejected = TotalRejected,
            TotalErrors = TotalErrors,
            TotalDropped = TotalDropped,
            Uptime = Uptime,
            ProcessingRate = ProcessingRate,
            DropRate = DropRate,
            RejectRate = RejectRate,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 重置所有计数器
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalEnqueued, 0);
        Interlocked.Exchange(ref _totalProcessed, 0);
        Interlocked.Exchange(ref _totalDroppedOldest, 0);
        Interlocked.Exchange(ref _totalDroppedNewest, 0);
        Interlocked.Exchange(ref _totalRejected, 0);
        Interlocked.Exchange(ref _totalErrors, 0);
        Interlocked.Exchange(ref _currentDepth, 0);
        _uptime.Restart();
    }
}

/// <summary>
/// 队列指标快照（不可变）
/// </summary>
public sealed class ServiceQueueMetricsSnapshot
{
    /// <summary>队列容量</summary>
    public required int Capacity { get; init; }

    /// <summary>当前队列深度</summary>
    public required long CurrentDepth { get; init; }

    /// <summary>队列使用率（0.0 - 1.0）</summary>
    public required double Utilization { get; init; }

    /// <summary>总入队消息数</summary>
    public required long TotalEnqueued { get; init; }

    /// <summary>总处理消息数</summary>
    public required long TotalProcessed { get; init; }

    /// <summary>总丢弃消息数（DropOldest）</summary>
    public required long TotalDroppedOldest { get; init; }

    /// <summary>总丢弃消息数（DropNewest）</summary>
    public required long TotalDroppedNewest { get; init; }

    /// <summary>总拒绝消息数</summary>
    public required long TotalRejected { get; init; }

    /// <summary>总错误数</summary>
    public required long TotalErrors { get; init; }

    /// <summary>总丢弃消息数（所有策略）</summary>
    public required long TotalDropped { get; init; }

    /// <summary>队列运行时间</summary>
    public required TimeSpan Uptime { get; init; }

    /// <summary>消息处理速率（消息/秒）</summary>
    public required double ProcessingRate { get; init; }

    /// <summary>消息丢失率（0.0 - 1.0）</summary>
    public required double DropRate { get; init; }

    /// <summary>消息拒绝率（0.0 - 1.0）</summary>
    public required double RejectRate { get; init; }

    /// <summary>快照时间戳</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// 格式化输出
    /// </summary>
    public override string ToString()
    {
        return $"QueueMetrics[Depth={CurrentDepth}/{Capacity} ({Utilization:P1}), " +
               $"Enqueued={TotalEnqueued}, Processed={TotalProcessed}, " +
               $"Dropped={TotalDropped}, Rejected={TotalRejected}, " +
               $"Rate={ProcessingRate:F2}/s, DropRate={DropRate:P2}, RejectRate={RejectRate:P2}]";
    }
}
