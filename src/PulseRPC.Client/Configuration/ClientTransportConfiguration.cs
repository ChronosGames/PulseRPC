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
    public static ClientTransportConfiguration Tcp(string name, string host, int port, TransportOptions? options = null, bool isDefault = false) =>
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
    public static ClientTransportConfiguration Kcp(string name, string host, int port, TransportOptions? options = null, bool isDefault = false) =>
        new()
        {
            Name = name,
            Type = TransportType.Kcp,
            Host = host,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };

    /// <summary>
    /// 创建WebSocket传输配置
    /// </summary>
    public static ClientTransportConfiguration WebSocket(string name, string host, int port, TransportOptions? options = null, bool isDefault = false) =>
        new()
        {
            Name = name,
            Type = TransportType.WebSocket,
            Host = host,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };
}

/// <summary>
/// 客户端配置构建器
/// </summary>
public class ClientConfigurationBuilder
{
    private readonly List<ClientTransportConfiguration> _transports = new();
    private Action<ClientOptions>? _clientOptionsConfig;

    /// <summary>
    /// 配置客户端选项
    /// </summary>
    public ClientConfigurationBuilder ConfigureClient(Action<ClientOptions> configure)
    {
        _clientOptionsConfig = configure;
        return this;
    }

    /// <summary>
    /// 添加TCP传输
    /// </summary>
    public ClientConfigurationBuilder AddTcp(string name, string host, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new TransportOptions();
        configureOptions?.Invoke(options);
        
        _transports.Add(ClientTransportConfiguration.Tcp(name, host, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加KCP传输
    /// </summary>
    public ClientConfigurationBuilder AddKcp(string name, string host, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new TransportOptions();
        configureOptions?.Invoke(options);
        
        _transports.Add(ClientTransportConfiguration.Kcp(name, host, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加WebSocket传输
    /// </summary>
    public ClientConfigurationBuilder AddWebSocket(string name, string host, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new TransportOptions();
        configureOptions?.Invoke(options);
        
        _transports.Add(ClientTransportConfiguration.WebSocket(name, host, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加传输通道
    /// </summary>
    public ClientConfigurationBuilder AddTransport(ClientTransportConfiguration transport)
    {
        _transports.Add(transport);
        return this;
    }

    /// <summary>
    /// 构建配置
    /// </summary>
    internal (List<ClientTransportConfiguration> Transports, Action<ClientOptions>? ClientConfig) Build() =>
        (_transports, _clientOptionsConfig);
}