using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseServiceDiscovery.Client.LoadBalancing;

namespace PulseRPC.LoadBalancing;

public static class LoadBalancingExtensions
{
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

    /// <summary>
    /// 添加负载均衡
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="strategy">负载均衡策略</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddLoadBalancing(this IServiceCollection services,
        LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin)
    {
        return strategy switch
        {
            LoadBalancingStrategy.RoundRobin => services.AddLoadBalancing<RoundRobinLoadBalancer>(),
            _ => throw new NotSupportedException($"负载均衡策略 {strategy} 尚未实现")
        };
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
    /// 添加自定义负载均衡实现
    /// </summary>
    /// <typeparam name="TImplementation">实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddLoadBalancing<TImplementation>(this IServiceCollection services)
        where TImplementation : class, ILoadBalancer
    {
        services.TryAddSingleton<ILoadBalancer, TImplementation>();

        return services;
    }

    /// <summary>
    /// 添加自定义负载均衡实现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="implementationFactory">实现工厂</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddLoadBalancing(this IServiceCollection services,
        Func<IServiceProvider, ILoadBalancer> implementationFactory)
    {
        services.RemoveAll<ILoadBalancer>();
        services.AddSingleton(implementationFactory);

        return services;
    }
}
