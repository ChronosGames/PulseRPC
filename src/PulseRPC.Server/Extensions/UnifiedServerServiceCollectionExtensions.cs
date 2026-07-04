using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Server.Transport;
using PulseRPC.Scheduling;
using PulseRPC.Serialization;
using PulseRPC.Shared;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// DI 容器扩展方法，用于注册 UnifiedPulseServer
/// </summary>
public static class UnifiedServerServiceCollectionExtensions
{
    /// <summary>
    /// 使用默认配置添加 PulseRPC 服务器（最简配置）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="port">TCP 监听端口</param>
    /// <returns>服务集合</returns>
    /// <example>
    /// <code>
    /// services.AddPulseServer(5000);
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseServer(
        this IServiceCollection services,
        int port)
    {
        return services.AddUnifiedPulseServer(options => options
            .UsePreset(ServerPreset.Default)
            .AddTcp(port));
    }

    /// <summary>
    /// 使用指定预设添加 PulseRPC 服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="preset">服务器预设</param>
    /// <param name="port">TCP 监听端口</param>
    /// <returns>服务集合</returns>
    /// <example>
    /// <code>
    /// services.AddPulseServer(ServerPreset.HighThroughput, 5000);
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseServer(
        this IServiceCollection services,
        ServerPreset preset,
        int port)
    {
        return services.AddUnifiedPulseServer(options => options
            .UsePreset(preset)
            .AddTcp(port));
    }

    /// <summary>
    /// 使用自定义配置添加 PulseRPC 服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置委托</param>
    /// <returns>服务集合</returns>
    /// <example>
    /// <code>
    /// services.AddPulseServer(options => options
    ///     .UsePreset(ServerPreset.LowLatency)
    ///     .AddTcp(5000)
    ///     .AddKcp(5001));
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseServer(
        this IServiceCollection services,
        Action<UnifiedServerOptions> configure)
    {
        return services.AddUnifiedPulseServer(configure);
    }

    /// <summary>
    /// 添加 UnifiedPulseServer 到 DI 容器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项的委托</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddUnifiedPulseServer(
        this IServiceCollection services,
        Action<UnifiedServerOptions> configureOptions)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        // 配置选项
        services.Configure(configureOptions);

        // 注册核心依赖（如果尚未注册）
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序 - 重要：使用 AddSingleton 而非 TryAddSingleton，以注册多个 ITransportProvider 实例
        services.AddSingleton<ITransportProvider>(new TcpTransportProvider());
        services.AddSingleton<ITransportProvider>(new KcpTransportProvider());

        // 注册内部依赖
        RegisterInternalDependencies(services);

        // 注册 UnifiedPulseServer 为单例（使用工厂方法）
        services.AddSingleton<UnifiedPulseServer>();

        // 注册为 IPulseServer 接口
        services.AddSingleton<IPulseServer>(sp => sp.GetRequiredService<UnifiedPulseServer>());

        // 注册为托管服务，自动启动/停止
        services.AddHostedService<UnifiedPulseServerHostedService>();

        return services;
    }

    /// <summary>
    /// 添加 UnifiedPulseServer 到 DI 容器（使用 IConfiguration）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置节</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddUnifiedPulseServer(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 从配置绑定选项
        services.Configure<UnifiedServerOptions>(configuration);

        // 注册核心依赖
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序 - 重要：使用 AddSingleton 而非 TryAddSingleton，以注册多个 ITransportProvider 实例
        services.AddSingleton<ITransportProvider>(new TcpTransportProvider());
        services.AddSingleton<ITransportProvider>(new KcpTransportProvider());

        // 注册内部依赖
        RegisterInternalDependencies(services);

        // 注册 UnifiedPulseServer
        services.AddSingleton<UnifiedPulseServer>();
        services.AddSingleton<IPulseServer>(sp => sp.GetRequiredService<UnifiedPulseServer>());

        // 注册托管服务
        services.AddHostedService<UnifiedPulseServerHostedService>();

        return services;
    }

    /// <summary>
    /// 注册内部依赖（ServerChannelManager等）
    /// </summary>
    private static void RegisterInternalDependencies(IServiceCollection services)
    {
        // 1. 首先注册基础依赖（不依赖其他服务）
        services.TryAddSingleton<IMessageDispatcher, MessageDispatcher>();
        services.TryAddSingleton<IResponseProcessor, ResponseProcessor>();

        // 1.5. 注册序列化提供程序（EventPublisher 的依赖）
        services.TryAddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

        // 2. 配置选项
        services.TryAddSingleton(Options.Create(new MessageEngineConfiguration()));
        services.TryAddSingleton(Options.Create(new TieredEngineManagerOptions
        {
            MaxConnections = 10000,
            DefaultL1BufferSize = 4096,
            DefaultL2QueueCapacity = 256,
            DefaultL3QueueCapacity = 128,
            DefaultMaxBatchSize = 128,
            EnableDetailedLogging = false
        }));

        // 3. 注册依赖于 IMessageDispatcher 的 TieredMessageEngineManager
        services.TryAddSingleton<ITieredMessageEngine, MessageEngine>();

        // 4. 注册 ServerChannelManager（依赖于上面的所有服务）
        services.TryAddSingleton<IServerChannelManager, ServerChannelManager>();

        // 6. 注册 ResponseSerializerRegistry（由源代码生成器生成）
        if (ResponseSerializerRegistry.Instance != null)
        {
            services.TryAddSingleton(ResponseSerializerRegistry.Instance);
        }

        // 7. 注册 ServiceRoutingTable（由源代码生成器生成）
        // ServiceRoutingTableRegistry 在程序集加载时由 ModuleInitializer 自动注册
        if (ServiceRoutingTableRegistry.Instance != null)
        {
            services.TryAddSingleton<IServiceRoutingTable>(ServiceRoutingTableRegistry.Instance);
        }
    }

    /// <summary>
    /// 添加 UnifiedPulseServer 构建器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>UnifiedPulseServer 构建器</returns>
    public static IUnifiedPulseServerBuilder AddUnifiedPulseServerBuilder(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册核心依赖
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序 - 重要：使用 AddSingleton 而非 TryAddSingleton，以注册多个 ITransportProvider 实例
        services.AddSingleton<ITransportProvider>(new TcpTransportProvider());
        services.AddSingleton<ITransportProvider>(new KcpTransportProvider());

        return new UnifiedPulseServerBuilder(services);
    }
}

