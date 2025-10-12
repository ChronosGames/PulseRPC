using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Scheduling;
using PulseRPC.Serialization;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Dispatch;
using PulseRPC.Server.Engine;
using PulseRPC.Server.Events;
using PulseRPC.Server.Integration;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Response;
using PulseRPC.Server.Scheduling;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Builder;

/// <summary>
/// 服务器构建器实现 - 高性能链式配置
/// </summary>
public sealed class PulseServerBuilder : IPulseServerBuilder
{
    private readonly List<TransportChannelConfiguration> _transports = new();
    private readonly List<Type> _middlewareTypes = new();
    private readonly List<Type> _interceptorTypes = new();

    public IServiceCollection Services { get; }

    public PulseServerBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));

        // 注册基础服务
        RegisterCoreServices();
    }

    public IPulseServerBuilder AddTcp(string name, int port,
        Action<TcpTransportOptions>? configure = null, bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "端口必须在1-65535范围内");
        }

        var options = new TcpTransportOptions();
        configure?.Invoke(options);

        var config = new TransportChannelConfiguration
        {
            Name = name,
            Type = TransportType.Tcp,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };

        _transports.Add(config);
        return this;
    }

    public IPulseServerBuilder AddKcp(string name, int port,
        Action<KcpTransportOptions>? configure = null, bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "端口必须在1-65535范围内");
        }

        var options = new KcpTransportOptions();
        configure?.Invoke(options);

        var config = new TransportChannelConfiguration
        {
            Name = name,
            Type = TransportType.Kcp,
            Port = port,
            Options = options,
            IsDefault = isDefault
        };

        _transports.Add(config);
        return this;
    }

    public IPulseServerBuilder AddService<TService, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TService : class
        where TImplementation : class, TService
    {
        Services.Add(ServiceDescriptor.Describe(typeof(TService), typeof(TImplementation), lifetime));

        // 注册到服务选项中，用于服务发现
        Services.Configure<PulseServiceOptions>(options =>
        {
            options.Services.Add(new PulseRPCServiceDescriptor
            {
                ServiceType = typeof(TService),
                ImplementationType = typeof(TImplementation),
                ServiceName = typeof(TService).Name,
                Lifetime = lifetime
            });
        });

        return this;
    }

    public IPulseServerBuilder AddService<TService>(TService implementationInstance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(implementationInstance);

        Services.AddSingleton(implementationInstance);

        Services.Configure<PulseServiceOptions>(options =>
        {
            options.Services.Add(new PulseRPCServiceDescriptor
            {
                ServiceType = typeof(TService),
                ImplementationType = implementationInstance.GetType(),
                ServiceName = typeof(TService).Name,
                Lifetime = ServiceLifetime.Singleton
            });
        });

        return this;
    }

    public IPulseServerBuilder UseHighPerformanceEngine(Action<MessageEngineOptions>? configure = null)
    {
        Services.Configure<MessageEngineOptions>(options =>
        {
            options.Enabled = true; // 确保启用
            configure?.Invoke(options);
        });

        // 高性能消息引擎已由 TieredMessageEngineServiceExtensions.AddTieredMessageEngine() 注册
        Services.AddSingleton<ITieredMessageEngineManager, TieredMessageEngineManager>();

        return this;
    }

    public IPulseServerBuilder UseTieredMessageProcessor(Action<TieredProcessorOptions>? configure = null)
    {
        Services.Configure<TieredProcessorOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        return this;
    }

    public IPulseServerBuilder UsePriorityScheduler(Action<PrioritySchedulerOptions>? configure = null)
    {
        Services.Configure<PrioritySchedulerOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        return this;
    }

    public IPulseServerBuilder UseAuthentication(Action<AuthenticationOptions>? configure = null)
    {
        Services.Configure<AuthenticationOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        // 注册认证服务
        Services.TryAddSingleton<AuthenticationMiddleware>();

        return this;
    }

    public IPulseServerBuilder UseAuthorization(Action<AuthorizationOptions>? configure = null)
    {
        Services.Configure<AuthorizationOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        return this;
    }

    public IPulseServerBuilder UseMiddleware<TMiddleware>() where TMiddleware : class, IPulseMiddleware
    {
        _middlewareTypes.Add(typeof(TMiddleware));
        Services.TryAddTransient<TMiddleware>();
        return this;
    }

    public IPulseServerBuilder UseInterceptor<TInterceptor>() where TInterceptor : class, IPulseInterceptor
    {
        _interceptorTypes.Add(typeof(TInterceptor));
        Services.TryAddTransient<TInterceptor>();
        return this;
    }

    public IPulseServerBuilder ConfigureServer(Action<ServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    public void Build()
    {
        // 验证配置
        ValidateConfiguration();

        // 注册服务器实例
        Services.TryAddSingleton<IPulseServer>(serviceProvider =>
        {
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var serverOptions = serviceProvider.GetRequiredService<IOptions<ServerOptions>>();
            var channelManager = serviceProvider.GetRequiredService<IServerChannelManager>();
            var transportIntegrationManager = serviceProvider.GetRequiredService<ITransportIntegrationManager>();

            // 创建增强的服务器管理器
            var serverManager = new PulseServer(
                loggerFactory,
                serverOptions,
                channelManager,
                transportIntegrationManager);

            // 添加配置的传输通道
            foreach (var transport in _transports)
            {
                serverManager.AddTransport(transport);
            }

            return serverManager;
        });
    }

    /// <summary>
    /// 注册核心服务
    /// </summary>
    private void RegisterCoreServices()
    {
        // 注册序列化器提供程序
        Services.TryAddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

        // 注册服务器通道管理器
        Services.TryAddSingleton<IServerChannelManager, ServerChannelManager>();

        // 注册事件发布器
        Services.TryAddSingleton<IEventPublisher, EventPublisher>();

        // 注册服务工厂
        Services.TryAddSingleton<IPulseServiceFactory, PulseServiceFactory>();

        // 注册消息处理器
        Services.TryAddSingleton<IMessageDispatcher, GeneratedMessageDispatcher>();

        // 注册答复处理器
        Services.TryAddSingleton<IResponseProcessor, HighPerformanceResponseProcessor>();

        // 确保已注册传输层集成服务
        Services.AddTransportIntegration();

        // 注册调度器配置（默认配置，可通过ConfigureScheduler覆盖）
        Services.Configure<SchedulerConfiguration>(config =>
        {
            config.InitialThreadCount = Environment.ProcessorCount;
            config.MaxThreadCount = Environment.ProcessorCount * 2;
            config.ThreadIdleTimeout = TimeSpan.FromSeconds(30);
            config.ChannelCapacity = 1024;
            config.EnableMetrics = true;
        });
    }

    /// <summary>
    /// 配置服务线程调度器
    /// </summary>
    /// <param name="configure">调度器配置委托</param>
    /// <returns>构建器实例</returns>
    public IPulseServerBuilder ConfigureScheduler(Action<SchedulerConfiguration> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        // 重新配置调度器
        Services.Configure(configure);

        // 注册调度器服务（如果尚未注册）
        Services.TryAddSingleton<IServiceScheduler>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<SchedulerConfiguration>>().Value;
            var logger = sp.GetService<ILogger<ServiceThreadScheduler>>();

            var scheduler = new ServiceThreadScheduler(config, logger);

            // 自动启动调度器
            scheduler.StartAsync().GetAwaiter().GetResult();

            return scheduler;
        });

        return this;
    }

    /// <summary>
    /// 验证构建器配置
    /// </summary>
    private void ValidateConfiguration()
    {
        if (_transports.Count == 0)
        {
            throw new InvalidOperationException("至少需要配置一个传输通道");
        }

        // 检查默认传输
        var defaultTransports = _transports.Where(t => t.IsDefault).ToList();
        if (defaultTransports.Count > 1)
        {
            throw new InvalidOperationException($"只能有一个默认传输通道，但找到了{defaultTransports.Count}个");
        }

        // 检查传输名称重复
        var duplicateNames = _transports.GroupBy(t => t.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            throw new InvalidOperationException($"传输通道名称重复: {string.Join(", ", duplicateNames)}");
        }

        // 检查端口冲突
        var duplicatePorts = _transports.GroupBy(t => t.Port)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatePorts.Count > 0)
        {
            throw new InvalidOperationException($"传输通道端口冲突: {string.Join(", ", duplicatePorts)}");
        }
    }
}

