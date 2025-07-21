using Microsoft.Extensions.Logging;
using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// PulseRPC 客户端工厂 - 用于Unity等非DI环境
/// </summary>
public static class PulseRpcClientFactory
{
    /// <summary>
    /// 创建简单的TCP客户端
    /// </summary>
    /// <param name="host">服务器主机</param>
    /// <param name="port">服务器端口</param>
    /// <param name="loggerFactory">日志工厂（可选）</param>
    /// <returns>客户端实例</returns>
    public static IPulseRpcClient CreateTcpClient(string host, int port, ILoggerFactory? loggerFactory = null)
    {
        return CreateClient(builder =>
        {
            builder.AddTcp("Default", host, port, isDefault: true);
        }, loggerFactory);
    }

    /// <summary>
    /// 创建简单的KCP客户端
    /// </summary>
    /// <param name="host">服务器主机</param>
    /// <param name="port">服务器端口</param>
    /// <param name="loggerFactory">日志工厂（可选）</param>
    /// <returns>客户端实例</returns>
    public static IPulseRpcClient CreateKcpClient(string host, int port, ILoggerFactory? loggerFactory = null)
    {
        return CreateClient(builder =>
        {
            builder.AddKcp("Default", host, port, isDefault: true);
        }, loggerFactory);
    }

    /// <summary>
    /// 创建客户端实例
    /// </summary>
    /// <param name="configure">客户端配置</param>
    /// <param name="loggerFactory">日志工厂（可选）</param>
    /// <returns>客户端实例</returns>
    public static IPulseRpcClient CreateClient(
        Action<ClientConfigurationBuilder> configure, 
        ILoggerFactory? loggerFactory = null)
    {
        // 创建默认日志工厂
        loggerFactory ??= CreateDefaultLoggerFactory();

        // 创建通道管理器
        var channelManager = new ChannelManager(loggerFactory);

        // 创建配置构建器并构建
        var builder = new ClientConfigurationBuilder();
        configure(builder);
        var (transports, _) = builder.Build();

        // 创建客户端管理器
        var clientManager = new PulseRpcClientManager(channelManager, loggerFactory);

        // 添加传输配置
        clientManager.AddTransports(transports);

        return clientManager;
    }

    /// <summary>
    /// 创建带多传输的客户端
    /// </summary>
    /// <param name="transports">传输配置列表</param>
    /// <param name="loggerFactory">日志工厂（可选）</param>
    /// <returns>客户端实例</returns>
    public static IPulseRpcClient CreateClient(
        IEnumerable<ClientTransportConfiguration> transports, 
        ILoggerFactory? loggerFactory = null)
    {
        // 创建默认日志工厂
        loggerFactory ??= CreateDefaultLoggerFactory();

        // 创建通道管理器
        var channelManager = new ChannelManager(loggerFactory);

        // 创建客户端管理器
        var clientManager = new PulseRpcClientManager(channelManager, loggerFactory);

        // 添加传输配置
        clientManager.AddTransports(transports);

        return clientManager;
    }

    /// <summary>
    /// 创建客户端构建器
    /// </summary>
    /// <returns>客户端构建器实例</returns>
    public static ClientBuilder CreateBuilder()
    {
        return new ClientBuilder();
    }
}

/// <summary>
/// 客户端构建器 - 用于流式配置
/// </summary>
public class ClientBuilder
{
    private readonly List<ClientTransportConfiguration> _transports = new();
    private ILoggerFactory? _loggerFactory;
    private ClientOptions? _clientOptions;

    /// <summary>
    /// 设置日志工厂
    /// </summary>
    public ClientBuilder WithLogger(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// 配置客户端选项
    /// </summary>
    public ClientBuilder WithOptions(Action<ClientOptions> configure)
    {
        _clientOptions = new ClientOptions();
        configure(_clientOptions);
        return this;
    }

    /// <summary>
    /// 添加TCP传输
    /// </summary>
    public ClientBuilder AddTcp(string name, string host, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new TransportOptions();
        configureOptions?.Invoke(options);
        
        _transports.Add(ClientTransportConfiguration.Tcp(name, host, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加KCP传输
    /// </summary>
    public ClientBuilder AddKcp(string name, string host, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new TransportOptions();
        configureOptions?.Invoke(options);
        
        _transports.Add(ClientTransportConfiguration.Kcp(name, host, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加WebSocket传输
    /// </summary>
    public ClientBuilder AddWebSocket(string name, string host, int port, Action<TransportOptions>? configureOptions = null, bool isDefault = false)
    {
        var options = new TransportOptions();
        configureOptions?.Invoke(options);
        
        _transports.Add(ClientTransportConfiguration.WebSocket(name, host, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加自定义传输
    /// </summary>
    public ClientBuilder AddTransport(ClientTransportConfiguration transport)
    {
        _transports.Add(transport);
        return this;
    }

    /// <summary>
    /// 构建客户端实例
    /// </summary>
    public IPulseRpcClient Build()
    {
        return PulseRpcClientFactory.CreateClient(_transports, _loggerFactory);
    }

    /// <summary>
    /// 创建默认日志工厂 - 条件编译以支持不同环境
    /// </summary>
    private static ILoggerFactory CreateDefaultLoggerFactory()
    {
#if NET9_0_OR_GREATER
        return Microsoft.Extensions.Logging.LoggerFactory.Create(builder => 
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information);
        });
#else
        // 对于netstandard2.1（Unity等环境），使用NullLoggerFactory
        return Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
#endif
    }
}

