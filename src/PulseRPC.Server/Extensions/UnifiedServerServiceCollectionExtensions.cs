using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Core;
using PulseRPC.Server.Integration;
using PulseRPC.Server.Pipeline;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Engine;
using PulseRPC.Transport;
using PulseRPC.Server.Dispatch;
using PulseRPC.Server.Response;
using PulseRPC.Serialization;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// DI 容器扩展方法，用于注册 UnifiedPulseServer
/// </summary>
public static class UnifiedServerServiceCollectionExtensions
{
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

        // 注册内置传输提供程序 - 使用 AddSingleton 确保所有提供程序都被注册
        services.AddSingleton<ITransportProvider, TcpTransportProvider>();
        services.AddSingleton<ITransportProvider, KcpTransportProvider>();

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

        // 注册内置传输提供程序 - 使用 AddSingleton 确保所有提供程序都被注册
        services.AddSingleton<ITransportProvider, TcpTransportProvider>();
        services.AddSingleton<ITransportProvider, KcpTransportProvider>();

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
        services.TryAddSingleton<IMessageDispatcher, HighPerformanceMessageDispatcher>();
        services.TryAddSingleton<IResponseProcessor, HighPerformanceResponseProcessor>();
        
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
        services.TryAddSingleton<ITieredMessageEngine, HighPerformanceMessageEngine>();

        // 4. 注册 ServerChannelManager（依赖于上面的所有服务）
        services.TryAddSingleton<IServerChannelManager, ServerChannelManager>();

        // 5. 注册 EventPublisher（依赖于 IServerChannelManager 和 ISerializerProvider）
        services.TryAddSingleton<Events.IEventPublisher, Events.EventPublisher>();

        // 6. 注册 ResponseSerializerRegistry（由源代码生成器生成）
        if (ResponseSerializerRegistry.Instance != null)
        {
            services.TryAddSingleton(ResponseSerializerRegistry.Instance);
        }

        // 7. 注册 ServiceRoutingTable（由源代码生成器生成）
        // ServiceRoutingTableRegistry 在程序集加载时由 ModuleInitializer 自动注册
        if (Routing.ServiceRoutingTableRegistry.Instance != null)
        {
            services.TryAddSingleton<Routing.IServiceRoutingTable>(Routing.ServiceRoutingTableRegistry.Instance);
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

        // 注册内置传输提供程序 - 使用 AddSingleton 确保所有提供程序都被注册
        services.AddSingleton<ITransportProvider, TcpTransportProvider>();
        services.AddSingleton<ITransportProvider, KcpTransportProvider>();

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
