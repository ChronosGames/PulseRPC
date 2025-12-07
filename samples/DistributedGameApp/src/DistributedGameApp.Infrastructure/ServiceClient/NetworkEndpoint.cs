namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 网络端点信息（内网或外网）
/// </summary>
public class NetworkEndpoint
{
    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP 端口
    /// </summary>
    public int TcpPort { get; set; }

    /// <summary>
    /// KCP 端口（可选）
    /// </summary>
    public int? KcpPort { get; set; }

    /// <summary>
    /// 公网主机地址（用于 Docker 等场景，如果未设置则使用 Host）
    /// </summary>
    public string? PublicHost { get; set; }

    /// <summary>
    /// 公网 TCP 端口（用于 Docker 端口映射等场景，如果未设置则使用 TcpPort）
    /// </summary>
    public int? PublicTcpPort { get; set; }

    /// <summary>
    /// 公网 KCP 端口（用于 Docker 端口映射等场景，如果未设置则使用 KcpPort）
    /// </summary>
    public int? PublicKcpPort { get; set; }

    /// <summary>
    /// 获取外部客户端使用的主机地址
    /// </summary>
    public string GetPublicHost() => PublicHost ?? Host;

    /// <summary>
    /// 获取外部客户端使用的 TCP 端口
    /// </summary>
    public int GetPublicTcpPort() => PublicTcpPort ?? TcpPort;

    /// <summary>
    /// 获取外部客户端使用的 KCP 端口
    /// </summary>
    public int? GetPublicKcpPort() => PublicKcpPort ?? KcpPort;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取 TCP 连接地址
    /// </summary>
    public string GetTcpAddress() => $"{Host}:{TcpPort}";

    /// <summary>
    /// 获取 KCP 连接地址
    /// </summary>
    public string? GetKcpAddress() => KcpPort.HasValue ? $"{Host}:{KcpPort.Value}" : null;

    public override string ToString() => $"{Host}:{TcpPort}" + (KcpPort.HasValue ? $" (KCP: {KcpPort.Value})" : "");
}
