using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PulseRPC.HealthCheck;
using PulseRPC.ServiceRegistration;

namespace PulseRPC.HealthCheck;

public static class HealthCheckExtensions
{
    /// <summary>
    /// 添加PulseRPC 健康检测（可选）
    /// </summary>
    /// <param name="services"></param>
    /// <typeparam name="TImplementation"></typeparam>
    /// <returns></returns>
    public static IServiceCollection AddPulseRpcHealthCheck<TImplementation>(this IServiceCollection services)
        where TImplementation : class, IHealthChecker
    {
        services.TryAddSingleton<IHealthChecker, TImplementation>();

        return services;
    }

    /// <summary>
    /// 添加自定义 PulseRPC 健康检测实现
    /// </summary>
    /// <param name="services"></param>
    /// <param name="implementationFactory"></param>
    /// <returns></returns>
    public static IServiceCollection AddPulseRpcHealthCheck(this IServiceCollection services,
        Func<IServiceProvider, IHealthChecker> implementationFactory)
    {
        services.RemoveAll<IHealthChecker>();
        services.AddSingleton(implementationFactory);

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 健康检测
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddPulseRpcHealthCheck(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册配置选项
        services.Configure<HealthCheckOptions>(configuration.GetSection("PulseRPC:HealthCheck"));
        return AddHealthCheckCore(services);
    }

    /// <summary>
    /// 添加 PulseRPC 健康检测 (使用配置回调)
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configureOptions"></param>
    /// <returns></returns>
    public static IServiceCollection AddPulseRpcHealthCheck(
        this IServiceCollection services,
        Action<HealthCheckOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return AddHealthCheckCore(services);
    }

    /// <summary>
    /// 添加 PulseRPC 健康检测 (使用配置回调)
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configureOptions"></param>
    /// <returns></returns>
    public static IServiceCollection AddHealthCheck(
        this IServiceCollection services,
        Action<HealthCheckOptions> configureOptions)
    {
        // 注册配置选项
        services.Configure(configureOptions);
        return AddHealthCheckCore(services);
    }

    private static IServiceCollection AddHealthCheckCore(IServiceCollection services)
    {
        // 添加核心服务
        services.TryAddSingleton<HealthCheckerService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HealthCheckerService>(provider =>
            provider.GetRequiredService<HealthCheckerService>()));

        // 添加健康检查注册器
        services.TryAddSingleton<ServiceRegistrar>();

        // 添加健康检查服务
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ServiceRegistrar>(provider =>
            provider.GetRequiredService<ServiceRegistrar>()));

        return services;
    }

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
