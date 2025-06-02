using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Client.ServiceDiscovery;
using PulseRPC.Server.ServiceDiscovery;
using PulseRPC.ServiceDiscovery.HealthCheck;
using PulseRPC.ServiceDiscovery.Implementations;

namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务发现依赖注入扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加服务发现支持
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceDiscovery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册健康检查服务
        services.AddHealthCheck(configuration);

        return services;
    }

    /// <summary>
    /// 添加 Consul 服务发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ConsulOptions>(configuration.GetSection(ConsulOptions.SectionName));

        services.TryAddSingleton<IServiceDiscovery, ConsulServiceDiscovery>();
        services.TryAddSingleton<IServiceRegistry, ConsulServiceDiscovery>();

        // 添加健康检查支持
        services.AddHealthCheck(configuration);

        return services;
    }

    /// <summary>
    /// 添加 Consul 服务发现 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services,
        Action<ConsulOptions> configureOptions)
    {
        services.Configure(configureOptions);

        services.TryAddSingleton<IServiceDiscovery, ConsulServiceDiscovery>();
        services.TryAddSingleton<IServiceRegistry, ConsulServiceDiscovery>();

        // 添加默认健康检查
        services.AddHealthCheck();

        return services;
    }

    /// <summary>
    /// 添加 Etcd 服务发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddEtcdServiceDiscovery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EtcdOptions>(configuration.GetSection(EtcdOptions.SectionName));

        services.TryAddSingleton<IServiceDiscovery, EtcdServiceDiscovery>();
        services.TryAddSingleton<IServiceRegistry, EtcdServiceDiscovery>();

        // 添加健康检查支持
        services.AddHealthCheck(configuration);

        return services;
    }

    /// <summary>
    /// 添加 Etcd 服务发现 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddEtcdServiceDiscovery(
        this IServiceCollection services,
        Action<EtcdOptions> configureOptions)
    {
        services.Configure(configureOptions);

        services.TryAddSingleton<IServiceDiscovery, EtcdServiceDiscovery>();
        services.TryAddSingleton<IServiceRegistry, EtcdServiceDiscovery>();

        // 添加默认健康检查
        services.AddHealthCheck();

        return services;
    }

    /// <summary>
    /// 添加 DNS 服务发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDnsServiceDiscovery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DnsOptions>(configuration.GetSection(DnsOptions.SectionName));

        services.TryAddSingleton<IServiceDiscovery, DnsServiceDiscovery>();

        // 添加健康检查支持
        services.AddHealthCheck(configuration);

        return services;
    }

    /// <summary>
    /// 添加 DNS 服务发现 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDnsServiceDiscovery(
        this IServiceCollection services,
        Action<DnsOptions> configureOptions)
    {
        services.Configure(configureOptions);

        services.TryAddSingleton<IServiceDiscovery, DnsServiceDiscovery>();

        // 添加默认健康检查
        services.AddHealthCheck();

        return services;
    }

    /// <summary>
    /// 添加静态服务发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureEndpoints">配置端点回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddStaticServiceDiscovery(
        this IServiceCollection services,
        Action<Dictionary<string, string[]>> configureEndpoints)
    {
        var endpoints = new Dictionary<string, string[]>();
        configureEndpoints(endpoints);

        services.TryAddSingleton<IServiceDiscovery>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<StaticServiceDiscovery>>();
            return new StaticServiceDiscovery(endpoints, logger);
        });

        // 添加默认健康检查
        services.AddHealthCheck();

        return services;
    }

    /// <summary>
    /// 添加健康检查服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHealthCheck(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.Configure<HealthCheckOptions>(configuration.GetSection(HealthCheckOptions.SectionName));
        }
        else
        {
            services.Configure<HealthCheckOptions>(options => { });
        }

        services.TryAddSingleton<IHealthChecker, HealthCheckService>();

        return services;
    }

    /// <summary>
    /// 添加健康检查服务 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHealthCheck(
        this IServiceCollection services,
        Action<HealthCheckOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.TryAddSingleton<IHealthChecker, HealthCheckService>();

        return services;
    }

    /// <summary>
    /// 添加自定义服务发现实现
    /// </summary>
    /// <typeparam name="TServiceDiscovery">服务发现实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCustomServiceDiscovery<TServiceDiscovery>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TServiceDiscovery : class, IServiceDiscovery
    {
        services.Add(new ServiceDescriptor(typeof(IServiceDiscovery), typeof(TServiceDiscovery), lifetime));

        // 添加默认健康检查
        services.AddHealthCheck();

        return services;
    }

    /// <summary>
    /// 添加自定义服务注册实现
    /// </summary>
    /// <typeparam name="TServiceRegistry">服务注册实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCustomServiceRegistry<TServiceRegistry>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TServiceRegistry : class, IServiceRegistry
    {
        services.Add(new ServiceDescriptor(typeof(IServiceRegistry), typeof(TServiceRegistry), lifetime));

        return services;
    }

    /// <summary>
    /// 添加自定义健康检查实现
    /// </summary>
    /// <typeparam name="THealthChecker">健康检查实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCustomHealthChecker<THealthChecker>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where THealthChecker : class, IHealthChecker
    {
        services.Add(new ServiceDescriptor(typeof(IHealthChecker), typeof(THealthChecker), lifetime));

        return services;
    }

    /// <summary>
    /// 添加服务发现提供者工厂
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceDiscoveryFactory(this IServiceCollection services)
    {
        services.TryAddSingleton<IServiceDiscoveryFactory, ServiceDiscoveryFactory>();
        return services;
    }
}