/// <summary>
/// UnifiedPulseServer 构建器接口
/// </summary>
public interface IUnifiedPulseServerBuilder
{
    /// <summary>
    /// 服务集合
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// 添加 TCP 传输
    /// </summary>
    IUnifiedPulseServerBuilder AddTcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null);

    /// <summary>
    /// 添加 KCP 传输
    /// </summary>
    IUnifiedPulseServerBuilder AddKcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null);

    /// <summary>
    /// 配置服务器选项
    /// </summary>
    IUnifiedPulseServerBuilder ConfigureOptions(Action<UnifiedServerOptions> configure);

    /// <summary>
    /// 构建并注册服务器
    /// </summary>
    IServiceCollection Build();
}

/// <summary>
/// UnifiedPulseServer 构建器实现
/// </summary>
internal class UnifiedPulseServerBuilder : IUnifiedPulseServerBuilder
{
    private readonly List<TransportChannelConfiguration> _transports = new();
    private Action<UnifiedServerOptions>? _optionsConfigurator;

    public IServiceCollection Services { get; }

    public UnifiedPulseServerBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IUnifiedPulseServerBuilder AddTcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null)
    {
        var options = new TcpTransportOptions();
        configure?.Invoke(options);

        _transports.Add(TransportChannelConfiguration.Tcp(name, port, options, isDefault));
        return this;
    }

    public IUnifiedPulseServerBuilder AddKcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null)
    {
        var options = new KcpTransportOptions();
        configure?.Invoke(options);

        _transports.Add(TransportChannelConfiguration.Kcp(name, port, options, isDefault));
        return this;
    }

    public IUnifiedPulseServerBuilder ConfigureOptions(Action<UnifiedServerOptions> configure)
    {
        _optionsConfigurator = configure;
        return this;
    }

    public IServiceCollection Build()
    {
        Services.AddUnifiedPulseServer(options =>
        {
            // 应用用户自定义配置
            _optionsConfigurator?.Invoke(options);
        });

        // 配置选项
        // Services.Configure<UnifiedServerOptions>(options =>
        // {
        //     // 添加传输配置
        //     options.Transports.AddRange(_transports);
        //
        //     // 应用用户自定义配置
        //     _optionsConfigurator?.Invoke(options);
        // });
        //
        // // 注册 UnifiedPulseServer
        // Services.AddSingleton<UnifiedPulseServer>();
        // Services.AddSingleton<IPulseServer>(sp => sp.GetRequiredService<UnifiedPulseServer>());
        //
        // // 注册托管服务
        // Services.AddHostedService<UnifiedPulseServerHostedService>();

        return Services;
    }
}

