namespace DistributedGameApp.Infrastructure.Sentry;

/// <summary>
/// Sentry 配置选项
/// </summary>
public class SentryOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "Sentry";

    /// <summary>
    /// Sentry DSN
    /// </summary>
    public string Dsn { get; set; } = string.Empty;

    /// <summary>
    /// 环境（Development, Staging, Production）
    /// </summary>
    public string Environment { get; set; } = "Development";

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 发送错误之前的采样率（0.0 - 1.0）
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// 追踪采样率（0.0 - 1.0）
    /// </summary>
    public double TracesSampleRate { get; set; } = 0.1;

    /// <summary>
    /// 是否发送默认PII（个人身份信息）
    /// </summary>
    public bool SendDefaultPii { get; set; } = false;

    /// <summary>
    /// 最大面包屑数量
    /// </summary>
    public int MaxBreadcrumbs { get; set; } = 100;

    /// <summary>
    /// 是否附加堆栈跟踪
    /// </summary>
    public bool AttachStacktrace { get; set; } = true;
}
