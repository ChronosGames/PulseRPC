using Microsoft.Extensions.DependencyInjection;
using PulseRPC.ServiceDiscovery.Adapter.Services;
using PulseServiceDiscovery.Client.Extensions;
using PulseServiceDiscovery.Server.Extensions;

namespace PulseRPC.ServiceDiscovery.Adapter.Extensions;

/// <summary>
/// PulseRPC服务发现适配器扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 为PulseRPC客户端添加服务发现支持
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceDiscovery(
        this IServiceCollection services,
        Action<PulseServiceDiscovery.Client.Options.ClientOptions>? configureOptions = null)
    {
        // 添加服务发现客户端
        services.AddServiceDiscoveryClient(configureOptions);

        // 添加PulseRPC适配器服务
        services.AddSingleton<PulseRpcServiceDiscoveryAdapter>();
        services.AddSingleton<PulseRpcEndpointProvider>();

        return services;
    }

    /// <summary>
    /// 为PulseRPC服务端添加服务注册支持
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceRegistration(
        this IServiceCollection services,
        Action<PulseServiceDiscovery.Server.Options.ServerOptions>? configureOptions = null)
    {
        // 添加服务发现服务端
        services.AddServiceDiscoveryServer(configureOptions);

        // 添加PulseRPC适配器服务
        services.AddSingleton<PulseRpcServiceRegistrationAdapter>();

        return services;
    }

    /// <summary>
    /// 配置PulseRPC服务发现（同时支持客户端和服务端）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureClientOptions">客户端配置选项</param>
    /// <param name="configureServerOptions">服务端配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServiceDiscovery(
        this IServiceCollection services,
        Action<PulseServiceDiscovery.Client.Options.ClientOptions>? configureClientOptions = null,
        Action<PulseServiceDiscovery.Server.Options.ServerOptions>? configureServerOptions = null)
    {
        services.AddPulseRpcServiceDiscovery(configureClientOptions);
        services.AddPulseRpcServiceRegistration(configureServerOptions);

        return services;
    }

    /// <summary>
    /// 启用PulseRPC自动服务注册
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="host">服务主机</param>
    /// <param name="port">服务端口</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection EnablePulseRpcAutoRegistration(
        this IServiceCollection services,
        string serviceName,
        string host,
        int port)
    {
        services.Configure<PulseRpcAutoRegistrationOptions>(options =>
        {
            options.ServiceName = serviceName;
            options.Host = host;
            options.Port = port;
            options.Enabled = true;
        });

        services.AddHostedService<PulseRpcAutoRegistrationService>();

        return services;
    }

    /// <summary>
    /// 配置PulseRPC负载均衡策略
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="strategy">负载均衡策略</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UsePulseRpcLoadBalancing(
        this IServiceCollection services,
        PulseServiceDiscovery.Abstractions.Enums.LoadBalancingStrategy strategy)
    {
        services.Configure<PulseServiceDiscovery.Client.Options.ClientOptions>(options =>
        {
            options.LoadBalancing.Strategy = strategy;
        });

        return services;
    }
}
