using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.ServiceDiscovery;

namespace PulseRPC.ServiceDiscovery.Consul.Extensions;

/// <summary>
/// Consul 服务集合扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Consul 服务发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulServiceDiscovery(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConsulOptions>(configuration.GetSection(ConsulOptions.SectionName));
        services.RemoveAll<IServiceDiscovery>();
        services.RemoveAll<IServiceRegistry>();
        services.AddSingleton<ConsulServiceDiscovery>();
        services.AddSingleton<IServiceDiscovery>(provider => provider.GetRequiredService<ConsulServiceDiscovery>());
        services.AddSingleton<IServiceRegistry>(provider => provider.GetRequiredService<ConsulServiceDiscovery>());

        return services;
    }

    /// <summary>
    /// 添加 Consul 服务发现（使用配置委托）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置委托</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulServiceDiscovery(this IServiceCollection services, 
        Action<ConsulOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.RemoveAll<IServiceDiscovery>();
        services.RemoveAll<IServiceRegistry>();
        services.AddSingleton<ConsulServiceDiscovery>();
        services.AddSingleton<IServiceDiscovery>(provider => provider.GetRequiredService<ConsulServiceDiscovery>());
        services.AddSingleton<IServiceRegistry>(provider => provider.GetRequiredService<ConsulServiceDiscovery>());

        return services;
    }
} 