namespace DistributedGameApp.Infrastructure.Health;

/// <summary>
/// HTTP 端点配置选项（用于健康检查和 Prometheus metrics）
/// </summary>
public class HttpEndpointOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "HttpEndpoint";

    /// <summary>
    /// 是否启用 HTTP 端点（默认：true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// HTTP 端口
    /// </summary>
    public int Port { get; set; } = 9090;

    /// <summary>
    /// 绑定地址（默认：0.0.0.0）
    /// </summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>
    /// 是否启用 Prometheus metrics 端点（默认：true）
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 健康检查路径（默认：/health）
    /// </summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>
    /// Metrics 路径（默认：/metrics）
    /// </summary>
    public string MetricsPath { get; set; } = "/metrics";
}