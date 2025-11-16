using DistributedGameApp.Infrastructure.Consul;
using DistributedGameApp.Infrastructure.Consul.Extensions;
using DistributedGameApp.Infrastructure.Health;
using DistributedGameApp.Infrastructure.Hosting.Bootstrap;
using DistributedGameApp.Infrastructure.MongoDB.Extensions;
using DistributedGameApp.Infrastructure.Sentry.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server.Extensions;

namespace DistributedGameApp.Infrastructure.Hosting;

/// <summary>
/// 服务器启动配置选项
/// </summary>
public sealed class ServerBootstrapperOptions
{
    /// <summary>
    /// 启用外网监听（用于客户端连接）
    /// </summary>
    public bool EnableExternalListener { get; set; } = false;

    /// <summary>
    /// 启用内网RPC通道（用于服务器间通信）
    /// </summary>
    public bool EnableInternalRpcChannel { get; set; } = true;

    /// <summary>
    /// 启用Consul服务注册与发现
    /// </summary>
    public bool EnableServiceDiscovery { get; set; } = true;

    /// <summary>
    /// 启用MongoDB数据库
    /// </summary>
    public bool EnableMongoDb { get; set; } = false;

    /// <summary>
    /// 启用Sentry错误追踪
    /// </summary>
    public bool EnableSentry { get; set; } = false;

    /// <summary>
    /// 配置应用特定服务
    /// </summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>
    /// 启用统一启动流程编排器（默认: true）
    /// </summary>
    public bool EnableBootstrapOrchestrator { get; set; } = true;
}

/// <summary>
/// PulseRPC服务器引导程序（统一启动流程）
/// </summary>
public static class ServerBootstrapper
{
    /// <summary>
    /// 添加PulseRPC服务器
    /// </summary>
    public static IServiceCollection AddPulseRpcServer(
        this IServiceCollection services,
        IConfiguration configuration,
        ServerBootstrapperOptions options)
    {
        // 1. 读取服务器标识
        var identity = configuration.GetSection("ServerIdentity").Get<ServerIdentityOptions>()
            ?? throw new InvalidOperationException("ServerIdentity configuration is missing.");

        services.AddSingleton(identity);

        // 2. Sentry（可选）
        if (options.EnableSentry && configuration.GetValue<bool>("Sentry:Enabled"))
        {
            services.AddSentryLogging(configuration);
        }

        // 3. 配置PulseRPC服务器
        ConfigurePulseServer(services, configuration, options);

        // 4. 基础设施
        if (options.EnableMongoDb)
        {
            services.AddMongoDb(configuration);
        }

        // 4.5 健康检查（在 Consul 注册之前注册）
        services.AddSingleton<IHealthCheckProvider, ServerHealthCheckProvider>();

        if (options.EnableServiceDiscovery)
        {
            services.AddConsul(configuration);

            // 如果使用统一启动流程编排器，则不单独注册 ConsulServiceRegistrationService
            if (!options.EnableBootstrapOrchestrator)
            {
                services.AddHostedService<ConsulServiceRegistrationService>();
            }
        }

        // 5. 应用特定服务
        options.ConfigureServices?.Invoke(services);

        // 6. 注册启动流程托管服务
        if (options.EnableBootstrapOrchestrator)
        {
            // 使用统一的启动流程编排器
            services.AddHostedService<ServerBootstrapOrchestrator>();
        }
        else
        {
            // 使用原来的分散启动方式
            services.AddHostedService<NamedPulseServersHostedService>();
        }

        return services;
    }

    private static void ConfigurePulseServer(
        IServiceCollection services,
        IConfiguration configuration,
        ServerBootstrapperOptions options)
    {
        var networkConfig = configuration.GetSection("Network");

        // 内网RPC通道（统一必选）
        if (options.EnableInternalRpcChannel)
        {
            var internalConfig = networkConfig.GetSection("Internal");
            if (internalConfig.Exists() && internalConfig.GetValue<bool>("Enabled"))
            {
                services.AddNamedPulseServer("Internal", internalConfig);
            }
        }

        // 外网监听（按需启用）
        if (options.EnableExternalListener)
        {
            var externalConfig = networkConfig.GetSection("External");
            if (externalConfig.Exists() && externalConfig.GetValue<bool>("Enabled"))
            {
                services.AddNamedPulseServer("External", externalConfig);
            }
        }
    }
}
