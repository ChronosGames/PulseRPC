using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Serialization;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Engine;
using PulseRPC.Server.Events;
using PulseRPC.Server.Integration;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;
using PulseRPC.Sessions;

namespace PulseRPC.Server.Builder;

/// <summary>
/// 服务器构建器实现 - 高性能链式配置
/// </summary>
public sealed class PulseRPCServerBuilder : IPulseRPCServerBuilder
{
    private readonly List<TransportChannelConfiguration> _transports = new();
    private readonly List<Type> _middlewareTypes = new();
    private readonly List<Type> _interceptorTypes = new();

    public IServiceCollection Services { get; }

    public PulseRPCServerBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));

        // 注册基础服务
        RegisterCoreServices();
    }

    public IPulseRPCServerBuilder AddTcp(string name, int port,
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

    public IPulseRPCServerBuilder AddKcp(string name, int port,
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

    public IPulseRPCServerBuilder AddService<TService, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TService : class
        where TImplementation : class, TService
    {
        Services.Add(ServiceDescriptor.Describe(typeof(TService), typeof(TImplementation), lifetime));

        // 注册到服务选项中，用于服务发现
        Services.Configure<PulseRpcServiceOptions>(options =>
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

    public IPulseRPCServerBuilder AddService<TService>(TService implementationInstance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(implementationInstance);

        Services.AddSingleton(implementationInstance);

        Services.Configure<PulseRpcServiceOptions>(options =>
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

    public IPulseRPCServerBuilder UseHighPerformanceEngine(Action<MessageEngineOptions>? configure = null)
    {
        Services.Configure<MessageEngineOptions>(options =>
        {
            options.Enabled = true; // 确保启用
            configure?.Invoke(options);
        });

        // 注册高性能消息引擎相关服务
        Services.TryAddSingleton<IHighThroughputProcessorManager, HighThroughputProcessorManager>();

        return this;
    }

    public IPulseRPCServerBuilder UseTieredMessageProcessor(Action<TieredProcessorOptions>? configure = null)
    {
        Services.Configure<TieredProcessorOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        return this;
    }

    public IPulseRPCServerBuilder UsePriorityScheduler(Action<PrioritySchedulerOptions>? configure = null)
    {
        Services.Configure<PrioritySchedulerOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        return this;
    }

    public IPulseRPCServerBuilder UseAuthentication(Action<AuthenticationOptions>? configure = null)
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

    public IPulseRPCServerBuilder UseAuthorization(Action<AuthorizationOptions>? configure = null)
    {
        Services.Configure<AuthorizationOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        return this;
    }

    public IPulseRPCServerBuilder UseMiddleware<TMiddleware>() where TMiddleware : class, IPulseRpcMiddleware
    {
        _middlewareTypes.Add(typeof(TMiddleware));
        Services.TryAddTransient<TMiddleware>();
        return this;
    }

    public IPulseRPCServerBuilder UseInterceptor<TInterceptor>() where TInterceptor : class, IPulseRpcInterceptor
    {
        _interceptorTypes.Add(typeof(TInterceptor));
        Services.TryAddTransient<TInterceptor>();
        return this;
    }

    public IPulseRPCServerBuilder ConfigureServer(Action<ServerOptions> configure)
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
        Services.TryAddSingleton<IPulseRPCServer>(serviceProvider =>
        {
            var sessionManager = serviceProvider.GetRequiredService<IClientSessionManager>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var serverOptions = serviceProvider.GetRequiredService<IOptions<ServerOptions>>();
            var transportIntegrationManager = serviceProvider.GetRequiredService<ITransportIntegrationManager>();

            // 创建增强的服务器管理器
            var serverManager = new PulseRPCServer(
                sessionManager,
                loggerFactory,
                serverOptions,
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

        // 注册会话管理器
        Services.TryAddSingleton<IClientSessionManager, ClientSessionManager>();

        // 注册服务器通道管理器
        Services.TryAddSingleton<IServerChannelManager, ServerChannelManager>();

        // 注册事件发布器
        Services.TryAddSingleton<IEventPublisher, EventPublisher>();

        // 注册服务工厂
        Services.TryAddSingleton<IPulseRpcServiceFactory, PulseRpcServiceFactory>();

        // 注册消息处理器
        Services.TryAddSingleton<IMessageDispatcher, CompiledMessageDispatcher>();

        // 确保已注册传输层集成服务
        Services.AddTransportIntegration();
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
public class PulseRpcServiceOptions
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
public interface IPulseRpcServiceFactory
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
internal sealed class PulseRpcServiceFactory : IPulseRpcServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<PulseRpcServiceOptions> _serviceOptions;

    public PulseRpcServiceFactory(IServiceProvider serviceProvider, IOptions<PulseRpcServiceOptions> serviceOptions)
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
