using PulseRPC.Shared;

using PulseRPC.Client;

namespace PulseRPC.Client.Configuration;

/// <summary>
/// 连接配置 - 简化的连接配置类，用于灵活的连接创建
/// </summary>
public sealed class ConnectionConfig
{
    /// <summary>
    /// 连接名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称（用于服务发现）
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 主机地址（直连时使用）
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// 端口号（直连时使用）
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.TCP;

    /// <summary>
    /// 连接生命周期
    /// </summary>
    public ConnectionLifetime Lifetime { get; set; } = ConnectionLifetime.Session;

    /// <summary>
    /// 传输选项
    /// </summary>
    public TransportOptions? Options { get; set; }

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 空闲超时时间
    /// </summary>
    public TimeSpan? IdleTimeout { get; set; }

    /// <summary>
    /// 连接标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan? ConnectTimeout { get; set; }

    /// <summary>
    /// 创建 TCP 连接配置
    /// </summary>
    public static ConnectionConfig Tcp(
        string name,
        string host,
        int port,
        ConnectionLifetime lifetime = ConnectionLifetime.Session,
        Dictionary<string, string>? tags = null)
    {
        return new ConnectionConfig
        {
            Name = name,
            Host = host,
            Port = port,
            Transport = TransportType.TCP,
            Lifetime = lifetime,
            Tags = tags ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 创建 KCP 连接配置
    /// </summary>
    public static ConnectionConfig Kcp(
        string name,
        string host,
        int port,
        ConnectionLifetime lifetime = ConnectionLifetime.Session,
        Dictionary<string, string>? tags = null)
    {
        return new ConnectionConfig
        {
            Name = name,
            Host = host,
            Port = port,
            Transport = TransportType.KCP,
            Lifetime = lifetime,
            Tags = tags ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 创建服务发现连接配置
    /// </summary>
    public static ConnectionConfig Service(
        string name,
        string serviceName,
        TransportType transport = TransportType.TCP,
        ConnectionLifetime lifetime = ConnectionLifetime.Session,
        Dictionary<string, string>? tags = null)
    {
        return new ConnectionConfig
        {
            Name = name,
            ServiceName = serviceName,
            Transport = transport,
            Lifetime = lifetime,
            Tags = tags ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 转换为 ConnectionDescriptor
    /// </summary>
    public ConnectionDescriptor ToDescriptor()
    {
        var descriptor = new ConnectionDescriptor
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Name,
            ServiceName = ServiceName,
            Transport = Transport,
            Strategy = Lifetime switch
            {
                ConnectionLifetime.Persistent => ConnectionStrategy.Persistent,
                ConnectionLifetime.Session => ConnectionStrategy.Session,
                ConnectionLifetime.Transient => ConnectionStrategy.Transient,
                _ => ConnectionStrategy.Session
            },
            AutoReconnect = AutoReconnect,
            Tags = new Dictionary<string, string>(Tags),
            TransportOptions = Options,
            ConnectTimeout = ConnectTimeout,
            IdleTimeout = IdleTimeout
        };

        // 如果指定了主机和端口，创建端点
        if (!string.IsNullOrEmpty(Host) && Port.HasValue)
        {
            descriptor.Endpoint = new EndpointAddress(Host, Port.Value);
        }

        return descriptor;
    }

    /// <summary>
    /// 验证配置的有效性
    /// </summary>
    public ConnectionValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("连接名称不能为空");

        // 必须指定服务名称或主机端口
        bool hasServiceName = !string.IsNullOrWhiteSpace(ServiceName);
        bool hasHostPort = !string.IsNullOrWhiteSpace(Host) && Port.HasValue;

        if (!hasServiceName && !hasHostPort)
            errors.Add("必须指定服务名称或主机端口");

        if (hasHostPort)
        {
            if (Port <= 0 || Port > 65535)
                errors.Add("端口号必须在 1-65535 范围内");
        }

        return new ConnectionValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(ServiceName))
            return $"{Name} -> {ServiceName} [{Transport}:{Lifetime}]";

        if (!string.IsNullOrEmpty(Host) && Port.HasValue)
            return $"{Name} -> {Host}:{Port} [{Transport}:{Lifetime}]";

        return $"{Name} [{Transport}:{Lifetime}]";
    }
}
