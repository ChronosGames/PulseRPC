using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;
using PulseRPC.Serialization;
using PulseRPC.Shared;

namespace PulseRPC.Server;

/// <summary>
/// PulseRPC 服务器工厂类 - 提供简化的服务器创建 API
/// </summary>
/// <example>
/// 最简配置：
/// <code>
/// var server = PulseServer.CreateDefault(5000);
/// await server.StartAsync();
/// </code>
///
/// 使用预设：
/// <code>
/// var server = PulseServer.Create(options => options
///     .UsePreset(ServerPreset.HighThroughput)
///     .AddTcp(5000));
/// </code>
///
/// 完整配置：
/// <code>
/// var server = PulseServer.Create(options => options
///     .UsePreset(ServerPreset.LowLatency)
///     .AddTcp(5000)
///     .AddKcp(5001));
/// </code>
/// </example>
public static class PulseServerFactory
{
    /// <summary>
    /// 使用默认配置创建服务器
    /// </summary>
    /// <param name="port">TCP 监听端口</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer CreateDefault(int port)
    {
        return Create(options => options
            .UsePreset(ServerPreset.Default)
            .AddTcp(port));
    }

    /// <summary>
    /// 使用默认配置创建服务器（带日志工厂）
    /// </summary>
    /// <param name="port">TCP 监听端口</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer CreateDefault(int port, ILoggerFactory loggerFactory)
    {
        return Create(
            options => options
                .UsePreset(ServerPreset.Default)
                .AddTcp(port),
            loggerFactory);
    }

    /// <summary>
    /// 使用指定预设创建服务器
    /// </summary>
    /// <param name="preset">服务器预设</param>
    /// <param name="port">TCP 监听端口</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(ServerPreset preset, int port)
    {
        return Create(options => options
            .UsePreset(preset)
            .AddTcp(port));
    }

    /// <summary>
    /// 使用指定预设创建服务器（带日志工厂）
    /// </summary>
    /// <param name="preset">服务器预设</param>
    /// <param name="port">TCP 监听端口</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(ServerPreset preset, int port, ILoggerFactory loggerFactory)
    {
        return Create(
            options => options
                .UsePreset(preset)
                .AddTcp(port),
            loggerFactory);
    }

    /// <summary>
    /// 使用自定义配置创建服务器
    /// </summary>
    /// <param name="configure">配置委托</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(Action<UnifiedServerOptions> configure)
    {
        return Create(configure, null);
    }

    /// <summary>
    /// 使用自定义配置创建服务器（带日志工厂）
    /// </summary>
    /// <param name="configure">配置委托</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(Action<UnifiedServerOptions> configure, ILoggerFactory? loggerFactory)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        // 创建并配置选项
        var options = new UnifiedServerOptions();
        configure(options);

        // 验证配置
        options.Validate();

        // 使用日志工厂或默认空日志
        var factory = loggerFactory ?? NullLoggerFactory.Instance;

        // 直接创建依赖并构建服务器
        return CreateServerWithDependencies(options, factory);
    }

    /// <summary>
    /// 使用依赖创建服务器实例
    /// </summary>
    private static UnifiedPulseServer CreateServerWithDependencies(
        UnifiedServerOptions options,
        ILoggerFactory loggerFactory)
    {
        // 创建简单的服务提供者用于消息引擎
        var services = new ServiceCollection();
        var serviceProvider = new MinimalServiceProvider();

        // 创建传输集成管理器
        var transportProviders = new ITransportProvider[]
        {
            new TcpTransportProvider(),
            new KcpTransportProvider()
        };
        var transportManagerLogger = loggerFactory.CreateLogger<TransportIntegrationManager>();
        var transportIntegrationManager = new TransportIntegrationManager(transportProviders, transportManagerLogger);

        // 创建消息分发器
        var dispatcher = new MessageDispatcher();

        // 创建通道管理器
        var channelManagerLogger = loggerFactory.CreateLogger<ServerChannelManager>();
        var channelManager = new ServerChannelManager(channelManagerLogger, loggerFactory);

        // 创建响应处理器
        var responseProcessorLogger = loggerFactory.CreateLogger<ResponseProcessor>();
        var responseProcessor = new ResponseProcessor(
            channelManager,
            PulseRPCSerializerProvider.Instance,
            null,
            responseProcessorLogger,
            ResponseSerializerRegistry.Instance);

        // 创建消息引擎配置
        var engineConfig = Options.Create(new MessageEngineConfiguration());

        // 创建消息引擎
        var engineLogger = loggerFactory.CreateLogger<MessageEngine>();
        var messageEngine = new MessageEngine(
            dispatcher,
            serviceProvider,
            engineConfig,
            engineLogger,
            channelManager,
            responseProcessor,
            null); // IServiceScheduler - 可选

        // 创建服务器实例
        return new UnifiedPulseServer(
            messageEngine,
            channelManager,
            transportIntegrationManager,
            loggerFactory,
            Options.Create(options));
    }
}

/// <summary>
/// 最小服务提供者实现 - 用于工厂模式下的简单场景
/// </summary>
internal sealed class MinimalServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        // 工厂模式下返回 null，大多数场景下不需要 DI
        return null;
    }
}
