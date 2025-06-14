using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PulseRPC.HealthCheck;

namespace PulseRPC.HealthCheck;

public static class HealthCheckExtensions
{
    /// <summary>
    /// 添加清理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCleanupService(this IServiceCollection services)
    {
        services.TryAddSingleton<CleanupService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(provider =>
            provider.GetRequiredService<CleanupService>()));

        return services;
    }
}
