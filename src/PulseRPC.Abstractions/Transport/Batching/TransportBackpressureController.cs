namespace PulseRPC.Abstractions.Transport.Batching;

/// <summary>
/// 传输层背压控制器 - 基于队列利用率的三级背压
/// </summary>
/// <remarks>
/// <para>
/// <strong>背压等级</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>None: 队列利用率 &lt; ThrottleThreshold (默认 70%)</description></item>
/// <item><description>Throttle: 利用率在 ThrottleThreshold ~ RejectThreshold 之间</description></item>
/// <item><description>Reject: 利用率 &gt; RejectThreshold (默认 90%)</description></item>
/// </list>
/// <para>
/// <strong>滞后防抖</strong>：状态降级时需要低于 (阈值 - 滞后) 才会触发，防止状态抖动。
/// </para>
/// </remarks>
public sealed class TransportBackpressureController
{
    private readonly int _capacity;
    private readonly double _throttleThreshold;
    private readonly double _rejectThreshold;
    private readonly double _hysteresis;

    // 使用 int 而非 enum 以支持 Interlocked
    private int _currentLevel;

    /// <summary>
    /// 创建背压控制器
    /// </summary>
    /// <param name="capacity">队列容量</param>
    /// <param name="throttleThreshold">节流阈值 (0-1)</param>
    /// <param name="rejectThreshold">拒绝阈值 (0-1)</param>
    /// <param name="hysteresis">滞后阈值 (0-1)</param>
    public TransportBackpressureController(
        int capacity,
        double throttleThreshold = 0.7,
        double rejectThreshold = 0.9,
        double hysteresis = 0.1)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Must be >= 1");

        _capacity = capacity;
        _throttleThreshold = throttleThreshold;
        _rejectThreshold = rejectThreshold;
        _hysteresis = hysteresis;
        _currentLevel = (int)BackpressureLevel.None;
    }

    /// <summary>
    /// 从配置创建背压控制器
    /// </summary>
    public static TransportBackpressureController FromOptions(BatchedTransportOptions options)
    {
        return new TransportBackpressureController(
            options.QueueCapacity,
            options.ThrottleThreshold,
            options.RejectThreshold,
            options.HysteresisThreshold);
    }

    /// <summary>
    /// 当前背压等级（无锁读取）
    /// </summary>
    public BackpressureLevel CurrentLevel => (BackpressureLevel)Volatile.Read(ref _currentLevel);

    /// <summary>
    /// 队列容量
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 更新背压等级
    /// </summary>
    /// <param name="currentQueueDepth">当前队列深度</param>
    /// <returns>更新后的背压等级</returns>
    public BackpressureLevel Update(int currentQueueDepth)
    {
        double utilization = (double)currentQueueDepth / _capacity;
        int currentLevel = Volatile.Read(ref _currentLevel);
        int newLevel = currentLevel;

        // 升级逻辑（无滞后）
        if (utilization >= _rejectThreshold)
        {
            newLevel = (int)BackpressureLevel.Reject;
        }
        else if (utilization >= _throttleThreshold)
        {
            newLevel = (int)BackpressureLevel.Throttle;
        }
        // 降级逻辑（带滞后防抖）
        else if (currentLevel == (int)BackpressureLevel.Reject &&
                 utilization < _rejectThreshold - _hysteresis)
        {
            newLevel = (int)BackpressureLevel.Throttle;
        }
        else if (currentLevel == (int)BackpressureLevel.Throttle &&
                 utilization < _throttleThreshold - _hysteresis)
        {
            newLevel = (int)BackpressureLevel.None;
        }
        else if (currentLevel == (int)BackpressureLevel.None)
        {
            // 已经是最低等级，保持不变
        }

        // 原子更新
        if (newLevel != currentLevel)
        {
            Interlocked.Exchange(ref _currentLevel, newLevel);
        }

        return (BackpressureLevel)newLevel;
    }

    /// <summary>
    /// 检查是否应该拒绝发送
    /// </summary>
    /// <param name="currentQueueDepth">当前队列深度</param>
    /// <param name="strategy">背压策略</param>
    /// <returns>true 表示应该拒绝</returns>
    public bool ShouldReject(int currentQueueDepth, TransportBackpressureStrategy strategy)
    {
        var level = Update(currentQueueDepth);

        switch (level)
        {
            case BackpressureLevel.Reject:
                // Reject 状态下，除 Block 外全部拒绝
                return strategy != TransportBackpressureStrategy.Block;

            case BackpressureLevel.Throttle:
                // Throttle 仅作为观测信号；拒绝行为必须确定且只在 Reject 阈值发生。
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// 重置背压等级
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _currentLevel, (int)BackpressureLevel.None);
    }
}
