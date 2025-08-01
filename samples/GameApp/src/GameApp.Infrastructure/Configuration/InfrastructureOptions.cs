namespace GameApp.Infrastructure.Configuration;

/// <summary>
/// 基础设施配置选项
/// </summary>
public class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    /// <summary>
    /// 是否启用调试模式
    /// </summary>
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// 缓存配置
    /// </summary>
    public CacheOptions Cache { get; set; } = new();

    /// <summary>
    /// 日志配置
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();
}

/// <summary>
/// 缓存配置选项
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// 默认缓存过期时间（分钟）
    /// </summary>
    public int DefaultExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// 是否启用分布式缓存
    /// </summary>
    public bool EnableDistributedCache { get; set; } = true;
}

/// <summary>
/// 日志配置选项
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// 是否启用结构化日志
    /// </summary>
    public bool EnableStructuredLogging { get; set; } = true;

    /// <summary>
    /// 日志保留天数
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}