/// <summary>
/// 静态服务发现实现
/// </summary>
internal class StaticServiceDiscovery(
    Dictionary<string, string[]> endpoints,
    ILogger<StaticServiceDiscovery> logger)
    : IServiceDiscovery
{
    public Task<IReadOnlyList<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (!endpoints.TryGetValue(serviceName, out var endpointStrings))
        {
            logger.LogWarning("服务 {ServiceName} 未在静态配置中找到", serviceName);
            return Array.Empty<ServiceEndpoint>();
        }

        var endpoints1 = new List<ServiceEndpoint>();
        foreach (var endpointStr in endpointStrings)
        {
            if (TryParseEndpoint(endpointStr, serviceName, out var endpoint))
            {
                endpoints1.Add(endpoint);
            }
        }

        logger.LogDebug("从静态配置发现服务 {ServiceName}，端点数量: {Count}", serviceName, endpoints1.Count);
        return await Task.FromResult(endpoints1.AsReadOnly());
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverByTagsAsync(
        string serviceName,
        Dictionary<string, string> tags,
        CancellationToken cancellationToken = default)
    {
        // 静态服务发现不支持标签过滤
        return await DiscoverAsync(serviceName, cancellationToken);
    }

    public async IAsyncEnumerable<ServiceEndpoint[]> WatchAsync(
        string serviceName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 静态服务发现不支持监听
        var serviceEndpoints = await DiscoverAsync(serviceName, cancellationToken);
        yield return serviceEndpoints.ToArray();
    }

    private bool TryParseEndpoint(string endpointStr, string serviceName, out ServiceEndpoint endpoint)
    {
        endpoint = null!;

        try
        {
            if (!endpointStr.Contains(':'))
            {
                return false;
            }

            var parts = endpointStr.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            {
                return false;
            }

            endpoint = new ServiceEndpoint
            {
                ServiceId = $"{serviceName}-{endpointStr}",
                ServiceName = serviceName,
                EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(parts[0]), port)
            };

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "解析端点字符串 {EndpointStr} 失败", endpointStr);
            return false;
        }
    }
}

