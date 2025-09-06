using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using PulseRPC.Serialization;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Events;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Processing;

namespace PulseRPC.Server;

/// <summary>
/// 服务端依赖注入扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加PulseRPC服务器服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(this IServiceCollection services)
    {
        // 注册序列化器提供程序
        services.AddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

        // 注册通道管理器
        services.AddSingleton<IServerChannelManager, ServerChannelManager>();

        // 注册认证中间件
        services.AddSingleton<AuthenticationMiddleware>();

        // 注册ServiceRegistry（使用工厂方法）
        services.AddSingleton<ServiceRegistry>();

        // 注册事件发布器
        services.AddSingleton<IEventPublisher, EventPublisher>();

        // 注册默认服务器实例（无传输配置）
        services.TryAddSingleton<IPulseRpcServer, PulseRpcServerManager>();

        // 注册服务工厂
        services.TryAddSingleton<IPulseRpcServiceFactory, PulseRpcServiceFactory>();

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册服务器选项
        services.Configure<ServerOptions>(configuration.GetSection("PulseRPC:Server"));

        // 注册服务器管理器
        services.TryAddSingleton<IPulseRpcServer, PulseRpcServerManager>();

        // 注册服务工厂
        services.TryAddSingleton<IPulseRpcServiceFactory, PulseRpcServiceFactory>();

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务器 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(
        this IServiceCollection services,
        Action<ServerOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // 注册基础组件
        services.AddPulseRpcServer();

        // 注册服务器管理器
        services.TryAddSingleton<IPulseRpcServer, PulseRpcServerManager>();

        // 注册服务工厂
        services.TryAddSingleton<IPulseRpcServiceFactory, PulseRpcServiceFactory>();

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务器 (使用服务器配置构建器)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">服务器配置构建器</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(
        this IServiceCollection services,
        Action<ServerConfigurationBuilder> configure)
    {
        var builder = new ServerConfigurationBuilder();
        configure(builder);
        var (transports, serverConfig) = builder.Build();

        // 配置服务器选项
        if (serverConfig != null)
        {
            services.Configure(serverConfig);
        }

        // 注册基础组件
        services.AddPulseRpcServer();

        // 注册带传输配置的服务器管理器
        services.AddSingleton<IPulseRpcServer>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var serverChannelManager = sp.GetRequiredService<IServerChannelManager>();
            var serverOptions = sp.GetRequiredService<IOptions<ServerOptions>>();

            var serverManager = new PulseRpcServerManager(serverChannelManager, loggerFactory, serverOptions);

            // 自动添加配置的传输通道
            foreach (var transport in transports)
            {
                serverManager.AddTransport(
                    transport.Name,
                    transport.Type,
                    transport.Port,
                    transport.Options,
                    transport.IsDefault);
            }

            return serverManager;
        });

        // 注册服务工厂
        services.TryAddSingleton<IPulseRpcServiceFactory, PulseRpcServiceFactory>();

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务器 (使用传输配置列表)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="transports">传输配置列表</param>
    /// <param name="configureServer">服务器配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(
        this IServiceCollection services,
        IEnumerable<TransportChannelConfiguration> transports,
        Action<ServerOptions>? configureServer = null)
    {
        // 配置服务器选项
        if (configureServer != null)
        {
            services.Configure(configureServer);
        }

        // 注册基础组件
        services.AddPulseRpcServer();

        var transportList = transports.ToList();

        // 注册带传输配置的服务器管理器
        services.AddSingleton<IPulseRpcServer>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var serverChannelManager = sp.GetRequiredService<IServerChannelManager>();
            var serverOptions = sp.GetRequiredService<IOptions<ServerOptions>>();

            var serverManager = new PulseRpcServerManager(serverChannelManager, loggerFactory, serverOptions);

            // 自动添加传输通道
            foreach (var transport in transportList)
            {
                serverManager.AddTransport(
                    transport.Name,
                    transport.Type,
                    transport.Port,
                    transport.Options,
                    transport.IsDefault);
            }

            return serverManager;
        });

        // 注册服务工厂
        services.TryAddSingleton<IPulseRpcServiceFactory, PulseRpcServiceFactory>();

        return services;
    }

    /// <summary>
    /// 快速配置TCP服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="port">监听端口</param>
    /// <param name="configureServer">服务器配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcTcpServer(
        this IServiceCollection services,
        int port,
        Action<ServerOptions>? configureServer = null)
    {
        return services.AddPulseRpcServer(builder =>
        {
            builder.AddTcp("Default", port, isDefault: true);
            if (configureServer != null)
            {
                builder.ConfigureServer(configureServer);
            }
        });
    }

    /// <summary>
    /// 快速配置KCP服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="port">监听端口</param>
    /// <param name="configureServer">服务器配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcKcpServer(
        this IServiceCollection services,
        int port,
        Action<ServerOptions>? configureServer = null)
    {
        return services.AddPulseRpcServer(builder =>
        {
            builder.AddKcp("Default", port, isDefault: true);
            if (configureServer != null)
            {
                builder.ConfigureServer(configureServer);
            }
        });
    }

    /// <summary>
    /// 添加 PulseRPC 服务
    /// </summary>
    /// <typeparam name="TService">服务接口类型</typeparam>
    /// <typeparam name="TImplementation">服务实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcService<TService, TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.Add(new Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Singleton));

        // 注册服务描述符
        services.Configure<PulseRpcServiceOptions>(options =>
        {
            options.Services.Add(new ServiceDescriptor
            {
                ServiceType = typeof(TService),
                ImplementationType = typeof(TImplementation),
                ServiceName = typeof(TService).Name,
                Lifetime = ServiceLifetime.Singleton,
            });
        });

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务 (使用实例)
    /// </summary>
    /// <typeparam name="TService">服务接口类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="implementationInstance">服务实例</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcService<TService>(
        this IServiceCollection services,
        TService implementationInstance)
        where TService : class
    {
        services.AddSingleton(implementationInstance);

        // 注册服务描述符
        services.Configure<PulseRpcServiceOptions>(options =>
        {
            options.Services.Add(new ServiceDescriptor
            {
                ServiceType = typeof(TService),
                ImplementationType = implementationInstance.GetType(),
                ServiceName = typeof(TService).Name,
                Lifetime = ServiceLifetime.Singleton
            });
        });

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务 (使用工厂)
    /// </summary>
    /// <typeparam name="TService">服务接口类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="implementationFactory">服务工厂</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcService<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
    {
        services.Add(new Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(TService), implementationFactory, lifetime));

        // 注册服务描述符
        services.Configure<PulseRpcServiceOptions>(options =>
        {
            options.Services.Add(new ServiceDescriptor
            {
                ServiceType = typeof(TService),
                ServiceName = typeof(TService).Name,
                Lifetime = lifetime
            });
        });

        return services;
    }

    /// <summary>
    /// 配置服务器选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigurePulseRpcServer(
        this IServiceCollection services,
        Action<ServerOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services;
    }

    /// <summary>
    /// 配置性能选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigurePulseRpcPerformance(
        this IServiceCollection services,
        Action<PerformanceOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services;
    }

    /// <summary>
    /// 配置安全选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigurePulseRpcSecurity(
        this IServiceCollection services,
        Action<SecurityOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services;
    }

    /// <summary>
    /// 添加中间件
    /// </summary>
    /// <typeparam name="TMiddleware">中间件类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcMiddleware<TMiddleware>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TMiddleware : class, IPulseRpcMiddleware
    {
        services.Add(new Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(IPulseRpcMiddleware), typeof(TMiddleware), lifetime));

        return services;
    }

    /// <summary>
    /// 添加拦截器
    /// </summary>
    /// <typeparam name="TInterceptor">拦截器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcInterceptor<TInterceptor>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TInterceptor : class, IPulseRpcInterceptor
    {
        services.Add(new Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(IPulseRpcInterceptor), typeof(TInterceptor), lifetime));

        return services;
    }
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
    IEnumerable<ServiceDescriptor> GetRegisteredServices();
}

