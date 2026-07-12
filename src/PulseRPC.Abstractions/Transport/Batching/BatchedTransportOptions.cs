namespace PulseRPC.Abstractions.Transport.Batching;

/// <summary>
/// 批处理传输配置选项
/// </summary>
public sealed class BatchedTransportOptions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // 批处理配置
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 批处理消息数量阈值，达到此数量后立即刷新
    /// </summary>
    public int BatchThreshold { get; set; } = 16;

    /// <summary>
    /// 批处理字节大小阈值，累积字节数达到此值后立即刷新
    /// </summary>
    public int BatchSizeThreshold { get; set; } = 64 * 1024;

    /// <summary>
    /// 定时刷新间隔，即使未达到阈值也会刷新
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(2);

    /// <summary>
    /// 启用自适应批处理（根据队列深度动态调整阈值）
    /// </summary>
    [Obsolete("BatchedTransport 当前使用显式 BatchThreshold/BatchSizeThreshold；自适应调整尚未实现。")]
    public bool EnableAdaptiveBatching { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════════════════
    // 背压配置
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 发送队列容量
    /// </summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>
    /// 背压策略
    /// </summary>
    public TransportBackpressureStrategy BackpressureStrategy { get; set; } = TransportBackpressureStrategy.Block;

    /// <summary>
    /// 节流阈值（队列利用率百分比），超过此值进入 Throttle 状态
    /// </summary>
    public double ThrottleThreshold { get; set; } = 0.7;

    /// <summary>
    /// 拒绝阈值（队列利用率百分比），超过此值进入 Reject 状态
    /// </summary>
    public double RejectThreshold { get; set; } = 0.9;

    /// <summary>
    /// 滞后阈值（用于防抖），状态降级时需要低于 (阈值 - 滞后) 才会触发
    /// </summary>
    public double HysteresisThreshold { get; set; } = 0.1;

    // ═══════════════════════════════════════════════════════════════════════════
    // 指标配置
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 启用传输层指标收集
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 传输层标识（用于指标标签）
    /// </summary>
    public string TransportId { get; set; } = "";

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">配置值超出有效范围</exception>
    public void Validate()
    {
        if (BatchThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(BatchThreshold), "Must be >= 1");

        if (BatchSizeThreshold < 1024)
            throw new ArgumentOutOfRangeException(nameof(BatchSizeThreshold), "Must be >= 1024");

        if (FlushInterval < TimeSpan.FromMilliseconds(1))
            throw new ArgumentOutOfRangeException(nameof(FlushInterval), "Must be >= 1ms");

        if (QueueCapacity < 10)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity), "Must be >= 10");

        if (ThrottleThreshold <= 0 || ThrottleThreshold >= 1)
            throw new ArgumentOutOfRangeException(nameof(ThrottleThreshold), "Must be between 0 and 1");

        if (RejectThreshold <= ThrottleThreshold || RejectThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(RejectThreshold), "Must be > ThrottleThreshold and <= 1");

        if (HysteresisThreshold < 0 || HysteresisThreshold >= ThrottleThreshold)
            throw new ArgumentOutOfRangeException(nameof(HysteresisThreshold), "Must be >= 0 and < ThrottleThreshold");
    }
}
