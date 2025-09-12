using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// 传输通道配置
/// </summary>
public class TransportChannelConfiguration
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
    /// 监听端口
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
    public static TransportChannelConfiguration Tcp(string name, int port, TransportOptions? options = null, bool isDefault = false) =>
        new()
        {
            Name = name,
            Type = TransportType.Tcp,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };

    /// <summary>
    /// 创建KCP传输配置
    /// </summary>
    public static TransportChannelConfiguration Kcp(string name, int port, TransportOptions? options = null, bool isDefault = false) =>
        new()
        {
            Name = name,
            Type = TransportType.Kcp,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };

    /// <summary>
    /// 创建WebSocket传输配置
    /// </summary>
    public static TransportChannelConfiguration WebSocket(string name, int port, TransportOptions? options = null, bool isDefault = false) =>
        new()
        {
            Name = name,
            Type = TransportType.WebSocket,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };
}

/// <summary>
/// 服务器配置构建器
/// </summary>
public class ServerConfigurationBuilder
{
    private readonly List<TransportChannelConfiguration> _transports = new();
    private Action<ServerOptions>? _serverOptionsConfig;

    /// <summary>
    /// 配置服务器选项
    /// </summary>
    public ServerConfigurationBuilder ConfigureServer(Action<ServerOptions> configure)
    {
        _serverOptionsConfig = configure;
        return this;
    }

    /// <summary>
    /// 添加TCP传输
    /// </summary>
    public ServerConfigurationBuilder AddTcp(string name, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new TcpTransportOptions();
        configureOptions?.Invoke(options);

        _transports.Add(TransportChannelConfiguration.Tcp(name, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加KCP传输
    /// </summary>
    public ServerConfigurationBuilder AddKcp(string name, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new KcpTransportOptions();
        configureOptions?.Invoke(options);

        _transports.Add(TransportChannelConfiguration.Kcp(name, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加传输通道
    /// </summary>
    public ServerConfigurationBuilder AddTransport(TransportChannelConfiguration transport)
    {
        _transports.Add(transport);
        return this;
    }

    /// <summary>
    /// 构建配置
    /// </summary>
    internal (List<TransportChannelConfiguration> Transports, Action<ServerOptions>? ServerConfig) Build() =>
        (_transports, _serverOptionsConfig);
}
