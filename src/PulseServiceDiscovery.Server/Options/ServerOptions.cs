namespace PulseServiceDiscovery.Server.Options;

/// <summary>
/// 服务端配置选项
/// </summary>
public class ServerOptions
{
    /// <summary>
    /// 存储配置
    /// </summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>
    /// 健康检查配置
    /// </summary>
    public ServerHealthCheckOptions HealthCheck { get; set; } = new();

    /// <summary>
    /// 事件配置
    /// </summary>
    public EventOptions Events { get; set; } = new();

    /// <summary>
    /// 清理配置
    /// </summary>
    public CleanupOptions Cleanup { get; set; } = new();
}

/// <summary>
/// 存储配置选项
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// 存储类型 (Memory, File, Database)
    /// </summary>
    public string Type { get; set; } = "Memory";

    /// <summary>
    /// 连接字符串（用于数据库存储）
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 文件路径（用于文件存储）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 最大条目数
    /// </summary>
    public int MaxEntries { get; set; } = 10000;

    /// <summary>
    /// 持久化间隔
    /// </summary>
    public TimeSpan PersistenceInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// 服务端健康检查配置
/// </summary>
public class ServerHealthCheckOptions
{
    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 检查间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 最大并发检查数
    /// </summary>
    public int MaxConcurrentChecks { get; set; } = 50;

    /// <summary>
    /// 连续失败多少次后标记为不健康
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 连续成功多少次后标记为健康
    /// </summary>
    public int SuccessThreshold { get; set; } = 1;
}

/// <summary>
/// 事件配置
/// </summary>
public class EventOptions
{
    /// <summary>
    /// 是否启用事件通知
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 事件队列最大长度
    /// </summary>
    public int MaxQueueLength { get; set; } = 1000;

    /// <summary>
    /// 事件批处理大小
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// 事件处理超时
    /// </summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 清理配置
/// </summary>
public class CleanupOptions
{
    /// <summary>
    /// 是否启用自动清理
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 清理间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 服务过期时间
    /// </summary>
    public TimeSpan ServiceExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 健康检查超时后自动移除
    /// </summary>
    public bool RemoveUnhealthyServices { get; set; } = true;
}
