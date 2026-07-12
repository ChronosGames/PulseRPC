namespace PulseRPC.Client.Configuration;

/// <summary>
/// 客户端选项
/// </summary>
/// <remarks>
/// <para>
/// The client runtime currently consumes <see cref="Name"/> and
/// <see cref="LoadBalancing"/>. Other shipped properties remain only for source and
/// binary compatibility and are marked obsolete.
/// </para>
/// <code>
/// var client = new PulseClientBuilder()
///     .Configure(options =>
///         options.LoadBalancing.ConsistentHashVirtualNodes = 256)
///     .WithLogging(loggerFactory)
///     .Build();
/// </code>
/// </remarks>
public sealed class ClientOptions
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    public string Name { get; set; } = "PulseRPC-Client";

    /// <summary>
    /// Legacy default timeout that is not propagated to client channels.
    /// </summary>
    [Obsolete("This value is not propagated to generated calls. Use each RPC method's CancellationToken; configure connection/handshake timeouts on ConnectionDescriptor.TransportOptions.", false)]
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Legacy connection limit that is not consumed by ConnectionManager.
    /// </summary>
    [Obsolete("This value is not consumed by ConnectionManager. Manage explicit ConnectionDescriptor registrations instead.", false)]
    public int MaxConcurrentConnections { get; set; } = 100;

    /// <summary>
    /// Legacy debug flag that is not connected to logging.
    /// </summary>
    [Obsolete("This flag is not connected to the client runtime. Configure Microsoft.Extensions.Logging instead.", false)]
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// Legacy statistics switch. Client statistics are always maintained.
    /// </summary>
    [Obsolete("This switch is not consumed; client statistics are always maintained.", false)]
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// Strongly typed connection load-balancing settings.
    /// </summary>
    public ConnectionLoadBalancingOptions LoadBalancing { get; set; } = new();

    /// <summary>
    /// Legacy cleanup interval that is not consumed by ConnectionManager.
    /// </summary>
    [Obsolete("This value is not consumed by ConnectionManager.", false)]
    public TimeSpan AutoCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 自定义设置字典（高级选项）
    /// </summary>
    /// <remarks>
    /// This dictionary is retained for compatibility and is not read by the runtime.
    /// </remarks>
    [Obsolete("This settings dictionary is not read by the client runtime. Use strongly typed effective options instead.", false)]
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
/// Legacy client-side service discovery model.
/// </summary>
/// <remarks>
/// The client runtime does not consume this type. Register explicit client connections;
/// server cluster discovery is provided by PulseRPC.Infrastructure packages.
/// </remarks>
[Obsolete("Client-side service discovery is not connected to the runtime. Register explicit connections; use PulseRPC.Infrastructure for server cluster discovery.", false)]
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
