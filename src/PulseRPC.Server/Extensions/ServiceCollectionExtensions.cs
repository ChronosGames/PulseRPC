using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PulseRPC.Server.ServiceRegistration;
using System.Net;
using Microsoft.Extensions.Logging;
using PulseRPC.Serialization;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Events;
using PulseRPC.Server.ServiceDiscovery;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;

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
        services.AddSingleton<IServerChannelManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ServerChannelManager>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new ServerChannelManager(logger, loggerFactory);
        });

        // 注册认证中间件
        services.AddSingleton<AuthenticationMiddleware>();

        // 注册ServiceRegistry（使用工厂方法）
        services.AddSingleton<ServiceRegistry>(sp =>
        {
            var authMiddleware = sp.GetRequiredService<AuthenticationMiddleware>();
            var channelManager = sp.GetRequiredService<IServerChannelManager>();
            var serializerProvider = sp.GetRequiredService<ISerializerProvider>();
            var logger = sp.GetRequiredService<ILogger<ServiceRegistry>>();

            return ServiceRegistry.CreateWithRpcHandling(authMiddleware, channelManager, serializerProvider, logger);
        });

        // 注册事件发布器
        services.AddSingleton<IEventPublisher, EventPublisher>();

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
        services.TryAddSingleton<ServerManager>();

        // 注册服务注册管理器为后台服务
        services.AddHostedService<ServiceRegistryManager>();

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

        // 注册服务器管理器
        services.TryAddSingleton<ServerManager>();

        // 注册服务注册管理器为后台服务
        services.AddHostedService<ServiceRegistryManager>();

        // 注册服务工厂
        services.TryAddSingleton<IPulseRpcServiceFactory, PulseRpcServiceFactory>();

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务
    /// </summary>
    /// <typeparam name="TService">服务接口类型</typeparam>
    /// <typeparam name="TImplementation">服务实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcService<TService, TImplementation>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
        where TImplementation : class, TService
    {
        services.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));

        // 注册服务描述符
        services.Configure<PulseRpcServiceOptions>(options =>
        {
            options.Services.Add(new ServiceDescriptor
            {
                ServiceType = typeof(TService),
                ImplementationType = typeof(TImplementation),
                ServiceName = typeof(TService).Name,
                Lifetime = lifetime
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
    /// 配置服务注册选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigurePulseRpcServiceRegistry(
        this IServiceCollection services,
        Action<ServiceRegistryOptions> configureOptions)
    {
        services.Configure(configureOptions);
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

    /// <summary>
    /// 添加 PulseRPC 服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceRegistration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册配置选项
        services.Configure<ServiceRegistrationOptions>(configuration.GetSection("PulseRPC:ServiceRegistration"));

        // 添加核心服务
        AddServiceRegistrationCore(services, configuration);

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务注册 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceRegistration(
        this IServiceCollection services,
        Action<ServiceRegistrationOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // 获取配置以确定服务注册类型
        var options = new ServiceRegistrationOptions();
        configureOptions(options);

        // 根据配置添加相应的服务注册中心
        AddServiceRegistryByType(services, options.RegistryType, options);

        // 添加核心服务
        services.TryAddSingleton<ServiceRegistrar>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ServiceRegistrar>(provider =>
            provider.GetRequiredService<ServiceRegistrar>()));

        return services;
    }

    /// <summary>
    /// 添加服务注册（自动配置）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="port">服务端口</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceRegistration(
        this IServiceCollection services,
        string serviceName,
        int port,
        Action<ServiceRegistrationOptions>? configureOptions = null)
    {
        services.Configure<ServiceRegistrationOptions>(options =>
        {
            // 生成服务ID
            var serviceId = GenerateServiceId(serviceName, port, options.IdGenerationStrategy, options.ServiceIdPrefix);

            // 自动添加服务信息
            var serviceInfo = new ServiceInfo
            {
                ServiceId = serviceId,
                ServiceName = serviceName,
                Host = GetLocalIPAddress(),
                Port = port,
                Tags = new Dictionary<string, string>(options.DefaultTags),
                Metadata = new Dictionary<string, object>(options.DefaultMetadata)
            };

            options.AutoRegisterServices.Add(serviceInfo);

            // 应用额外配置
            configureOptions?.Invoke(options);
        });

        return AddPulseRpcServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 添加多个服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="servicesInfo">服务信息列表</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceRegistrations(
        this IServiceCollection services,
        IEnumerable<ServiceInfo> servicesInfo,
        Action<ServiceRegistrationOptions>? configureOptions = null)
    {
        services.Configure<ServiceRegistrationOptions>(options =>
        {
            options.AutoRegisterServices.AddRange(servicesInfo);
            configureOptions?.Invoke(options);
        });

        return AddPulseRpcServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 配置 Consul 服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="consulAddress">Consul 地址</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcConsulRegistration(
        this IServiceCollection services,
        string consulAddress = "http://localhost:8500",
        Action<ServiceRegistrationOptions>? configureOptions = null)
    {
        services.Configure<ServiceRegistrationOptions>(options =>
        {
            options.RegistryType = ServiceRegistryType.Consul;
            options.ConsulAddress = consulAddress;
            configureOptions?.Invoke(options);
        });

        // 添加 Consul 服务注册
        services.AddConsulServiceRegistry(configuration => { });

        return AddPulseRpcServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 配置 Etcd 服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="etcdEndpoints">Etcd 端点</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcEtcdRegistration(
        this IServiceCollection services,
        string[] etcdEndpoints,
        Action<ServiceRegistrationOptions>? configureOptions = null)
    {
        services.Configure<ServiceRegistrationOptions>(options =>
        {
            options.RegistryType = ServiceRegistryType.Etcd;
            options.EtcdEndpoints = etcdEndpoints;
            configureOptions?.Invoke(options);
        });

        // 添加 Etcd 服务注册
        services.AddEtcdServiceRegistry(configuration => { });

        return AddPulseRpcServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 配置健康检查选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigurePulseRpcHealthCheck(
        this IServiceCollection services,
        Action<HealthCheckOptions> configureOptions)
    {
        services.Configure<ServiceRegistrationOptions>(options =>
        {
            var healthCheckOptions = new HealthCheckOptions();
            configureOptions(healthCheckOptions);

            options.EnableHealthCheck = healthCheckOptions.Enabled;
            options.HealthCheckInterval = healthCheckOptions.Interval;
            options.HealthCheckTimeout = healthCheckOptions.Timeout;
            options.HealthCheckPath = healthCheckOptions.Path;
        });

        return services;
    }

    /// <summary>
    /// 配置心跳选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigurePulseRpcHeartbeat(
        this IServiceCollection services,
        Action<HeartbeatOptions> configureOptions)
    {
        services.Configure<ServiceRegistrationOptions>(options =>
        {
            var heartbeatOptions = new HeartbeatOptions();
            configureOptions(heartbeatOptions);

            options.EnableHeartbeat = heartbeatOptions.Enabled;
            options.HeartbeatInterval = heartbeatOptions.Interval;
            options.HeartbeatTimeout = heartbeatOptions.Timeout;
        });

        return services;
    }

    /// <summary>
    /// 获取服务注册器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>服务注册器</returns>
    public static ServiceRegistrar GetServiceRegistrar(this IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<ServiceRegistrar>();
    }

    #region Private Methods

    /// <summary>
    /// 添加服务注册核心服务
    /// </summary>
    private static void AddServiceRegistrationCore(IServiceCollection services, IConfiguration configuration)
    {
        // 获取配置以确定服务注册类型
        var options = new ServiceRegistrationOptions();
        configuration.GetSection("PulseRPC:ServiceRegistration").Bind(options);

        // 根据配置添加相应的服务注册中心
        AddServiceRegistryByType(services, options.RegistryType, options);

        // 添加服务注册器
        services.TryAddSingleton<ServiceRegistrar>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ServiceRegistrar>(provider =>
            provider.GetRequiredService<ServiceRegistrar>()));
    }

    /// <summary>
    /// 根据类型添加服务注册中心
    /// </summary>
    private static void AddServiceRegistryByType(IServiceCollection services, ServiceRegistryType type, ServiceRegistrationOptions options)
    {
        switch (type)
        {
            case ServiceRegistryType.Consul:
                services.AddConsulServiceRegistry(configuration => { });
                break;
            case ServiceRegistryType.Etcd:
                services.AddEtcdServiceRegistry(configuration => { });
                break;
            case ServiceRegistryType.Zookeeper:
                services.AddZookeeperServiceRegistry(configuration => { });
                break;
            case ServiceRegistryType.Dns:
                // DNS 注册通常不需要额外配置
                break;
            case ServiceRegistryType.Custom:
                // 自定义实现由用户手动注册
                break;
        }
    }

    /// <summary>
    /// 生成服务ID
    /// </summary>
    private static string GenerateServiceId(string serviceName, int port, ServiceIdGenerationStrategy strategy, string? prefix)
    {
        var id = strategy switch
        {
            ServiceIdGenerationStrategy.HostNameAndPort => $"{Environment.MachineName}:{port}",
            ServiceIdGenerationStrategy.IpAddressAndPort => $"{GetLocalIPAddress()}:{port}",
            ServiceIdGenerationStrategy.Guid => Guid.NewGuid().ToString("N"),
            ServiceIdGenerationStrategy.Custom => $"{serviceName}-{Guid.NewGuid().ToString("N")[..8]}",
            _ => $"{serviceName}-{Environment.MachineName}:{port}"
        };

        return string.IsNullOrEmpty(prefix) ? $"{serviceName}-{id}" : $"{prefix}-{serviceName}-{id}";
    }

    /// <summary>
    /// 获取本地IP地址
    /// </summary>
    private static string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return ipAddress?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    #endregion
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
/// 健康检查配置选项
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 健康检查超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 健康检查路径
    /// </summary>
    public string Path { get; set; } = "/health";
}

/// <summary>
/// 心跳配置选项
/// </summary>
public class HeartbeatOptions
{
    /// <summary>
    /// 是否启用心跳
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 心跳超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
