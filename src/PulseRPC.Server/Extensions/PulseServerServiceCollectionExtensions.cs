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
using PulseRPC.Server.Security;
using PulseRPC.Scheduling;
using PulseRPC.Serialization;
using PulseRPC.Shared;
using PulseRPC.Routing;
using PulseRPC.Server.Routing;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// DI 容器扩展方法，用于注册 PulseServer
/// </summary>
public static class PulseServerServiceCollectionExtensions
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
        return services.AddPulseServer(options => options.AddTcp(port));
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
    [Obsolete("Server presets are not connected to runtime behavior. Use AddPulseServer(options => ...) instead.", false)]
    public static IServiceCollection AddPulseServer(
        this IServiceCollection services,
        ServerPreset preset,
        int port)
    {
        return services.AddPulseServer(options => options
            .UsePreset(preset)
            .AddTcp(port));
    }

    /// <summary>
    /// 使用自定义配置添加 PulseRPC 服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置委托</param>
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
        Action<PulseServerOptions> configureOptions)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        // 配置选项
        services.Configure(configureOptions);

        // 注册核心依赖（如果尚未注册）
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序 - 重要：使用 AddSingleton 而非 TryAddSingleton，以注册多个 ITransportProvider 实例
        RegisterBuiltInTransportProviders(services);

        // 注册内部依赖
        RegisterInternalDependencies(services);

        // Public facade over the one runtime composition root.
        services.TryAddSingleton<PulseServer>(sp =>
            new PulseServer(sp.GetRequiredService<ServerRuntime>()));

        // 注册为 IPulseServer 接口
        services.TryAddSingleton<IPulseServer>(sp => sp.GetRequiredService<PulseServer>());

        // 注册为托管服务，自动启动/停止
        services.AddHostedService<PulseServerHostedService>();

        return services;
    }

    /// <summary>
    /// 添加 PulseServer 到 DI 容器（使用 IConfiguration）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置节</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseServer(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 从配置绑定选项
        services.Configure<PulseServerOptions>(configuration);

        // 注册核心依赖
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序 - 重要：使用 AddSingleton 而非 TryAddSingleton，以注册多个 ITransportProvider 实例
        RegisterBuiltInTransportProviders(services);

        // 注册内部依赖
        RegisterInternalDependencies(services);

        // Public facade over the one runtime composition root.
        services.TryAddSingleton<PulseServer>(sp =>
            new PulseServer(sp.GetRequiredService<ServerRuntime>()));
        services.TryAddSingleton<IPulseServer>(sp => sp.GetRequiredService<PulseServer>());

        // 注册托管服务
        services.AddHostedService<PulseServerHostedService>();

        return services;
    }

    /// <summary>
    /// 注册内部依赖（ServerChannelManager等）
    /// </summary>
    private static void RegisterInternalDependencies(IServiceCollection services)
    {
        services.TryAddSingleton<IClientFacingGatePolicy>(sp =>
            new ClientFacingGatePolicy(
                sp.GetRequiredService<IOptions<PulseServerOptions>>()
                    .Value.EnableClientFacingGate));

        // 1. 注册序列化提供程序（EventPublisher 的依赖）
        services.TryAddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

        // 2. standard 与 named 都通过同一个 component factory 创建 runtime 图。
        services.TryAddSingleton<IServerChannelManager>(sp =>
            ServerRuntimeComponentFactory.CreateChannelRegistry(
                sp.GetRequiredService<ILoggerFactory>()));
        services.TryAddSingleton<IMessageDispatcher>(sp =>
            ServerRuntimeComponentFactory.CreateDispatcher(
                sp.GetRequiredService<IServiceRoutingTable>(),
                sp.GetRequiredService<ILoggerFactory>()));
        services.TryAddSingleton<IResponseProcessor>(sp =>
            ServerRuntimeComponentFactory.CreateResponseProcessor(
                sp.GetRequiredService<IServerChannelManager>(),
                sp.GetService<ISerializerProvider>(),
                sp.GetService<IResponseSerializerRegistry>(),
                sp.GetService<IServiceRoutingTable>(),
                sp.GetRequiredService<ILoggerFactory>()));
        services.TryAddSingleton<ITieredMessageEngine>(sp =>
            ServerRuntimeComponentFactory.CreateMessageEngine(
                sp.GetRequiredService<IMessageDispatcher>(),
                sp,
                sp.GetRequiredService<IOptions<PulseServerOptions>>().Value,
                sp.GetRequiredService<IServerChannelManager>(),
                sp.GetRequiredService<IResponseProcessor>(),
                sp.GetRequiredService<ILoggerFactory>()));

        // 5. 注册统一路由基础设施（IPulseRouter/IPulseBackplane，依赖于上面的 ServerChannelManager）
        services.AddPulseRouting();

        // 6. 延迟解析 Source Generator 注册表，允许宿主程序集在 AddPulseServer 之后加载。
        services.TryAddSingleton<IResponseSerializerRegistry>(_ =>
            ResponseSerializerRegistry.Instance
            ?? throw new InvalidOperationException("IResponseSerializerRegistry 未注册。"));

        // 7. 注册 ServiceRoutingTable（由源代码生成器生成）
        // ServiceRoutingTableRegistry 在程序集加载时由 ModuleInitializer 自动注册
        services.TryAddSingleton<IServiceRoutingTable>(_ =>
            ServiceRoutingTableRegistry.Instance
            ?? throw new InvalidOperationException("IServiceRoutingTable 未注册。"));

        services.TryAddSingleton<IServiceManifest>(_ =>
            ServiceManifestRegistry.Instance
            ?? throw new InvalidOperationException("IServiceManifest 未注册。"));
        services.TryAddSingleton<ServerRuntime>(sp =>
            ServerRuntimeComponentFactory.CreateRuntime(
                sp.GetRequiredService<ITieredMessageEngine>(),
                sp.GetRequiredService<IServerChannelManager>(),
                sp.GetRequiredService<ITransportIntegrationManager>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IOptions<PulseServerOptions>>()));
    }

    internal static void RegisterBuiltInTransportProviders(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransportProvider, TcpTransportProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransportProvider, KcpTransportProvider>());
    }

    /// <summary>
    /// 添加 PulseServer 构建器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>PulseServer 构建器</returns>
    public static IPulseServerBuilder AddPulseServerBuilder(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册核心依赖
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序 - 重要：使用 AddSingleton 而非 TryAddSingleton，以注册多个 ITransportProvider 实例
        PulseServerServiceCollectionExtensions.RegisterBuiltInTransportProviders(services);

        return new PulseServerBuilder(services);
    }
}

