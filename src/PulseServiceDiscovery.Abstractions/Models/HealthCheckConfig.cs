namespace PulseServiceDiscovery.Abstractions.Models;

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
