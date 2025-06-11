using PulseServiceDiscovery.Abstractions.Enums;

namespace PulseServiceDiscovery.Client.Options;

/// <summary>
/// 客户端配置选项
/// </summary>
public class ClientOptions
{
    /// <summary>
    /// 缓存配置选项
    /// </summary>
    public CacheOptions CacheOptions { get; set; } = new();

    /// <summary>
    /// 负载均衡配置选项
    /// </summary>
    public LoadBalancingOptions LoadBalancingOptions { get; set; } = new();

    /// <summary>
    /// 健康检查配置选项
    /// </summary>
    public HealthCheckOptions HealthCheckOptions { get; set; } = new();

    /// <summary>
    /// 重试配置选项
    /// </summary>
    public RetryOptions RetryOptions { get; set; } = new();

    /// <summary>
    /// 超时配置选项
    /// </summary>
    public TimeoutOptions TimeoutOptions { get; set; } = new();
}

/// <summary>
/// 缓存配置选项
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认TTL
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大缓存条目数
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// 缓存刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 是否在后台刷新
    /// </summary>
    public bool BackgroundRefresh { get; set; } = true;
}

/// <summary>
/// 负载均衡配置选项
/// </summary>
public class LoadBalancingOptions
{
    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public LoadBalancingStrategy Strategy { get; set; } = LoadBalancingStrategy.RoundRobin;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 权重配置
    /// </summary>
    public Dictionary<string, int> Weights { get; set; } = new();

    /// <summary>
    /// 是否启用粘性会话
    /// </summary>
    public bool EnableStickySession { get; set; } = false;

    /// <summary>
    /// 粘性会话TTL
    /// </summary>
    public TimeSpan StickySessionTtl { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// 健康检查配置选项
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 健康检查超时
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 失败阈值
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 成功阈值
    /// </summary>
    public int SuccessThreshold { get; set; } = 1;
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