/// <summary>
/// PulseServer 构建器接口
/// </summary>
public interface IPulseServerBuilder
{
    /// <summary>
    /// 服务集合
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// 添加 TCP 传输
    /// </summary>
    IPulseServerBuilder AddTcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null);

    /// <summary>
    /// 添加 KCP 传输
    /// </summary>
    IPulseServerBuilder AddKcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null);

    /// <summary>
    /// 配置服务器选项
    /// </summary>
    IPulseServerBuilder ConfigureOptions(Action<PulseServerOptions> configure);

    /// <summary>
    /// 构建并注册服务器
    /// </summary>
    IServiceCollection Build();
}

/// <summary>
/// PulseServer 构建器实现
/// </summary>
internal class PulseServerBuilder : IPulseServerBuilder
{
    private readonly List<TransportChannelConfiguration> _transports = new();
    private Action<PulseServerOptions>? _optionsConfigurator;

    public IServiceCollection Services { get; }

    public PulseServerBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IPulseServerBuilder AddTcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null)
    {
        var options = new TcpTransportOptions();
        configure?.Invoke(options);

        _transports.Add(TransportChannelConfiguration.Tcp(name, port, options, isDefault));
        return this;
    }

    public IPulseServerBuilder AddKcpTransport(string name, int port, bool isDefault = false, Action<TransportOptions>? configure = null)
    {
        var options = new KcpTransportOptions();
        configure?.Invoke(options);

        _transports.Add(TransportChannelConfiguration.Kcp(name, port, options, isDefault));
        return this;
    }

    public IPulseServerBuilder ConfigureOptions(Action<PulseServerOptions> configure)
    {
        _optionsConfigurator = configure;
        return this;
    }

    public IServiceCollection Build()
    {
        Services.AddPulseServer(options =>
        {
            options.Transports.AddRange(_transports);

            // 应用用户自定义配置
            _optionsConfigurator?.Invoke(options);
        });

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
        Action<PulseServerOptions> configureOptions)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(serverName)) throw new ArgumentException("Server name cannot be null or whitespace", nameof(serverName));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        // 使用命名方式注册配置
        services.Configure<PulseServerOptions>(serverName, configureOptions);

        // 注册核心依赖（如果尚未注册）- 这些依赖可以被多个服务器实例共享
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 内置 provider 跨 standard/named 注册保持每种类型唯一。
        PulseServerServiceCollectionExtensions.RegisterBuiltInTransportProviders(services);

        // 注册序列化器提供程序（共享）
        services.TryAddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

        services.TryAddSingleton<IResponseSerializerRegistry>(_ =>
            ResponseSerializerRegistry.Instance
            ?? throw new InvalidOperationException("IResponseSerializerRegistry 未注册。"));

        services.TryAddSingleton<IServiceRoutingTable>(_ =>
            ServiceRoutingTableRegistry.Instance
            ?? throw new InvalidOperationException("IServiceRoutingTable 未注册。"));
        services.TryAddSingleton<IServiceManifest>(_ =>
            ServiceManifestRegistry.Instance
            ?? throw new InvalidOperationException("IServiceManifest 未注册。"));
        PulseRoutingServiceExtensions.RegisterSharedRoutingDependencies(services);

        // 为每个命名服务器注册独立的依赖（使用 Keyed Services）
        // 注意：依赖创建顺序很重要

        // 0. 每个服务器独立的 gate 策略与宿主服务提供者视图
        services.AddKeyedSingleton<IClientFacingGatePolicy>(serverName, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<PulseServerOptions>>()
                .Get(serverName);
            return new ClientFacingGatePolicy(options.EnableClientFacingGate);
        });
        services.AddKeyedSingleton<ClientFacingGateServiceProvider>(serverName, (sp, key) =>
            new ClientFacingGateServiceProvider(
                sp,
                sp.GetRequiredKeyedService<IClientFacingGatePolicy>(key),
                key));

        // 1. 通道管理器（每个服务器独立）
        services.AddKeyedSingleton<IServerChannelManager>(serverName, (sp, key) =>
            ServerRuntimeComponentFactory.CreateChannelRegistry(
                sp.GetRequiredService<ILoggerFactory>()));

        services.AddKeyedSingleton<IPulseRouter>(serverName, (sp, key) =>
            new LocalPulseRouter(
                sp.GetRequiredKeyedService<IServerChannelManager>(key),
                sp.GetRequiredService<IGroupManager>(),
                sp.GetRequiredService<IUserConnectionMapping>(),
                sp.GetRequiredKeyedService<ClientFacingGateServiceProvider>(key),
                sp.GetRequiredService<ILogger<LocalPulseRouter>>(),
                sp.GetService<IServiceRoutingTable>(),
                sp.GetService<IResponseSerializerRegistry>(),
                sp.GetRequiredService<MessageDeduplicationCache>(),
                sp.GetRequiredService<DeliveryRetryOptions>()));

        // 2. 消息分发器（每个服务器独立）
        services.AddKeyedSingleton<IMessageDispatcher>(serverName, (sp, key) =>
            ServerRuntimeComponentFactory.CreateDispatcher(
                sp.GetRequiredService<IServiceRoutingTable>(),
                sp.GetRequiredService<ILoggerFactory>()));

        // 3. 响应处理器（每个服务器独立）
        services.AddKeyedSingleton<IResponseProcessor>(serverName, (sp, key) =>
        {
            return ServerRuntimeComponentFactory.CreateResponseProcessor(
                sp.GetRequiredKeyedService<IServerChannelManager>(key),
                sp.GetService<ISerializerProvider>(),
                sp.GetService<IResponseSerializerRegistry>(),
                sp.GetService<IServiceRoutingTable>(),
                sp.GetRequiredService<ILoggerFactory>());
        });

        // 4. 消息引擎（每个服务器独立）
        services.AddKeyedSingleton<ITieredMessageEngine>(serverName, (sp, key) =>
        {
            var serverOptions = sp.GetRequiredService<IOptionsMonitor<PulseServerOptions>>()
                .Get((string)key!);

            return ServerRuntimeComponentFactory.CreateMessageEngine(
                sp.GetRequiredKeyedService<IMessageDispatcher>(key),
                sp.GetRequiredKeyedService<ClientFacingGateServiceProvider>(key),
                serverOptions,
                sp.GetRequiredKeyedService<IServerChannelManager>(key),
                sp.GetRequiredKeyedService<IResponseProcessor>(key),
                sp.GetRequiredService<ILoggerFactory>());
        });

        // 5. 每个 name 只创建一个 runtime 组合根；其内部只引用该 name 的 registry。
        services.AddKeyedSingleton<ServerRuntime>(serverName, (sp, key) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<PulseServerOptions>>()
                .Get((string)key!);

            return ServerRuntimeComponentFactory.CreateRuntime(
                sp.GetRequiredKeyedService<ITieredMessageEngine>(key),
                sp.GetRequiredKeyedService<IServerChannelManager>(key),
                sp.GetRequiredService<ITransportIntegrationManager>(),
                sp.GetRequiredService<ILoggerFactory>(),
                Options.Create(options));
        });

        // 6. 注册命名服务器 facade（使用 Keyed Service）
        services.AddKeyedSingleton<INamedPulseServer>(
            serverName,
            (sp, key) =>
            {
                return new NamedPulseServer(
                    serverName,
                    sp.GetRequiredKeyedService<ServerRuntime>(key));
            });

        services.AddSingleton(new NamedServerRegistration(serverName));
        services.AddHostedService<NamedPulseServersHostedService>();

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

