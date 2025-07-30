using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// PulseRPC 客户端静态工厂类
/// </summary>
public static class PulseRpcClientFactory
{
    /// <summary>
    /// 创建客户端
    /// </summary>
    /// <param name="configure">客户端配置委托</param>
    /// <returns>配置好的客户端实例</returns>
    public static IPulseClient CreateClient(Action<IPulseClientBuilder> configure)
    {
        // 创建简单的服务提供者
        var loggerFactory = new NullLoggerFactory();
        var channelManager = new ChannelManager(loggerFactory);

        var serviceProvider = new SimpleServiceProvider(loggerFactory, channelManager);

        // 创建构建器并配置
        var builder = new PulseClientBuilder(serviceProvider);
        configure(builder);

        // 构建并返回客户端
        return builder.Build();
    }

    /// <summary>
    /// 创建简单的 TCP 客户端
    /// </summary>
    /// <param name="name">通道名称</param>
    /// <param name="host">服务器主机</param>
    /// <param name="port">服务器端口</param>
    /// <returns>配置好的 TCP 客户端</returns>
    public static IPulseClient CreateTcpClient(string name, string host, int port)
    {
        return CreateClient(builder => builder.AddTcp(name, host, port));
    }

    /// <summary>
    /// 创建简单的 KCP 客户端
    /// </summary>
    /// <param name="name">通道名称</param>
    /// <param name="host">服务器主机</param>
    /// <param name="port">服务器端口</param>
    /// <returns>配置好的 KCP 客户端</returns>
    public static IPulseClient CreateKcpClient(string name, string host, int port)
    {
        return CreateClient(builder => builder.AddKcp(name, host, port));
    }
}

/// <summary>
/// 简单的服务提供者实现
/// </summary>
internal class SimpleServiceProvider : IServiceProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IChannelManager _channelManager;

    public SimpleServiceProvider(ILoggerFactory loggerFactory, IChannelManager channelManager)
    {
        _loggerFactory = loggerFactory;
        _channelManager = channelManager;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(ILoggerFactory))
            return _loggerFactory;
        if (serviceType == typeof(IChannelManager))
            return _channelManager;
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var loggerType = serviceType.GetGenericArguments()[0];
            return _loggerFactory.CreateLogger(loggerType);
        }
        return null;
    }
}
