namespace PulseRPC.Client.Configuration;

/// <summary>
/// 客户端选项
/// </summary>
/// <remarks>
/// <para>推荐使用 <see cref="ClientPresets"/> 预设配置，减少手动配置。</para>
/// <code>
/// // 使用预设配置
/// var client = new PulseClientBuilder()
///     .UseGameClientPreset()  // 或 UseDefaults(), UseDevelopmentPreset()
///     .WithLogging(loggerFactory)
///     .Build();
///
/// // 或使用静态工厂方法
/// var client = PulseClientBuilder.CreateGameClient(loggerFactory);
/// </code>
/// </remarks>
public sealed class ClientOptions
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    public string Name { get; set; } = "PulseRPC-Client";

    /// <summary>
    /// 默认超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 最大并发连接数
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 100;

    /// <summary>
    /// 启用调试模式
    /// </summary>
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// 启用统计信息收集
    /// </summary>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// Strongly typed connection load-balancing settings.
    /// </summary>
    public ConnectionLoadBalancingOptions LoadBalancing { get; set; } = new();

    /// <summary>
    /// 自动清理间隔（高级选项，一般无需修改）
    /// </summary>
    public TimeSpan AutoCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 自定义设置字典（高级选项）
    /// </summary>
    /// <remarks>
    /// 用于存储自定义键值对配置，一般无需使用。
    /// 推荐使用强类型配置属性替代。
    /// </remarks>
    public Dictionary<string, string> Settings { get; set; } = new();
}

/// <summary>
/// 服务发现类型
/// </summary>
public enum ServiceDiscoveryType
{
    /// <summary>
    /// 静态配置
    /// </summary>
    Static,

    /// <summary>
    /// Consul
    /// </summary>
    Consul,

    /// <summary>
    /// Etcd
    /// </summary>
    Etcd,

    /// <summary>
    /// DNS
    /// </summary>
    Dns,

    /// <summary>
    /// 自定义
    /// </summary>
    Custom
}

/// <summary>
/// 服务发现配置选项
/// </summary>
public class ServiceDiscoveryOptions
{
    /// <summary>
    /// 服务发现类型
    /// </summary>
    public ServiceDiscoveryType Type { get; set; } = ServiceDiscoveryType.Static;

    /// <summary>
    /// 是否启用服务发现
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Consul 服务器地址
    /// </summary>
    public string? ConsulAddress { get; set; } = "http://localhost:8500";

    /// <summary>
    /// Etcd 端点列表
    /// </summary>
    public string[]? EtcdEndpoints { get; set; }

    /// <summary>
    /// DNS 解析域名
    /// </summary>
    public string? DnsDomain { get; set; }

    /// <summary>
    /// 静态端点配置 (服务名 -> 端点列表)
    /// </summary>
    public Dictionary<string, string[]> StaticEndpoints { get; set; } = new();

    /// <summary>
    /// 服务发现刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用服务监听 (实时更新)
    /// </summary>
    public bool EnableWatch { get; set; } = true;

    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// 缓存超时时间
    /// </summary>
    public TimeSpan CacheTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 服务发现超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 标签过滤条件
    /// </summary>
    public Dictionary<string, string> TagFilters { get; set; } = new();

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// 重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 是否启用故障转移
    /// </summary>
    public bool EnableFailover { get; set; } = true;
}

// 连接池配置选项（保留的设计草稿，尚未公开）。
// public class ConnectionPoolOptions
// {
//     /// <summary>
//     /// 是否启用连接池
//     /// </summary>
//     public bool Enabled { get; set; } = true;
//
//     /// <summary>
//     /// 最大连接数
//     /// </summary>
//     public int MaxConnections { get; set; } = 100;
//
//     /// <summary>
//     /// 最小连接数
//     /// </summary>
//     public int MinConnections { get; set; } = 10;
//
//     /// <summary>
//     /// 连接空闲超时时间
//     /// </summary>
//     public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
//
//     /// <summary>
//     /// 连接生存时间
//     /// </summary>
//     public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(1);
//
//     /// <summary>
//     /// 获取连接超时时间
//     /// </summary>
//     public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(10);
//
//     /// <summary>
//     /// 连接验证间隔
//     /// </summary>
//     public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromMinutes(5);
//
//     /// <summary>
//     /// 是否在借用时验证连接
//     /// </summary>
//     public bool ValidateOnBorrow { get; set; } = true;
//
//     /// <summary>
//     /// 是否在归还时验证连接
//     /// </summary>
//     public bool ValidateOnReturn { get; set; } = false;
//
//     /// <summary>
//     /// 是否在空闲时验证连接
//     /// </summary>
//     public bool ValidateOnIdle { get; set; } = true;
// }
