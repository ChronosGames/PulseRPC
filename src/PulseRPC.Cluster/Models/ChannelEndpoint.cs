using PulseRPC.Transport;

namespace PulseRPC.Cluster;

/// <summary>
/// 通道端点 - 表示一个传输通道及其网络地址
/// </summary>
public class ChannelEndpoint
{
    /// <summary>
    /// 通道唯一标识
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// 通道名称（对应 [Channel] 特性中的名称）
    /// </summary>
    public required string ChannelName { get; init; }

    /// <summary>
    /// 传输协议类型
    /// </summary>
    public TransportProtocol Protocol { get; init; } = TransportProtocol.Tcp;

    /// <summary>
    /// 网络地址
    /// </summary>
    public required NetworkAddress Address { get; init; }

    /// <summary>
    /// 通道特性（如支持的序列化格式、压缩等）
    /// </summary>
    public ChannelCapabilities Capabilities { get; init; } = new();

    /// <summary>
    /// 负载均衡权重
    /// </summary>
    public int Weight { get; init; } = 100;

    /// <summary>
    /// 通道元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    public override string ToString() => $"{ChannelName}@{Address}";
}

/// <summary>
/// 传输协议类型
/// </summary>
public enum TransportProtocol
{
    Tcp,
    Kcp,
    Http2,
    WebSocket,
    Quic
}

/// <summary>
/// 网络地址
/// </summary>
public class NetworkAddress
{
    /// <summary>
    /// 主机地址
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// 端口号
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// 路径（用于 HTTP/WebSocket）
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// 是否使用 TLS
    /// </summary>
    public bool UseTls { get; init; } = false;

    /// <summary>
    /// 网络地址的字符串表示
    /// </summary>
    public override string ToString()
    {
        var scheme = UseTls ? "tls://" : "tcp://";
        var pathPart = !string.IsNullOrEmpty(Path) ? Path : "";
        return $"{scheme}{Host}:{Port}{pathPart}";
    }
}

/// <summary>
/// 通道能力
/// </summary>
public class ChannelCapabilities
{
    /// <summary>
    /// 支持的序列化格式
    /// </summary>
    public List<string> SupportedSerializers { get; init; } = new() { "MemoryPack" };

    /// <summary>
    /// 支持压缩
    /// </summary>
    public bool SupportsCompression { get; init; } = false;

    /// <summary>
    /// 支持流式传输
    /// </summary>
    public bool SupportsStreaming { get; init; } = false;

    /// <summary>
    /// 最大并发连接数
    /// </summary>
    public int MaxConcurrentConnections { get; init; } = 1000;

    /// <summary>
    /// 支持的身份验证方式
    /// </summary>
    public List<string> SupportedAuthMethods { get; init; } = new();
}