internal sealed record NamedServerRegistration(string ServerName);

internal sealed class NamedPulseServersHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<string> _serverNames;
    private readonly ILogger<NamedPulseServersHostedService> _logger;
    private readonly List<INamedPulseServer> _startedServers = new();

    public NamedPulseServersHostedService(
        IServiceProvider serviceProvider,
        IEnumerable<NamedServerRegistration> registrations,
        ILogger<NamedPulseServersHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _serverNames = registrations
            .Select(registration => registration.ServerName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var serverName in _serverNames)
            {
                var server = _serviceProvider.GetRequiredKeyedService<INamedPulseServer>(serverName);
                await server.StartAsync(cancellationToken).ConfigureAwait(false);
                _startedServers.Add(server);
                _logger.LogInformation("Named PulseServer started: {ServerName}", serverName);
            }
        }
        catch (Exception startException)
        {
            try
            {
                await StopStartedServersAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "A named PulseServer failed to start and rollback also failed.",
                    startException,
                    rollbackException);
            }

            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => StopStartedServersAsync(cancellationToken);

    private async Task StopStartedServersAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        for (var index = _startedServers.Count - 1; index >= 0; index--)
        {
            try
            {
                await _startedServers[index].StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(ex);
                _logger.LogError(ex,
                    "Failed to stop named PulseServer: {ServerName}",
                    _startedServers[index].ServerName);
            }
        }

        _startedServers.Clear();
        if (failures is not null)
        {
            throw new AggregateException("One or more named PulseServers failed to stop.", failures);
        }
    }
}

/// <summary>
/// PulseServer 托管服务包装器
/// 自动在应用启动时启动服务器，在应用停止时停止服务器
/// </summary>
internal sealed class PulseServerHostedService : IHostedService
{
    private readonly PulseServer _server;
    private readonly ILogger<PulseServerHostedService> _logger;

    public PulseServerHostedService(
        PulseServer server,
        ILogger<PulseServerHostedService> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting PulseServer via hosted service");

        try
        {
            await _server.StartAsync(cancellationToken);
            _logger.LogInformation("PulseServer started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start PulseServer");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping PulseServer via hosted service");

        try
        {
            await _server.StopAsync(cancellationToken);
            _logger.LogInformation("PulseServer stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping PulseServer");
            throw;
        }
    }
}
