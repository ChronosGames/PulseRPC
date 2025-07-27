using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Infrastructure;

namespace PulseRPC.ServiceDiscovery;

public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// 添加服务发现功能
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置委托</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceDiscovery(
        this IServiceCollection services,
        Action<ServiceDiscoveryOptions>? configure = null)
    {
        var options = new ServiceDiscoveryOptions();
        configure?.Invoke(options);

        services.Configure(configure ?? (_ => { }));
        services.TryAddSingleton<IServiceDiscovery, ServiceDiscovery>();

        return services;
    }
}
