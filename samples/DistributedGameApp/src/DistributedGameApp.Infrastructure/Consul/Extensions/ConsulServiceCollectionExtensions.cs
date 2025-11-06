using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DistributedGameApp.Infrastructure.Consul.Extensions;

/// <summary>
/// Consul 依赖注入扩展
/// </summary>
public static class ConsulServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Consul 服务
    /// </summary>
    public static IServiceCollection AddConsul(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 配置选项
        services.Configure<ConsulOptions>(
            configuration.GetSection(ConsulOptions.SectionName));

        // 注册服务
        services.AddSingleton<ConsulServiceRegistry>();
        services.AddSingleton<ConsulServiceDiscovery>();

        return services;
    }

    /// <summary>
    /// 添加 Consul 服务（使用自定义配置）
    /// </summary>
    public static IServiceCollection AddConsul(
        this IServiceCollection services,
        Action<ConsulOptions> configureOptions)
    {
        // 配置选项
        services.Configure(configureOptions);

        // 注册服务
        services.AddSingleton<ConsulServiceRegistry>();
        services.AddSingleton<ConsulServiceDiscovery>();

        return services;
    }
}
