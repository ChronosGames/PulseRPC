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