/// <summary>
/// 服务发现工厂接口
/// </summary>
public interface IServiceDiscoveryFactory
{
    /// <summary>
    /// 创建服务发现实例
    /// </summary>
    /// <param name="providerType">提供者类型</param>
    /// <returns>服务发现实例</returns>
    IServiceDiscovery CreateServiceDiscovery(ServiceDiscoveryProviderType providerType);

    /// <summary>
    /// 创建服务注册实例
    /// </summary>
    /// <param name="providerType">提供者类型</param>
    /// <returns>服务注册实例</returns>
    IServiceRegistry CreateServiceRegistry(ServiceDiscoveryProviderType providerType);
}

/// <summary>
/// 服务发现工厂实现
/// </summary>
internal class ServiceDiscoveryFactory : IServiceDiscoveryFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceDiscoveryFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IServiceDiscovery CreateServiceDiscovery(ServiceDiscoveryProviderType providerType)
    {
        return providerType switch
        {
            ServiceDiscoveryProviderType.Consul => CreateConsulServiceDiscovery(),
            ServiceDiscoveryProviderType.Etcd => CreateEtcdServiceDiscovery(),
            ServiceDiscoveryProviderType.Dns => CreateDnsServiceDiscovery(),
            ServiceDiscoveryProviderType.Static => CreateStaticServiceDiscovery(),
            _ => throw new ArgumentException($"不支持的服务发现提供者类型: {providerType}")
        };
    }

    public IServiceRegistry CreateServiceRegistry(ServiceDiscoveryProviderType providerType)
    {
        return providerType switch
        {
            ServiceDiscoveryProviderType.Consul => CreateConsulServiceRegistry(),
            ServiceDiscoveryProviderType.Etcd => CreateEtcdServiceRegistry(),
            _ => throw new ArgumentException($"不支持的服务注册提供者类型: {providerType}")
        };
    }

    private IServiceDiscovery CreateConsulServiceDiscovery()
    {
        var options = _serviceProvider.GetRequiredService<IOptions<ConsulOptions>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<ConsulServiceDiscovery>>();
        return new ConsulServiceDiscovery(options, logger);
    }

    private IServiceRegistry CreateConsulServiceRegistry()
    {
        var options = _serviceProvider.GetRequiredService<IOptions<ConsulOptions>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<ConsulServiceDiscovery>>();
        return new ConsulServiceDiscovery(options, logger);
    }

    private IServiceDiscovery CreateEtcdServiceDiscovery()
    {
        var options = _serviceProvider.GetRequiredService<IOptions<EtcdOptions>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<EtcdServiceDiscovery>>();
        return new EtcdServiceDiscovery(options, logger);
    }

    private IServiceRegistry CreateEtcdServiceRegistry()
    {
        var options = _serviceProvider.GetRequiredService<IOptions<EtcdOptions>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<EtcdServiceDiscovery>>();
        return new EtcdServiceDiscovery(options, logger);
    }

    private IServiceDiscovery CreateStaticServiceDiscovery()
    {
        // 静态服务发现需要预配置的端点，这里返回一个空的实现
        var logger = _serviceProvider.GetRequiredService<ILogger<StaticServiceDiscovery>>();
        return new StaticServiceDiscovery(new Dictionary<string, string[]>(), logger);
    }

    private IServiceDiscovery CreateDnsServiceDiscovery()
    {
        var options = _serviceProvider.GetRequiredService<IOptions<DnsOptions>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<DnsServiceDiscovery>>();
        return new DnsServiceDiscovery(options, logger);
    }
}

/// <summary>
/// 服务发现提供者类型
/// </summary>
public enum ServiceDiscoveryProviderType
{
    /// <summary>
    /// Consul
    /// </summary>
    Consul,

    /// <summary>
    /// Etcd
    /// </summary>
    Etcd,

    /// <summary>
    /// 静态配置
    /// </summary>
    Static,

    /// <summary>
    /// DNS
    /// </summary>
    Dns,

    /// <summary>
    /// 自定义
    /// </summary>
    Custom
}
