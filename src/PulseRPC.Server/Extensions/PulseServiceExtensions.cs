using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;
using PulseRPC.Server.Services.Management;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// 统一服务系统的 DI 扩展方法
/// </summary>
/// <remarks>
/// <para><strong>架构说明</strong>：</para>
/// <list type="bullet">
/// <item><description><c>IPulseHub</c> - 无状态的 RPC 接口契约</description></item>
/// <item><description><c>IPulseService</c> - 有状态的服务实例</description></item>
/// <item><description>Hub 与 Service 分离，Hub 通过 <c>IServiceAccessor&lt;T&gt;</c> 访问 Service</description></item>
/// </list>
/// <para><strong>注册示例</strong>：</para>
/// <code>
/// // 注册有状态服务
/// services.AddPulseService&lt;PlayerService&gt;((sp, id) => new PlayerService(id, ...));
///
/// // 注册无状态 Hub（标准 DI）
/// services.AddTransient&lt;IPlayerHub, PlayerHub&gt;();
/// </code>
/// </remarks>
public static class PulseServiceExtensions
{
    /// <summary>
    /// 添加统一服务管理系统
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置委托</param>
    /// <returns>服务集合，支持链式调用</returns>
    public static IServiceCollection AddPulseServiceManagement(
        this IServiceCollection services,
        Action<PulseServiceManagerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PulseServiceManagerOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<PulseServiceManager>(serviceProvider =>
        {
            var manager = new PulseServiceManager(
                serviceProvider,
                serviceProvider.GetRequiredService<ILogger<PulseServiceManager>>(),
                serviceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<PulseServiceManagerOptions>>()
                    .Value);

            foreach (var registration in serviceProvider.GetServices<ServiceRegistrationEntry>())
            {
                registration.RegisterAction(manager);
            }

            return manager;
        });

        // 添加后台服务以自动启动 AutoStart 服务
        services.AddHostedService<PulseServiceManagerHostedService>();

        // 注册图不依赖配置委托的调用顺序；禁用时 Evictor.StartAsync 直接返回。
        services.AddHostedService<ServiceInstanceEvictor>();

        return services;
    }

    /// <summary>
    /// 注册有状态的 PulseService
    /// </summary>
    /// <typeparam name="TService">服务实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="factory">自定义工厂（可选）</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para>
    /// 此方法会自动完成以下注册：
    /// </para>
    /// <list type="number">
    /// <item><description>注册统一服务管理系统（如果尚未注册）</description></item>
    /// <item><description>注册服务工厂到 PulseServiceManager</description></item>
    /// <item><description>注册 IServiceAccessor&lt;TService&gt; 访问器</description></item>
    /// </list>
    /// <para>
    /// <strong>注意</strong>：Hub 需要单独注册（使用标准 DI），例如：
    /// </para>
    /// <code>
    /// services.AddTransient&lt;IPlayerHub, PlayerHub&gt;();
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册有状态服务
    /// services.AddPulseService&lt;PlayerService&gt;((sp, playerId) =>
    ///     new PlayerService(playerId, sp.GetRequiredService&lt;ILogger&lt;PlayerService&gt;&gt;()));
    ///
    /// // 注册无状态 Hub
    /// services.AddTransient&lt;IPlayerHub, PlayerHub&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseService<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, string, TService>? factory = null)
        where TService : class, IPulseService
    {
        // 1. 确保统一服务管理系统已注册
        services.AddPulseServiceManagement();

        // 2. 只注册受管理的访问器。TService 不直接暴露给 DI，避免创建绕过
        // PulseServiceManager 的未启动、未缓存、无租约平行实例。
        services.TryAddSingleton<IServiceAccessor<TService>, ServiceAccessor<TService>>();

        // 3. 注册内部 catalog；Manager 首次解析时立即完成类型注册，
        // 不要求宿主先启动 HostedService。
        services.AddSingleton(new ServiceRegistrationEntry
        {
            RegisterAction = manager => manager.Register<TService>(factory)
        });

        return services;
    }
}

/// <summary>
/// 服务注册条目
/// </summary>
internal sealed class ServiceRegistrationEntry
{
    /// <summary>
    /// 注册委托 - 直接调用 PulseServiceManager.Register，避免反射
    /// </summary>
    public required Action<PulseServiceManager> RegisterAction { get; init; }
}

/// <summary>
/// 统一服务的 HostedService - 负责启动 AutoStart 服务
/// </summary>
internal sealed class PulseServiceManagerHostedService : IHostedService
{
    private readonly PulseServiceManager _serviceManager;
    private readonly ILogger<PulseServiceManagerHostedService> _logger;

    public PulseServiceManagerHostedService(
        PulseServiceManager serviceManager,
        ILogger<PulseServiceManagerHostedService> logger)
    {
        _serviceManager = serviceManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting PulseServiceManagerHostedService");

        // 启动所有 AutoStart 服务
        await _serviceManager.StartAutoStartServicesAsync(cancellationToken);

        _logger.LogInformation("PulseServiceManagerHostedService started");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping PulseServiceManagerHostedService");
        // IHostedService stop order depends on registration order. Disposing the manager here can
        // invalidate IServiceAccessor while a Pulse server registered earlier is still draining
        // requests. The DI provider owns the singleton and disposes it after every hosted service
        // has stopped.
        _logger.LogInformation(
            "PulseServiceManagerHostedService stopped; manager disposal is deferred to the service provider");
        return Task.CompletedTask;
    }
}
