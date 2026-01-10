namespace PulseRPC.Abstractions.Transport.Batching;

/// <summary>
/// 传输层背压策略
/// </summary>
public enum TransportBackpressureStrategy
{
    /// <summary>
    /// 阻塞等待队列空间（默认行为）
    /// </summary>
    Block = 0,

    /// <summary>
    /// 丢弃最旧消息以腾出空间
    /// </summary>
    DropOldest = 1,

    /// <summary>
    /// 丢弃新消息（SendAsync 返回 false）
    /// </summary>
    DropNewest = 2,

    /// <summary>
    /// 抛出 BackpressureRejectedException 异常
    /// </summary>
    Reject = 3
}

/// <summary>
/// 背压等级 - 基于队列利用率
/// </summary>
public enum BackpressureLevel
{
    /// <summary>
    /// 正常 - 队列利用率 &lt; ThrottleThreshold (默认 70%)
    /// </summary>
    None = 0,

    /// <summary>
    /// 节流 - 队列利用率在 ThrottleThreshold ~ RejectThreshold 之间
    /// </summary>
    Throttle = 1,

    /// <summary>
    /// 拒绝 - 队列利用率 &gt; RejectThreshold (默认 90%)
    /// </summary>
    Reject = 2
}

/// <summary>
/// 背压拒绝异常
/// </summary>
public sealed class BackpressureRejectedException : Exception
{
    /// <summary>
    /// 当前背压等级
    /// </summary>
    public BackpressureLevel Level { get; }

    /// <summary>
    /// 当前队列深度
    /// </summary>
    public int QueueDepth { get; }

    /// <summary>
    /// 队列容量
    /// </summary>
    public int Capacity { get; }

    public BackpressureRejectedException(
        BackpressureLevel level,
        int queueDepth,
        int capacity)
        : base($"Send rejected due to backpressure (Level: {level}, Queue: {queueDepth}/{capacity})")
    {
        Level = level;
        QueueDepth = queueDepth;
        Capacity = capacity;
    }

    public BackpressureRejectedException(
        BackpressureLevel level,
        int queueDepth,
        int capacity,
        Exception innerException)
        : base($"Send rejected due to backpressure (Level: {level}, Queue: {queueDepth}/{capacity})", innerException)
    {
        Level = level;
        QueueDepth = queueDepth;
        Capacity = capacity;
    }
}
