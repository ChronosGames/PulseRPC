using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Enums;
using PulseServiceDiscovery.Client.Caching;
using PulseServiceDiscovery.Client.HealthCheck;
using PulseServiceDiscovery.Client.LoadBalancing;
using PulseServiceDiscovery.Client.Options;

namespace PulseServiceDiscovery.Client.Extensions;

/// <summary>
/// ServiceCollection 扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加服务发现客户端
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceDiscoveryClient(
        this IServiceCollection services,
        Action<ClientOptions>? configureOptions = null)
    {
        // 配置选项
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 注册核心服务
        services.TryAddSingleton<ServiceDiscoveryClient>();
        services.TryAddSingleton<IServiceDiscovery>(provider => provider.GetRequiredService<ServiceDiscoveryClient>());

        // 注册健康检查
        services.TryAddSingleton<IHealthChecker, HealthChecker>();
        services.TryAddSingleton<HealthCheckService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(provider =>
            provider.GetRequiredService<HealthCheckService>()));

        // 注册内存缓存
        services.TryAddSingleton<IMemoryCache, MemoryCache>();

        // 默认注册内存缓存实现
        services.TryAddSingleton<IServiceCache, MemoryServiceCache>();

        // 注册负载均衡器
        RegisterLoadBalancers(services);

        return services;
    }

    /// <summary>
    /// 使用内存缓存
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseMemoryCache(this IServiceCollection services)
    {
        services.TryAddSingleton<IMemoryCache, MemoryCache>();
        services.Replace(ServiceDescriptor.Singleton<IServiceCache, MemoryServiceCache>());
        return services;
    }

    /// <summary>
    /// 使用分布式缓存
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseDistributedCache(this IServiceCollection services)
    {
        // 确保有分布式缓存实现
        services.TryAddSingleton<IDistributedCache, MemoryDistributedCache>();
        services.Replace(ServiceDescriptor.Singleton<IServiceCache, DistributedServiceCache>());
        return services;
    }

    /// <summary>
    /// 配置负载均衡策略
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="defaultStrategy">默认负载均衡策略</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureLoadBalancing(
        this IServiceCollection services,
        LoadBalancingStrategy defaultStrategy = LoadBalancingStrategy.RoundRobin)
    {
        services.Configure<ClientOptions>(options =>
        {
            options.LoadBalancingOptions.Strategy = defaultStrategy;
        });

        return services;
    }

    /// <summary>
    /// 配置缓存选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureCache">缓存配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureCache(
        this IServiceCollection services,
        Action<CacheOptions> configureCache)
    {
        services.Configure<ClientOptions>(options =>
        {
            configureCache(options.CacheOptions);
        });

        return services;
    }

    /// <summary>
    /// 配置健康检查选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureHealthCheck">健康检查配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureHealthCheck(
        this IServiceCollection services,
        Action<HealthCheckOptions> configureHealthCheck)
    {
        services.Configure<ClientOptions>(options =>
        {
            configureHealthCheck(options.HealthCheckOptions);
        });

        return services;
    }

    /// <summary>
    /// 添加自定义负载均衡器
    /// </summary>
    /// <typeparam name="TLoadBalancer">负载均衡器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="strategy">负载均衡策略</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddLoadBalancer<TLoadBalancer>(
        this IServiceCollection services,
        LoadBalancingStrategy strategy)
        where TLoadBalancer : class, ILoadBalancer
    {
        services.AddKeyedSingleton<ILoadBalancer, TLoadBalancer>(strategy);
        return services;
    }

    /// <summary>
    /// 添加自定义缓存实现
    /// </summary>
    /// <typeparam name="TCache">缓存实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceCache<TCache>(this IServiceCollection services)
        where TCache : class, IServiceCache
    {
        services.Replace(ServiceDescriptor.Singleton<IServiceCache, TCache>());
        return services;
    }

    /// <summary>
    /// 添加自定义健康检查器
    /// </summary>
    /// <typeparam name="THealthChecker">健康检查器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHealthChecker<THealthChecker>(this IServiceCollection services)
        where THealthChecker : class, IHealthChecker
    {
        services.Replace(ServiceDescriptor.Singleton<IHealthChecker, THealthChecker>());
        return services;
    }

    /// <summary>
    /// 禁用健康检查
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection DisableHealthCheck(this IServiceCollection services)
    {
        services.Configure<ClientOptions>(options =>
        {
            options.HealthCheckOptions.Enabled = false;
        });

        return services;
    }

    /// <summary>
    /// 注册所有内置的负载均衡器
    /// </summary>
    /// <param name="services">服务集合</param>
    private static void RegisterLoadBalancers(IServiceCollection services)
    {
        // 注册所有内置负载均衡器
        services.AddKeyedSingleton<ILoadBalancer, RoundRobinLoadBalancer>(LoadBalancingStrategy.RoundRobin);
        services.AddKeyedSingleton<ILoadBalancer, RandomLoadBalancer>(LoadBalancingStrategy.Random);
        services.AddKeyedSingleton<ILoadBalancer, WeightedRoundRobinLoadBalancer>(LoadBalancingStrategy.WeightedRoundRobin);
        services.AddKeyedSingleton<ILoadBalancer, LeastConnectionsLoadBalancer>(LoadBalancingStrategy.LeastConnections);

        // 注册负载均衡器工厂
        services.TryAddSingleton<ILoadBalancerFactory, LoadBalancerFactory>();
    }

    /// <summary>
    /// 获取负载均衡器
    /// </summary>
    /// <param name="provider">服务提供程序</param>
    /// <param name="strategy">负载均衡策略</param>
    /// <returns>负载均衡器</returns>
    public static ILoadBalancer GetLoadBalancer(this IServiceProvider provider, LoadBalancingStrategy strategy)
    {
        return provider.GetRequiredKeyedService<ILoadBalancer>(strategy);
    }
}

/// <summary>
/// 负载均衡器工厂接口
/// </summary>
public interface ILoadBalancerFactory
{
    /// <summary>
    /// 获取负载均衡器
    /// </summary>
    /// <param name="strategy">负载均衡策略</param>
    /// <returns>负载均衡器</returns>
    ILoadBalancer GetLoadBalancer(LoadBalancingStrategy strategy);
}

/// <summary>
/// 负载均衡器工厂实现
/// </summary>
public class LoadBalancerFactory : ILoadBalancerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LoadBalancerFactory> _logger;

    public LoadBalancerFactory(IServiceProvider serviceProvider, ILogger<LoadBalancerFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ILoadBalancer GetLoadBalancer(LoadBalancingStrategy strategy)
    {
        try
        {
            var loadBalancer = _serviceProvider.GetRequiredKeyedService<ILoadBalancer>(strategy);
            _logger.LogDebug("Retrieved load balancer for strategy: {Strategy}", strategy);
            return loadBalancer;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to get load balancer for strategy: {Strategy}", strategy);

            // 回退到轮询策略
            _logger.LogWarning("Falling back to RoundRobin load balancer");
            return _serviceProvider.GetRequiredKeyedService<ILoadBalancer>(LoadBalancingStrategy.RoundRobin);
        }
    }
}
