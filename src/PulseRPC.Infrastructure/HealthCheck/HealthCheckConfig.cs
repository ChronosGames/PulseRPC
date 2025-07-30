using PulseRPC.Infrastructure;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Routing;

namespace PulseRPC.HealthCheck;

/// <summary>
/// 健康检查结果
/// </summary>
public class HealthCheckResult
{
    /// <summary>
    /// 服务编号
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckTime { get; set; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan ResponseTime { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 响应时间 (毫秒)
    /// </summary>
    public long ResponseTimeMs => (long)ResponseTime.TotalMilliseconds;

    /// <summary>
    /// 尝试次数
    /// </summary>
    public int Attempts { get; set; }


    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// 连续失败次数
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// 连续成功次数
    /// </summary>
    public int ConsecutiveSuccesses { get; init; }

    /// <summary>
    /// 错误信息（如果有）
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 额外的检查数据
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }


    /// <summary>
    /// 创建服务端点
    /// </summary>
    /// <param name="id">服务ID</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="weight">权重</param>
    /// <param name="metadata">元数据</param>
    /// <param name="health">健康状态</param>
    // public ServiceEndpoint(
    //     string id,
    //     string serviceName,
    //     string host,
    //     int port,
    //     int weight = 1,
    //     ServiceMetadata? metadata = null,
    //     HealthStatus health = HealthStatus.Unknown)

    public ServiceEndpoint ToEndpoint()
    {
        return new ServiceEndpoint
        {
            ServiceId = $"health_check_{Guid.NewGuid()}",
            ServiceType = "HealthCheck",
            Host = "localhost",
            Port = 0,
            IsHealthy = true,
            Protocol = "Tcp"
        };
    }

    public static HealthCheckResult Healthy(string message, Dictionary<string, object>? data = null)
    {
        return new HealthCheckResult()
        {
            Status = HealthStatus.Healthy,
            ErrorMessage = message,
            Data = data,
        };
    }

    public static HealthCheckResult Unhealthy(string message, Exception ex, Dictionary<string, object>? data = null)
    {
        return new HealthCheckResult()
        {
            Status = HealthStatus.Unhealthy,
            ErrorMessage = ex.Message,
            Data = data,
        };
    }

    public static HealthCheckResult Degraded(string message, Dictionary<string, object>? data = null)
    {
        return new HealthCheckResult()
        {
            Status = HealthStatus.Degraded,
            ErrorMessage = message,
            Data = data,
        };
    }
}

/// <summary>
/// 健康检查配置
/// </summary>
public class HealthCheckConfig
{
    /// <summary>
    /// 健康检查URL
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 检查间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 超时时间
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

    /// <summary>
    /// 初始延迟
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// HTTP方法
    /// </summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// 期望的HTTP状态码
    /// </summary>
    public int ExpectedStatusCode { get; set; } = 200;

    /// <summary>
    /// 期望的响应内容
    /// </summary>
    public string? ExpectedContent { get; set; }

    /// <summary>
    /// 自定义头部
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// TCP检查配置
    /// </summary>
    public TcpCheckConfig? TcpCheck { get; set; }

    /// <summary>
    /// 创建HTTP健康检查配置
    /// </summary>
    /// <param name="url">检查URL</param>
    /// <param name="interval">检查间隔</param>
    /// <returns>健康检查配置</returns>
    public static HealthCheckConfig Http(string url, TimeSpan? interval = null) => new()
    {
        Url = url,
        Interval = interval ?? TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// 创建TCP健康检查配置
    /// </summary>
    /// <param name="host">主机</param>
    /// <param name="port">端口</param>
    /// <param name="interval">检查间隔</param>
    /// <returns>健康检查配置</returns>
    public static HealthCheckConfig Tcp(string host, int port, TimeSpan? interval = null) => new()
    {
        TcpCheck = new TcpCheckConfig { Host = host, Port = port },
        Interval = interval ?? TimeSpan.FromSeconds(30)
    };
}

/// <summary>
/// TCP检查配置
/// </summary>
public class TcpCheckConfig
{
    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 发送数据
    /// </summary>
    public byte[]? SendData { get; set; }

    /// <summary>
    /// 期望接收的数据
    /// </summary>
    public byte[]? ExpectedData { get; set; }
}
