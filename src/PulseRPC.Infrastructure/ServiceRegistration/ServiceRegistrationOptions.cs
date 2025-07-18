using PulseRPC.Cluster;

namespace PulseRPC.ServiceRegistration;

/// <summary>
/// 服务注册中心类型
/// </summary>
public enum ServiceRegistryType
{
    /// <summary>
    /// Consul
    /// </summary>
    Consul,

    /// <summary>
    /// Etcd
    /// </summary>
    Etcd,

    /// <summary>
    /// Zookeeper
    /// </summary>
    Zookeeper,

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
/// 服务ID生成策略
/// </summary>
public enum ServiceIdGenerationStrategy
{
    /// <summary>
    /// 主机名 + 端口
    /// </summary>
    HostNameAndPort,

    /// <summary>
    /// IP地址 + 端口
    /// </summary>
    IpAddressAndPort,

    /// <summary>
    /// GUID
    /// </summary>
    Guid,

    /// <summary>
    /// 自定义
    /// </summary>
    Custom
}

/// <summary>
/// 重试配置选项
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 基础重试延迟
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大重试延迟
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 退避倍数
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// 是否添加随机抖动
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// 抖动系数 (0.0 - 1.0)
    /// </summary>
    public double JitterFactor { get; set; } = 0.1;
}

/// <summary>
/// 服务注册配置选项
/// </summary>
public class ServiceRegistrationOptions
{
    /// <summary>
    /// 是否启用服务注册
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 服务注册类型
    /// </summary>
    public ServiceRegistryType RegistryType { get; set; } = ServiceRegistryType.Consul;

    /// <summary>
    /// Consul 服务器地址
    /// </summary>
    public string? ConsulAddress { get; set; } = "http://localhost:8500";

    /// <summary>
    /// Etcd 端点列表
    /// </summary>
    public string[]? EtcdEndpoints { get; set; }

    /// <summary>
    /// 服务发现超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 自动注册的服务列表
    /// </summary>
    public List<ServiceInfo> AutoRegisterServices { get; set; } = new();

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 健康检查超时时间
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 健康检查URL路径
    /// </summary>
    public string HealthCheckPath { get; set; } = "/health";

    /// <summary>
    /// 是否启用心跳
    /// </summary>
    public bool EnableHeartbeat { get; set; } = true;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 心跳超时时间
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 服务实例ID生成策略
    /// </summary>
    public ServiceIdGenerationStrategy IdGenerationStrategy { get; set; } = ServiceIdGenerationStrategy.HostNameAndPort;

    /// <summary>
    /// 自定义服务ID前缀
    /// </summary>
    public string? ServiceIdPrefix { get; set; }

    /// <summary>
    /// 默认标签
    /// </summary>
    public Dictionary<string, string> DefaultTags { get; set; } = new();

    /// <summary>
    /// 默认元数据
    /// </summary>
    public Dictionary<string, string> DefaultMetadata { get; set; } = new();

    /// <summary>
    /// 重试配置
    /// </summary>
    public RetryOptions RetryOptions { get; set; } = new();

    /// <summary>
    /// 是否在应用停止时自动注销服务
    /// </summary>
    public bool AutoUnregisterOnShutdown { get; set; } = true;

    public CleanupOptions CleanupOptions { get; set; } = new();

    public TimeSpan ServiceExpiration { get; set; }

    /// <summary>
    /// 服务权重（用于负载均衡）
    /// </summary>
    public int Weight { get; set; } = 100;

    /// <summary>
    /// 是否启用TTL模式（Time To Live）
    /// </summary>
    public bool EnableTtl { get; set; }

    /// <summary>
    /// TTL 时间（TTL模式下服务的生存时间）
    /// </summary>
    public TimeSpan TtlDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// TTL 续期间隔
    /// </summary>
    public TimeSpan TtlRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 心跳配置选项
/// </summary>
public class HeartbeatOptions
{
    /// <summary>
    /// 是否启用心跳
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 心跳超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
