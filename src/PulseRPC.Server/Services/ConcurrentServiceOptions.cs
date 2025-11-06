namespace PulseRPC.Server;

/// <summary>
/// 并发服务配置选项
/// </summary>
public sealed class ConcurrentServiceOptions
{
    /// <summary>
    /// 最大并发度（同时处理的消息数量）
    /// </summary>
    /// <remarks>
    /// 默认值: 4
    /// 推荐设置:
    /// - IO 密集型服务（数据库查询、HTTP 调用）: 8-16
    /// - CPU 密集型服务（计算任务）: CPU 核心数
    /// - 混合型服务: 4-8
    /// </remarks>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// 消息队列容量
    /// </summary>
    /// <remarks>
    /// 默认值: 10000
    /// 队列满时，新消息会抛出 InvalidOperationException
    /// </remarks>
    public int QueueCapacity { get; set; } = 10000;

    /// <summary>
    /// 是否启用任务清理（自动清理已完成的任务）
    /// </summary>
    /// <remarks>
    /// 默认值: true
    /// 启用后会定期清理已完成的任务，避免内存泄漏
    /// </remarks>
    public bool EnableTaskCleanup { get; set; } = true;

    /// <summary>
    /// 任务清理间隔（毫秒）
    /// </summary>
    /// <remarks>
    /// 默认值: 100ms
    /// 仅在 EnableTaskCleanup = true 时有效
    /// </remarks>
    public int CleanupIntervalMs { get; set; } = 100;

    /// <summary>
    /// 背压策略（队列满时的处理策略）
    /// </summary>
    /// <remarks>
    /// 默认值: Block（阻塞等待）
    ///
    /// 可选策略：
    /// - Block: 队列满时抛出异常，让调用者重试
    /// - DropOldest: 丢弃最旧消息，插入新消息
    /// - DropNewest: 拒绝新消息，保留队列中的消息
    /// - Reject: 拒绝新消息并抛出异常
    /// </remarks>
    public Configuration.BackpressureStrategy BackpressureStrategy { get; set; } = Configuration.BackpressureStrategy.Block;

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    public void Validate()
    {
        if (MaxConcurrency < 1)
            throw new ArgumentException("MaxConcurrency must be at least 1", nameof(MaxConcurrency));

        if (MaxConcurrency > 1000)
            throw new ArgumentException("MaxConcurrency should not exceed 1000", nameof(MaxConcurrency));

        if (QueueCapacity < 1)
            throw new ArgumentException("QueueCapacity must be at least 1", nameof(QueueCapacity));

        if (CleanupIntervalMs < 10)
            throw new ArgumentException("CleanupIntervalMs must be at least 10ms", nameof(CleanupIntervalMs));
    }

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static ConcurrentServiceOptions Default => new();

    /// <summary>
    /// 创建 IO 密集型配置（高并发）
    /// </summary>
    public static ConcurrentServiceOptions ForIOIntensive => new()
    {
        MaxConcurrency = 16,
        QueueCapacity = 10000
    };

    /// <summary>
    /// 创建 CPU 密集型配置（低并发）
    /// </summary>
    public static ConcurrentServiceOptions ForCPUIntensive => new()
    {
        MaxConcurrency = Environment.ProcessorCount,
        QueueCapacity = 5000
    };

    /// <summary>
    /// 克隆配置
    /// </summary>
    public ConcurrentServiceOptions Clone() => new()
    {
        MaxConcurrency = MaxConcurrency,
        QueueCapacity = QueueCapacity,
        EnableTaskCleanup = EnableTaskCleanup,
        CleanupIntervalMs = CleanupIntervalMs,
        BackpressureStrategy = BackpressureStrategy
    };
}
