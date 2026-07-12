namespace PulseRPC.Server.Configuration;

/// <summary>
/// 未接入统一服务管理器的历史 Service 生命周期配置。
/// </summary>
[Obsolete("This options type is not consumed. Configure PulseServiceManagerOptions for managed service lifecycle.", false)]
public sealed class ServiceLifecycleOptions
{
    /// <summary>
    /// 空闲超时时间（无消息处理后自动销毁）
    /// </summary>
    /// <remarks>
    /// 默认值: 5 分钟
    /// 设置为 Timeout.InfiniteTimeSpan 表示永不超时
    /// </remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否启用自动销毁空闲 Service
    /// </summary>
    /// <remarks>
    /// 默认值: true
    /// 设置为 false 时，Service 将不会自动销毁
    /// </remarks>
    public bool EnableAutoDestroy { get; set; } = true;

    /// <summary>
    /// 销毁检查间隔（后台任务检查频率）
    /// </summary>
    /// <remarks>
    /// 默认值: 1 分钟
    /// 后台任务定期检查空闲 Service 并销毁
    /// </remarks>
    public TimeSpan DestroyCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 最大 Service 实例数（每个类型）
    /// </summary>
    /// <remarks>
    /// 默认值: 1000
    /// 超过此数量时，新创建请求将失败
    /// 设置为 -1 表示无限制
    /// </remarks>
    public int MaxInstancesPerType { get; set; } = 1000;

    /// <summary>
    /// 优雅停止超时时间（等待队列清空的最长时间）
    /// </summary>
    /// <remarks>
    /// 默认值: 30 秒
    /// 超时后强制停止，即使队列中还有消息
    /// </remarks>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用引用计数管理
    /// </summary>
    /// <remarks>
    /// 默认值: true
    /// 启用后，使用中的 Service 不会被销毁
    /// </remarks>
    public bool EnableReferenceCount { get; set; } = true;

    /// <summary>
    /// 创建失败时的重试次数
    /// </summary>
    /// <remarks>
    /// 默认值: 3
    /// 创建 Service 失败时的重试次数
    /// </remarks>
    public int CreateRetryCount { get; set; } = 3;

    /// <summary>
    /// 创建重试间隔
    /// </summary>
    /// <remarks>
    /// 默认值: 100 毫秒
    /// </remarks>
    public TimeSpan CreateRetryInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    public void Validate()
    {
        if (IdleTimeout < TimeSpan.Zero && IdleTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentException("IdleTimeout must be positive or Timeout.InfiniteTimeSpan", nameof(IdleTimeout));

        if (DestroyCheckInterval <= TimeSpan.Zero)
            throw new ArgumentException("DestroyCheckInterval must be positive", nameof(DestroyCheckInterval));

        if (MaxInstancesPerType < -1 || MaxInstancesPerType == 0)
            throw new ArgumentException("MaxInstancesPerType must be positive or -1 (unlimited)", nameof(MaxInstancesPerType));

        if (GracefulShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentException("GracefulShutdownTimeout must be positive", nameof(GracefulShutdownTimeout));

        if (CreateRetryCount < 0)
            throw new ArgumentException("CreateRetryCount must be non-negative", nameof(CreateRetryCount));

        if (CreateRetryInterval < TimeSpan.Zero)
            throw new ArgumentException("CreateRetryInterval must be non-negative", nameof(CreateRetryInterval));
    }

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static ServiceLifecycleOptions Default => new();

    /// <summary>
    /// 创建长期运行的 Service 配置（禁用自动销毁）
    /// </summary>
    public static ServiceLifecycleOptions LongRunning => new()
    {
        EnableAutoDestroy = false,
        IdleTimeout = Timeout.InfiniteTimeSpan
    };

    /// <summary>
    /// 创建短期 Service 配置（快速回收）
    /// </summary>
    public static ServiceLifecycleOptions ShortLived => new()
    {
        EnableAutoDestroy = true,
        IdleTimeout = TimeSpan.FromMinutes(1),
        DestroyCheckInterval = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// 创建游戏房间场景配置
    /// </summary>
    public static ServiceLifecycleOptions GameRoom => new()
    {
        EnableAutoDestroy = true,
        IdleTimeout = TimeSpan.FromMinutes(10),
        DestroyCheckInterval = TimeSpan.FromMinutes(2),
        MaxInstancesPerType = 10000,
        EnableReferenceCount = true
    };

    /// <summary>
    /// 创建聊天室场景配置
    /// </summary>
    public static ServiceLifecycleOptions ChatRoom => new()
    {
        EnableAutoDestroy = true,
        IdleTimeout = TimeSpan.FromMinutes(30),
        DestroyCheckInterval = TimeSpan.FromMinutes(5),
        MaxInstancesPerType = 5000,
        EnableReferenceCount = true
    };

    /// <summary>
    /// 克隆配置
    /// </summary>
    public ServiceLifecycleOptions Clone() => new()
    {
        IdleTimeout = IdleTimeout,
        EnableAutoDestroy = EnableAutoDestroy,
        DestroyCheckInterval = DestroyCheckInterval,
        MaxInstancesPerType = MaxInstancesPerType,
        GracefulShutdownTimeout = GracefulShutdownTimeout,
        EnableReferenceCount = EnableReferenceCount,
        CreateRetryCount = CreateRetryCount,
        CreateRetryInterval = CreateRetryInterval
    };
}
