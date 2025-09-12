using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// 客户端传输通道配置
/// </summary>
public class ClientTransportConfiguration
{
    /// <summary>
    /// 通道名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Type { get; set; }

    /// <summary>
    /// 服务器主机
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// 服务器端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 传输选项
    /// </summary>
    public TransportOptions? Options { get; set; }

    /// <summary>
    /// 是否为默认传输
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// 创建TCP传输配置
    /// </summary>
    public static ClientTransportConfiguration Tcp(string name, string host, int port, TcpTransportOptions? options = null, bool isDefault = false) =>
        new()
        {
            Name = name,
            Type = TransportType.Tcp,
            Host = host,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };

    /// <summary>
    /// 创建KCP传输配置
    /// </summary>
    public static ClientTransportConfiguration Kcp(string name, string host, int port, KcpTransportOptions? options = null, bool isDefault = false) =>
        new()
        {
            Name = name,
            Type = TransportType.Kcp,
            Host = host,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };
}
