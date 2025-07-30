using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Client;

/// <summary>
/// 客户端依赖注入扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加PulseRPC客户端服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">客户端配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseClient(this IServiceCollection services, Action<IPulseClientBuilder> configure)
    {
        // 注册客户端构建器
        services.TryAddSingleton<IPulseClientBuilder, PulseClientBuilder>();

        // 使用配置构建客户端
        services.AddSingleton<IPulseClient>(provider =>
        {
            var builder = provider.GetRequiredService<IPulseClientBuilder>();
            configure(builder);
            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// 快速配置TCP客户端
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="name">通道名称</param>
    /// <param name="host">服务器主机</param>
    /// <param name="port">服务器端口</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseTcpClient(
        this IServiceCollection services,
        string name,
        string host,
        int port)
    {
        return services.AddPulseClient(builder => builder.AddTcp(name, host, port));
    }

    /// <summary>
    /// 快速配置KCP客户端
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="name">通道名称</param>
    /// <param name="host">服务器主机</param>
    /// <param name="port">服务器端口</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseKcpClient(
        this IServiceCollection services,
        string name,
        string host,
        int port)
    {
        return services.AddPulseClient(builder => builder.AddKcp(name, host, port));
    }
}