/// <summary>
/// 传输层集成扩展方法
/// </summary>
internal static class TransportIntegrationExtensions
{
    /// <summary>
    /// 注册传输层集成服务
    /// </summary>
    public static IServiceCollection AddTransportIntegration(this IServiceCollection services)
    {
        // 注册传输集成管理器
        services.TryAddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();

        // 注册内置传输提供程序 - 使用 AddSingleton 确保所有提供程序都被注册
        services.AddSingleton<ITransportProvider, TcpTransportProvider>();
        services.AddSingleton<ITransportProvider, KcpTransportProvider>();

        return services;
    }
}

/// <summary>
/// PulseRPC 服务选项
/// </summary>
public class PulseServiceOptions
{
    /// <summary>
    /// 注册的服务集合
    /// </summary>
    public List<PulseRPCServiceDescriptor> Services { get; set; } = new();
}

/// <summary>
/// 服务描述符
/// </summary>
public class PulseRPCServiceDescriptor
{
    /// <summary>
    /// 服务类型
    /// </summary>
    public Type ServiceType { get; set; } = null!;

    /// <summary>
    /// 实现类型
    /// </summary>
    public Type? ImplementationType { get; set; }

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 服务生命周期
    /// </summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// 服务版本
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 服务标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// PulseRPC 服务工厂接口
/// </summary>
public interface IPulseServiceFactory
{
    /// <summary>
    /// 创建服务实例
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>服务实例</returns>
    object CreateService(Type serviceType);

    /// <summary>
    /// 创建服务实例
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <returns>服务实例</returns>
    TService CreateService<TService>() where TService : class;

    /// <summary>
    /// 获取所有已注册的服务
    /// </summary>
    /// <returns>服务描述符集合</returns>
    IEnumerable<PulseRPCServiceDescriptor> GetRegisteredServices();
}

/// <summary>
/// PulseRPC 服务工厂实现
/// </summary>
internal sealed class PulseServiceFactory : IPulseServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<PulseServiceOptions> _serviceOptions;

    public PulseServiceFactory(IServiceProvider serviceProvider, IOptions<PulseServiceOptions> serviceOptions)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _serviceOptions = serviceOptions ?? throw new ArgumentNullException(nameof(serviceOptions));
    }

    public object CreateService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _serviceProvider.GetRequiredService(serviceType);
    }

    public TService CreateService<TService>() where TService : class
    {
        return _serviceProvider.GetRequiredService<TService>();
    }

    public IEnumerable<PulseRPCServiceDescriptor> GetRegisteredServices()
    {
        return _serviceOptions.Value.Services;
    }
}