/// <summary>
/// 命名服务器扩展方法（方案1实现）
/// </summary>
public static class NamedPulseServerServiceCollectionExtensions
{
    /// <summary>
    /// 添加命名的 PulseRPC 服务器（支持多实例）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="serverName">服务器名称（唯一标识）</param>
    /// <param name="configureOptions">配置选项的委托</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddNamedPulseServer(
        this IServiceCollection services,
        string serverName,
        Action<UnifiedServerOptions> configureOptions)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(serverName)) throw new ArgumentException("Server name cannot be null or whitespace", nameof(serverName));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        // 使用命名方式注册配置
        services.Configure<UnifiedServerOptions>(serverName, configureOptions);

        // 注册核心依赖（如果尚未注册）- 这些依赖可以被多个服务器实例共享
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序（共享）- 重要：使用 AddSingleton 而非 TryAddSingleton，以注册多个 ITransportProvider 实例
        services.AddSingleton<ITransportProvider>(new TcpTransportProvider());
        services.AddSingleton<ITransportProvider>(new KcpTransportProvider());

        // 注册序列化器提供程序（共享）
        services.TryAddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

        // 注册响应序列化器注册表（如果存在）
        if (ResponseSerializerRegistry.Instance != null)
        {
            services.TryAddSingleton(ResponseSerializerRegistry.Instance);
        }

        // 为每个命名服务器注册独立的依赖（使用 Keyed Services）
        // 注意：依赖创建顺序很重要

        // 1. 通道管理器（每个服务器独立）
        services.AddKeyedSingleton<IServerChannelManager>(serverName, (sp, key) =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ServerChannelManager>();
            return new ServerChannelManager(logger, loggerFactory);
        });

        // 2. 消息分发器（每个服务器独立）
        services.AddKeyedSingleton<IMessageDispatcher>(serverName, (sp, key) =>
            new MessageDispatcher());

        // 3. 响应处理器（每个服务器独立）
        services.AddKeyedSingleton<IResponseProcessor>(serverName, (sp, key) =>
        {
            var channelManager = sp.GetRequiredKeyedService<IServerChannelManager>(key);
            var serializerProvider = sp.GetService<ISerializerProvider>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ResponseProcessor>();
            var responseSerializerRegistry = sp.GetService<IResponseSerializerRegistry>();

            return new ResponseProcessor(
                channelManager,
                serializerProvider,
                null, // options
                logger,
                responseSerializerRegistry);
        });

        // 4. 消息引擎（每个服务器独立）
        services.AddKeyedSingleton<ITieredMessageEngine>(serverName, (sp, key) =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<MessageEngine>();
            var dispatcher = sp.GetRequiredKeyedService<IMessageDispatcher>(key);
            var channelManager = sp.GetRequiredKeyedService<IServerChannelManager>(key);
            var responseProcessor = sp.GetRequiredKeyedService<IResponseProcessor>(key);
            var scheduler = sp.GetService<IServiceScheduler>();

            var engineOptions = Options.Create(new MessageEngineConfiguration());

            return new MessageEngine(
                dispatcher,
                sp, // IServiceProvider
                engineOptions,
                logger,
                channelManager,
                responseProcessor,
                scheduler);
        });

        // 5. 注册命名服务器（使用 Keyed Service）
        services.AddKeyedSingleton<INamedPulseServer>(
            serverName,
            (sp, key) =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<UnifiedServerOptions>>()
                    .Get(serverName);

                var messageEngine = sp.GetRequiredKeyedService<ITieredMessageEngine>(key);
                var channelManager = sp.GetRequiredKeyedService<IServerChannelManager>(key);
                var transportIntegrationManager = sp.GetRequiredService<ITransportIntegrationManager>();
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                return new NamedUnifiedPulseServer(
                    serverName,
                    messageEngine,
                    channelManager,
                    transportIntegrationManager,
                    loggerFactory,
                    Options.Create(options));
            });

        return services;
    }

    /// <summary>
    /// 添加命名的 PulseRPC 服务器（使用 IConfiguration）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="serverName">服务器名称（唯一标识）</param>
    /// <param name="configuration">配置节</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddNamedPulseServer(
        this IServiceCollection services,
        string serverName,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        return services.AddNamedPulseServer(serverName, configuration.Bind);
    }
}

/// <summary>
/// UnifiedPulseServer 托管服务包装器
/// 自动在应用启动时启动服务器，在应用停止时停止服务器
/// </summary>
internal sealed class UnifiedPulseServerHostedService : IHostedService
{
    private readonly UnifiedPulseServer _server;
    private readonly ILogger<UnifiedPulseServerHostedService> _logger;

    public UnifiedPulseServerHostedService(
        UnifiedPulseServer server,
        ILogger<UnifiedPulseServerHostedService> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting UnifiedPulseServer via hosted service");

        try
        {
            await _server.StartAsync(cancellationToken);
            _logger.LogInformation("UnifiedPulseServer started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start UnifiedPulseServer");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping UnifiedPulseServer via hosted service");

        try
        {
            await _server.StopAsync(cancellationToken);
            _logger.LogInformation("UnifiedPulseServer stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping UnifiedPulseServer");
            throw;
        }
    }
}
