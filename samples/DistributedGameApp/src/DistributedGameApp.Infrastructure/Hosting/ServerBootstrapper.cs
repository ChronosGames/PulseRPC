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

    /// <summary>
    /// 启用服务端推送（IHubContext&lt;TReceiver&gt;）（默认: true）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 当启用时，会自动检测并配置推送服务：
    /// </para>
    /// <list type="bullet">
    /// <item>优先使用 External 服务器（面向客户端推送）</item>
    /// <item>如果没有 External，则使用 Internal 服务器（服务器间推送）</item>
    /// </list>
    /// <para>
    /// 自动完成的工作：
    /// </para>
    /// <list type="number">
    /// <item>桥接 IServerChannelManager 到非 keyed 版本</item>
    /// <item>注册 IUserConnectionMapping 和 IGroupManager</item>
    /// <item>注册所有 IPulseReceiver 的 IHubContext</item>
    /// </list>
    /// </remarks>
    public bool EnablePushServices { get; set; } = true;

    /// <summary>
    /// 启用 HTTP 端点（用于健康检查和 Prometheus metrics）（默认: true）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 当启用时，会启动内嵌的 Kestrel HTTP 服务器，暴露以下端点：
    /// </para>
    /// <list type="bullet">
    /// <item>/health - 健康检查端点（供 Consul HTTP 模式使用）</item>
    /// <item>/metrics - Prometheus metrics 端点</item>
    /// <item>/ready - 就绪检查端点</item>
    /// </list>
    /// </remarks>
    public bool EnableHttpEndpoint { get; set; } = true;

    /// <summary>
    /// 启用 Prometheus metrics（默认: true）
    /// </summary>
    /// <remarks>
    /// 需要 EnableHttpEndpoint = true 才能生效
    /// </remarks>
    public bool EnablePrometheusMetrics { get; set; } = true;
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

        // 4.6 HTTP 端点（用于健康检查和 Prometheus metrics）
        if (options.EnableHttpEndpoint)
        {
            services.Configure<HttpEndpointOptions>(configuration.GetSection(HttpEndpointOptions.SectionName));
            services.AddHostedService<HealthEndpointHostedService>();
        }

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
        string? primaryPushServer = null;

        // 内网RPC通道（统一必选）
        if (options.EnableInternalRpcChannel)
        {
            var internalConfig = networkConfig.GetSection("Internal");
            if (internalConfig.Exists() && internalConfig.GetValue<bool>("Enabled"))
            {
                services.AddNamedPulseServer("Internal", internalConfig);
                // Internal 作为备选推送服务器
                primaryPushServer ??= "Internal";
            }
        }

        // 外网监听（按需启用）
        if (options.EnableExternalListener)
        {
            var externalConfig = networkConfig.GetSection("External");
            if (externalConfig.Exists() && externalConfig.GetValue<bool>("Enabled"))
            {
                services.AddNamedPulseServer("External", externalConfig);
                // External 优先作为推送服务器（面向客户端）
                primaryPushServer = "External";
            }
        }

        // 自动配置服务端推送（IHubContext<TReceiver>）
        if (options.EnablePushServices && !string.IsNullOrEmpty(primaryPushServer))
        {
            ConfigurePushServices(services, primaryPushServer);
        }
    }

    /// <summary>
    /// 配置服务端推送服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="serverName">作为推送源的服务器名称</param>
    /// <remarks>
    /// 此方法执行以下操作：
    /// <list type="number">
    /// <item>桥接指定服务器的 IServerChannelManager 到非 keyed 版本</item>
    /// <item>注册 IUserConnectionMapping 和 IGroupManager</item>
    /// <item>注册所有 IPulseReceiver 的 IHubContext（通过源生成器）</item>
    /// </list>
    /// </remarks>
    private static void ConfigurePushServices(IServiceCollection services, string serverName)
    {
        // 1. 桥接 keyed IServerChannelManager 到非 keyed 版本
        // IHubContext<TReceiver> 生成的代码需要非 keyed 的 IServerChannelManager
        services.AddSingleton<PulseRPC.Server.Transport.IServerChannelManager>(sp =>
            sp.GetRequiredKeyedService<PulseRPC.Server.Transport.IServerChannelManager>(serverName));

        // 2. 注册 IUserConnectionMapping 和 IGroupManager（IHubContext 依赖）
        services.AddPulseReceiverServices();

        // 3. 注册所有 IPulseReceiver 的 IHubContext（源生成器生成的代码）
        // 注意：这里使用反射调用，因为 AddAllPulseReceiverContexts 是源生成器生成的
        // 如果源生成器没有生成这个方法，则静默跳过
        TryRegisterAllPulseReceiverContexts(services);
    }

    /// <summary>
    /// 尝试注册所有 IPulseReceiver 的 IHubContext
    /// </summary>
    private static void TryRegisterAllPulseReceiverContexts(IServiceCollection services)
    {
        // 查找源生成器生成的 PulseReceiverServiceExtensions 类
        var extensionType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.FullName == "PulseRPC.Server.Extensions.PulseReceiverServiceExtensions");

        if (extensionType == null)
            return;

        // 查找 AddAllPulseReceiverContexts 方法
        var method = extensionType.GetMethod(
            "AddAllPulseReceiverContexts",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            new[] { typeof(IServiceCollection) },
            null);

        // 调用扩展方法
        method?.Invoke(null, new object[] { services });
    }
}
