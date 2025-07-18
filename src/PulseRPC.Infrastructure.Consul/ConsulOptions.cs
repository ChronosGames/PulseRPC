namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// Consul配置选项
/// </summary>
public record ConsulOptions
{
    /// <summary>
    /// Consul服务器端点
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:8500";
    
    /// <summary>
    /// Consul数据中心
    /// </summary>
    public string? Datacenter { get; init; }
    
    /// <summary>
    /// Consul访问令牌
    /// </summary>
    public string? Token { get; init; }
    
    /// <summary>
    /// 健康检查配置
    /// </summary>
    public ConsulHealthCheckOptions HealthCheck { get; init; } = new();
    
    /// <summary>
    /// 服务发现配置
    /// </summary>
    public ConsulDiscoveryOptions DiscoveryOptions { get; init; } = new();
    
    /// <summary>
    /// 连接配置
    /// </summary>
    public ConsulConnectionOptions Connection { get; init; } = new();
    
    /// <summary>
    /// 安全配置
    /// </summary>
    public ConsulSecurityOptions Security { get; init; } = new();
    
    /// <summary>
    /// 重试配置
    /// </summary>
    public ConsulRetryOptions Retry { get; init; } = new();
}

/// <summary>
/// Consul健康检查配置
/// </summary>
public record ConsulHealthCheckOptions
{
    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 健康检查超时时间
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// 服务失败后自动注销时间
    /// </summary>
    public TimeSpan DeregisterAfter { get; init; } = TimeSpan.FromMinutes(10);
    
    /// <summary>
    /// 健康检查类型 (TTL, HTTP, TCP, SCRIPT)
    /// </summary>
    public string CheckType { get; init; } = "TTL";
    
    /// <summary>
    /// HTTP健康检查路径
    /// </summary>
    public string? HttpPath { get; init; } = "/health";
    
    /// <summary>
    /// HTTP健康检查头部
    /// </summary>
    public Dictionary<string, string>? HttpHeaders { get; init; }
    
    /// <summary>
    /// 是否启用TLS跳过验证
    /// </summary>
    public bool TlsSkipVerify { get; init; } = false;
}

/// <summary>
/// Consul服务发现配置
/// </summary>
public record ConsulDiscoveryOptions
{
    /// <summary>
    /// 是否只返回健康的服务
    /// </summary>
    public bool HealthyOnly { get; init; } = true;
    
    /// <summary>
    /// 是否使用本地缓存
    /// </summary>
    public bool UseCache { get; init; } = true;
    
    /// <summary>
    /// 缓存生存时间
    /// </summary>
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// 服务标签过滤
    /// </summary>
    public string[]? Tags { get; init; }
    
    /// <summary>
    /// 是否启用服务监听
    /// </summary>
    public bool EnableWatching { get; init; } = true;
    
    /// <summary>
    /// 监听阻塞查询超时时间
    /// </summary>
    public TimeSpan WatchTimeout { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// 监听重试间隔
    /// </summary>
    public TimeSpan WatchRetryInterval { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 服务刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 预取服务列表
    /// </summary>
    public string[]? PreloadServices { get; init; }
}

/// <summary>
/// Consul连接配置
/// </summary>
public record ConsulConnectionOptions
{
    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; init; } = 3;
    
    /// <summary>
    /// 重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    
    /// <summary>
    /// 是否启用HTTP2
    /// </summary>
    public bool UseHttp2 { get; init; } = false;
    
    /// <summary>
    /// 连接池大小
    /// </summary>
    public int PoolSize { get; init; } = 10;
    
    /// <summary>
    /// 连接保持时间
    /// </summary>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Consul安全配置
/// </summary>
public record ConsulSecurityOptions
{
    /// <summary>
    /// 是否启用TLS
    /// </summary>
    public bool EnableTls { get; init; } = false;
    
    /// <summary>
    /// TLS证书文件路径
    /// </summary>
    public string? CertificatePath { get; init; }
    
    /// <summary>
    /// TLS私钥文件路径
    /// </summary>
    public string? PrivateKeyPath { get; init; }
    
    /// <summary>
    /// CA证书文件路径
    /// </summary>
    public string? CaCertificatePath { get; init; }
    
    /// <summary>
    /// 是否跳过TLS验证
    /// </summary>
    public bool InsecureSkipTlsVerify { get; init; } = false;
    
    /// <summary>
    /// 服务器名称（用于TLS验证）
    /// </summary>
    public string? ServerName { get; init; }
}

/// <summary>
/// Consul重试配置
/// </summary>
public record ConsulRetryOptions
{
    /// <summary>
    /// 是否启用重试
    /// </summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxAttempts { get; init; } = 3;
    
    /// <summary>
    /// 初始重试延迟
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    
    /// <summary>
    /// 最大重试延迟
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 重试延迟倍数
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;
    
    /// <summary>
    /// 随机化因子
    /// </summary>
    public double JitterFactor { get; init; } = 0.1;
    
    /// <summary>
    /// 可重试的异常类型
    /// </summary>
    public HashSet<Type> RetryableExceptions { get; init; } = new()
    {
        typeof(TimeoutException),
        typeof(HttpRequestException),
        typeof(TaskCanceledException)
    };
}

