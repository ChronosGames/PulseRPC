using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Infrastructure;

namespace PulseRPC.ServiceDiscovery;

public static class ServiceDiscoveryExtensions
{
    public static IServiceCollection AddServiceDiscovery(this IServiceCollection services,
        Action<ServiceDiscoveryOptions> configureOptions)
    {
        // 检查是否已经配置了服务发现
        services.Configure(configureOptions);

        return services;
    }
}
