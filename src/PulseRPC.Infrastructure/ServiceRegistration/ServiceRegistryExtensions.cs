using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Infrastructure;

namespace PulseRPC.ServiceRegistration;

public static class ServiceRegistryExtensions
{
    /// <summary>
    /// 添加 PulseRPC 服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceRegistration(
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
    public static IServiceCollection AddServiceRegistration(
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
        services.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IHostedService, ServiceRegistrar>(provider =>
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
    public static IServiceCollection AddServiceRegistration(
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
            var serviceInfo = ServiceInfo.Create(
                serviceId,
                serviceName,
                GetLocalIPAddress(),
                port,
                TransportProtocol.Tcp,
                null,
                new ServiceMetadata(options.DefaultMetadata)
            );

            options.AutoRegisterServices.Add(serviceInfo);

            // 应用额外配置
            configureOptions?.Invoke(options);
        });

        return AddServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 添加多个服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="servicesInfo">服务信息列表</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceRegistrations(
        this IServiceCollection services,
        IEnumerable<ServiceInfo> servicesInfo,
        Action<ServiceRegistrationOptions>? configureOptions = null)
    {
        services.Configure<ServiceRegistrationOptions>(options =>
        {
            options.AutoRegisterServices.AddRange(servicesInfo);
            configureOptions?.Invoke(options);
        });

        return AddServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 配置 Consul 服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="consulAddress">Consul 地址</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulRegistration(
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
        // services.AddConsulServiceRegistry(configuration => { });

        return AddServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 配置 Etcd 服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="etcdEndpoints">Etcd 端点</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddEtcdRegistration(
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
        // services.AddEtcdServiceRegistry(configuration => { });

        return AddServiceRegistration(services, configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// 配置服务注册选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureServiceRegistry(
        this IServiceCollection services,
        Action<ServiceRegistryOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services;
    }

    /// <summary>
    /// 添加服务注册
    /// </summary>
    /// <typeparam name="TImplementation">实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceRegistry<TImplementation>(this IServiceCollection services)
        where TImplementation : class, IServiceRegistry
    {
        services.TryAddSingleton<IServiceRegistry, TImplementation>();

        return services;
    }

    /// <summary>
    /// 添加自定义服务注册实现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="implementationFactory">实现工厂</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceRegistry(this IServiceCollection services,
        Func<IServiceProvider, IServiceRegistry> implementationFactory)
    {
        services.RemoveAll<IServiceRegistry>();
        services.AddSingleton(implementationFactory);

        return services;
    }

    /// <summary>
    /// 配置心跳选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureHeartbeat(
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
        services.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IHostedService, ServiceRegistrar>(provider =>
            provider.GetRequiredService<ServiceRegistrar>()));
    }

    /// <summary>
    /// 根据类型添加服务注册中心
    /// </summary>
    private static void AddServiceRegistryByType(IServiceCollection services, ServiceRegistryType type, ServiceRegistrationOptions options)
    {
        switch (type)
        {
            // case ServiceRegistryType.Consul:
            //     services.AddConsulServiceRegistry(configuration => { });
            //     break;
            // case ServiceRegistryType.Etcd:
            //     services.AddEtcdServiceRegistry(configuration => { });
            //     break;
            // case ServiceRegistryType.Zookeeper:
            //     services.AddZookeeperServiceRegistry(configuration => { });
            //     break;
            case ServiceRegistryType.Dns:
                // DNS 注册通常不需要额外配置
                break;
            case ServiceRegistryType.Custom:
                // 自定义实现由用户手动注册
                break;
            default:
                throw new NotSupportedException($"未注册的服务中心类型：{type}");
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
