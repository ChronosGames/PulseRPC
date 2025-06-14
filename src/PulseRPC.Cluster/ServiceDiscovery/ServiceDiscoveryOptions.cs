namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务发现配置选项
/// </summary>
public class ServiceDiscoveryOptions
{
    /// <summary>
    /// 清理配置
    /// </summary>
    public CleanupOptions Cleanup { get; set; } = new();

    /// <summary>
    /// 清理配置
    /// </summary>
    public class CleanupOptions
    {
        /// <summary>
        /// 是否启用清理
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 服务过期时间
        /// </summary>
        public TimeSpan ServiceExpiration { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 清理间隔
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}

/// <summary>
/// 重试配置选项
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 是否启用重试
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 基础延迟
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大延迟
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 是否添加抖动
    /// </summary>
    public bool UseJitter { get; set; } = true;
}

/// <summary>
/// 超时配置选项
/// </summary>
public class TimeoutOptions
{
    /// <summary>
    /// 默认超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 服务发现超时时间
    /// </summary>
    public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
