using dotnet_etcd;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 集群路由服务注册扩展
/// </summary>
public static class ClusterRoutingServiceCollectionExtensions
{
    /// <summary>
    /// 添加集群路由服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddClusterRouting(
        this IServiceCollection services,
        Action<ClusterRoutingOptions> configureOptions)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureOptions == null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        // 注册配置
        services.Configure(configureOptions);

        // 注册Etcd客户端
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ClusterRoutingOptions>>().Value;
            return new EtcdClient(string.Join(",", options.EtcdEndpoints));
        });

        // 注册核心服务
        services.AddSingleton<ServiceRouter>();
        services.AddSingleton<IServiceRouter>(sp => sp.GetRequiredService<ServiceRouter>());
        services.AddSingleton<NodeChangeHandler>();
        services.AddSingleton<ServiceLifecycleManager>();

        // 注册后台服务
        services.AddHostedService<FixedMappingCleanupService>();

        // 注册初始化托管服务
        services.AddHostedService<ClusterRoutingInitializationService>();

        return services;
    }

    /// <summary>
    /// 添加优雅关闭支持
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项（可选）</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddGracefulShutdown(
        this IServiceCollection services,
        Action<GracefulShutdownOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // 注册配置
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<GracefulShutdownOptions>(_ => { });
        }

        // 注册优雅关闭协调器
        services.AddSingleton<GracefulShutdownCoordinator>();
        services.AddSingleton<IGracefulShutdownCoordinator>(sp =>
            sp.GetRequiredService<GracefulShutdownCoordinator>());

        // 注册托管服务（监听应用停止事件）
        services.AddHostedService<GracefulShutdownHostedService>();

        // 注册健康检查
        services.AddHealthChecks()
            .AddCheck<GracefulShutdownHealthCheck>(
                "graceful-shutdown",
                tags: new[] { "ready", "live" });

        return services;
    }

    /// <summary>
    /// 添加集群路由服务（使用默认配置）
    /// </summary>
    public static IServiceCollection AddClusterRouting(
        this IServiceCollection services,
        ushort nodeId,
        params string[] etcdEndpoints)
    {
        return services.AddClusterRouting(options =>
        {
            options.NodeId = nodeId;
            options.EtcdEndpoints = etcdEndpoints.Length > 0
                ? etcdEndpoints
                : new[] { "http://localhost:2379" };
        });
    }
}

/// <summary>
/// 集群路由初始化托管服务
/// 在应用启动时初始化ServiceRouter
/// </summary>
internal class ClusterRoutingInitializationService : IHostedService
{
    private readonly ServiceRouter _router;
    private readonly ILogger<ClusterRoutingInitializationService> _logger;

    public ClusterRoutingInitializationService(
        ServiceRouter router,
        ILogger<ClusterRoutingInitializationService> logger)
    {
        _router = router;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始初始化集群路由服务...");

        try
        {
            await _router.InitializeAsync(cancellationToken);
            _logger.LogInformation("集群路由服务初始化成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "集群路由服务初始化失败");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("停止集群路由服务...");
        await _router.DisposeAsync();
        _logger.LogInformation("集群路由服务已停止");
    }
}
