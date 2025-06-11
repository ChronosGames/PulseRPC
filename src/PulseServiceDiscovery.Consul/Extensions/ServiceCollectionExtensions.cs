using Consul;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseServiceDiscovery.Abstractions;

namespace PulseServiceDiscovery.Consul.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加Consul服务发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services,
        Action<ConsulOptions>? configureOptions = null)
    {
        // 配置Consul选项
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 注册Consul客户端
        services.TryAddSingleton<IConsulClient>(provider =>
        {
            var options = provider.GetService<Microsoft.Extensions.Options.IOptions<ConsulOptions>>()?.Value ?? new ConsulOptions();

            var consulConfig = new ConsulClientConfiguration
            {
                Address = new Uri(options.Endpoint)
            };

            if (!string.IsNullOrWhiteSpace(options.Datacenter))
            {
                consulConfig.Datacenter = options.Datacenter;
            }

            if (!string.IsNullOrWhiteSpace(options.Token))
            {
                consulConfig.Token = options.Token;
            }

            return new ConsulClient(consulConfig);
        });

        // 注册服务发现和注册器
        services.TryAddSingleton<ConsulServiceDiscovery>();
        services.TryAddSingleton<IServiceDiscovery>(provider => provider.GetRequiredService<ConsulServiceDiscovery>());
        services.TryAddSingleton<IServiceRegistry>(provider => provider.GetRequiredService<ConsulServiceDiscovery>());

        return services;
    }

    /// <summary>
    /// 配置Consul端点
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="endpoint">Consul端点</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulEndpoint(
        this IServiceCollection services,
        string endpoint)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with { Endpoint = endpoint };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul数据中心
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="datacenter">数据中心</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulDatacenter(
        this IServiceCollection services,
        string datacenter)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with { Datacenter = datacenter };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul认证令牌
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="token">认证令牌</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulToken(
        this IServiceCollection services,
        string token)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with { Token = token };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul健康检查
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureHealthCheck">健康检查配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulHealthCheck(
        this IServiceCollection services,
        Action<ConsulHealthCheckOptions> configureHealthCheck)
    {
        services.Configure<ConsulOptions>(options =>
        {
            var updatedHealthCheck = options.HealthCheck;
            configureHealthCheck(updatedHealthCheck);
            options = options with { HealthCheck = updatedHealthCheck };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul服务发现选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureDiscovery">发现选项配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulDiscovery(
        this IServiceCollection services,
        Action<ConsulDiscoveryOptions> configureDiscovery)
    {
        services.Configure<ConsulOptions>(options =>
        {
            var updatedDiscovery = options.DiscoveryOptions;
            configureDiscovery(updatedDiscovery);
            options = options with { DiscoveryOptions = updatedDiscovery };
        });

        return services;
    }

    /// <summary>
    /// 禁用Consul健康检查
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection DisableConsulHealthCheck(this IServiceCollection services)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                HealthCheck = options.HealthCheck with { Enabled = false }
            };
        });

        return services;
    }

    /// <summary>
    /// 启用包含不健康服务的发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection IncludeUnhealthyServices(this IServiceCollection services)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                DiscoveryOptions = options.DiscoveryOptions with { HealthyOnly = false }
            };
        });

        return services;
    }
}