/// <summary>
/// PulseRPC 服务工厂实现
/// </summary>
internal class PulseRpcServiceFactory(
    IServiceProvider serviceProvider,
    IOptions<PulseRpcServiceOptions> serviceOptions)
    : IPulseRpcServiceFactory
{
    public object CreateService(Type serviceType)
    {
        return serviceProvider.GetRequiredService(serviceType);
    }

    public TService CreateService<TService>() where TService : class
    {
        return serviceProvider.GetRequiredService<TService>();
    }

    public IEnumerable<ServiceDescriptor> GetRegisteredServices()
    {
        return serviceOptions.Value.Services;
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
    public List<ServiceDescriptor> Services { get; set; } = new();
}

/// <summary>
/// 服务描述符
/// </summary>
public class ServiceDescriptor
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
/// 性能配置选项
/// </summary>
public class PerformanceOptions
{
    /// <summary>
    /// 最大并发连接数
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 1000;

    /// <summary>
    /// 最大并发请求数
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 10000;

    /// <summary>
    /// 请求缓冲区大小
    /// </summary>
    public int RequestBufferSize { get; set; } = 8192;

    /// <summary>
    /// 响应缓冲区大小
    /// </summary>
    public int ResponseBufferSize { get; set; } = 8192;

    /// <summary>
    /// 是否启用性能监控
    /// </summary>
    public bool EnablePerformanceMonitoring { get; set; } = true;

    /// <summary>
    /// 性能统计采样间隔
    /// </summary>
    public TimeSpan PerformanceStatsSamplingInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 安全配置选项
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// 是否启用SSL/TLS
    /// </summary>
    public bool EnableSsl { get; set; } = false;

    /// <summary>
    /// SSL证书路径
    /// </summary>
    public string? SslCertificatePath { get; set; }

    /// <summary>
    /// SSL证书密码
    /// </summary>
    public string? SslCertificatePassword { get; set; }

    /// <summary>
    /// 是否启用客户端证书验证
    /// </summary>
    public bool RequireClientCertificate { get; set; } = false;

    /// <summary>
    /// 是否启用身份验证
    /// </summary>
    public bool EnableAuthentication { get; set; } = false;

    /// <summary>
    /// 身份验证方案
    /// </summary>
    public string AuthenticationScheme { get; set; } = "Bearer";

    /// <summary>
    /// 是否启用授权
    /// </summary>
    public bool EnableAuthorization { get; set; } = false;

    /// <summary>
    /// JWT 密钥
    /// </summary>
    public string? JwtSecretKey { get; set; }

    /// <summary>
    /// JWT 发行者
    /// </summary>
    public string? JwtIssuer { get; set; }

    /// <summary>
    /// JWT 受众
    /// </summary>
    public string? JwtAudience { get; set; }

    /// <summary>
    /// JWT 过期时间
    /// </summary>
    public TimeSpan JwtExpiration { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// PulseRPC 中间件接口
/// </summary>
public interface IPulseRpcMiddleware
{
    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="next">下一个中间件</param>
    /// <returns>处理任务</returns>
    Task InvokeAsync(IPulseRpcContext context, Func<Task> next);
}

/// <summary>
/// PulseRPC 拦截器接口
/// </summary>
public interface IPulseRpcInterceptor
{
    /// <summary>
    /// 请求前拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>拦截任务</returns>
    Task OnRequestAsync(IPulseRpcContext context);

    /// <summary>
    /// 响应后拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>拦截任务</returns>
    Task OnResponseAsync(IPulseRpcContext context);

    /// <summary>
    /// 异常拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="exception">异常信息</param>
    /// <returns>拦截任务</returns>
    Task OnExceptionAsync(IPulseRpcContext context, Exception exception);
}

/// <summary>
/// PulseRPC 请求上下文接口
/// </summary>
public interface IPulseRpcContext
{
    /// <summary>
    /// 请求标识
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// 服务名称
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// 方法名称
    /// </summary>
    string MethodName { get; }

    /// <summary>
    /// 请求数据
    /// </summary>
    object? RequestData { get; }

    /// <summary>
    /// 响应数据
    /// </summary>
    object? ResponseData { get; set; }

    /// <summary>
    /// 请求时间
    /// </summary>
    DateTime RequestTime { get; }

    /// <summary>
    /// 客户端信息
    /// </summary>
    Dictionary<string, object> ClientInfo { get; }

    /// <summary>
    /// 上下文数据
    /// </summary>
    Dictionary<string, object> Items { get; }
}

/// <summary>
/// 高吞吐量消息处理器扩展方法
/// </summary>
public static class HighThroughputProcessorExtensions
{
    /// <summary>
    /// 添加高吞吐量消息处理器支持
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHighThroughputProcessor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册高吞吐量处理器配置
        services.Configure<HighThroughputProcessorOptions>(
            configuration.GetSection("PulseRPC:Server:HighThroughputProcessor"));

        return services.AddHighThroughputProcessorServices();
    }

    /// <summary>
    /// 添加高吞吐量消息处理器支持 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHighThroughputProcessor(
        this IServiceCollection services,
        Action<HighThroughputProcessorOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services.AddHighThroughputProcessorServices();
    }

    /// <summary>
    /// 添加默认启用的高吞吐量消息处理器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHighThroughputProcessor(this IServiceCollection services)
    {
        services.Configure<HighThroughputProcessorOptions>(options =>
        {
            options.Enabled = true;
        });

        return services.AddHighThroughputProcessorServices();
    }

    /// <summary>
    /// 配置高吞吐量处理器性能参数
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="l1BufferSize">L1缓冲区大小</param>
    /// <param name="batchIntervalMs">批处理间隔(毫秒)</param>
    /// <param name="maxBatchSize">最大批处理大小</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureHighThroughputProcessor(
        this IServiceCollection services,
        int l1BufferSize = 2048,
        int batchIntervalMs = 2,
        int maxBatchSize = 32)
    {
        services.Configure<HighThroughputProcessorOptions>(options =>
        {
            options.Enabled = true;
            options.L1BufferSize = l1BufferSize;
            options.BatchIntervalMs = batchIntervalMs;
            options.MaxBatchSize = maxBatchSize;
        });

        return services.AddHighThroughputProcessorServices();
    }

    /// <summary>
    /// 配置高吞吐量处理器背压策略
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="normalDropRate">普通消息丢弃率(0.0-1.0)</param>
    /// <param name="criticalTimeoutUs">关键消息超时(微秒)</param>
    /// <param name="enableDetailedLogging">是否启用详细日志</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureHighThroughputBackpressure(
        this IServiceCollection services,
        double normalDropRate = 0.8,
        int criticalTimeoutUs = 100,
        bool enableDetailedLogging = false)
    {
        services.Configure<HighThroughputProcessorOptions>(options =>
        {
            options.NormalMessageDropRate = normalDropRate;
            options.CriticalMessageTimeoutUs = criticalTimeoutUs;
            options.EnableDetailedLogging = enableDetailedLogging;
        });

        return services;
    }

    /// <summary>
    /// 添加基础高吞吐量处理器服务
    /// </summary>
    private static IServiceCollection AddHighThroughputProcessorServices(this IServiceCollection services)
    {
        // 注册消息处理器管理器
        services.TryAddSingleton<IHighThroughputProcessorManager, HighThroughputProcessorManager>();

        // 添加临时的消息处理注册表实现
        services.TryAddSingleton<IMessageHandlerRegistry, DefaultMessageHandlerRegistry>();

        return services;
    }
}

/// <summary>
/// 临时的默认消息处理注册表实现
/// </summary>
internal class DefaultMessageHandlerRegistry : IMessageHandlerRegistry
{
    private readonly ILogger<DefaultMessageHandlerRegistry> _logger;

    public DefaultMessageHandlerRegistry(ILogger<DefaultMessageHandlerRegistry> logger)
    {
        _logger = logger;
    }

    public async Task<object?> HandleAsync(ServerMessage message)
    {
        // 默认实现：简单回显处理
        _logger.LogDebug("处理消息: {MessageType}, SequenceId: {SequenceId}",
            message.GetType().Name, message.SequenceId);

        await Task.Delay(1); // 模拟处理延迟

        return new
        {
            Type = message.GetType().Name,
            SequenceId = message.SequenceId,
            ProcessedAt = DateTime.UtcNow,
            Message = "Processed successfully"
        };
    }
}
