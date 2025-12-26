using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.ServiceManagement;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// 统一服务系统的 DI 扩展方法
/// </summary>
/// <remarks>
/// <para><strong>架构说明</strong>：</para>
/// <list type="bullet">
/// <item><description><c>IPulseHub</c> - 无状态的 RPC 接口契约</description></item>
/// <item><description><c>IUnifiedPulseService</c> - 有状态的服务实例</description></item>
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
public static class UnifiedServiceExtensions
{
    /// <summary>
    /// 添加统一服务管理系统
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置委托</param>
    /// <returns>服务集合，支持链式调用</returns>
    public static IServiceCollection AddUnifiedServiceManagement(
        this IServiceCollection services,
        Action<UnifiedServiceManagerOptions>? configure = null)
    {
        var options = new UnifiedServiceManagerOptions();
        configure?.Invoke(options);

        // 使用 IOptions 模式注册配置
        services.Configure<UnifiedServiceManagerOptions>(opt =>
        {
            opt.ContinueOnAutoStartFailure = options.ContinueOnAutoStartFailure;
            opt.CleanupInterval = options.CleanupInterval;
            opt.MaxCachedInstances = options.MaxCachedInstances;
            opt.EnableInstanceEviction = options.EnableInstanceEviction;
        });

        services.TryAddSingleton(options);
        services.TryAddSingleton<UnifiedServiceManager>();

        // 添加后台服务以自动启动 AutoStart 服务
        services.AddHostedService<UnifiedServiceHostedService>();

        // 添加服务实例清理器（如果启用）
        if (options.EnableInstanceEviction)
        {
            services.AddHostedService<ServiceInstanceEvictor>();
        }

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
    /// <item><description>注册服务工厂到 UnifiedServiceManager</description></item>
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
        where TService : class, IUnifiedPulseService
    {
        // 1. 确保统一服务管理系统已注册
        services.AddUnifiedServiceManagement();

        // 2. 注册服务类型本身（用于 DI 创建）
        services.TryAddTransient<TService>();

        // 3. 注册服务访问器
        services.TryAddSingleton<IServiceAccessor<TService>, ServiceAccessor<TService>>();

        // 4. 注册到服务管理器的配置
        services.Configure<ServiceRegistrationCollection>(collection =>
        {
            collection.Add(new ServiceRegistrationEntry
            {
                ServiceType = typeof(TService),
                Factory = factory != null
                    ? (sp, id) => factory(sp, id)
                    : null,
                RegisterAction = manager => manager.Register<TService>(factory)
            });
        });

        return services;
    }
}

/// <summary>
/// 服务注册条目
/// </summary>
internal sealed class ServiceRegistrationEntry
{
    public required Type ServiceType { get; init; }
    public Func<IServiceProvider, string, IUnifiedPulseService>? Factory { get; init; }

    /// <summary>
    /// 注册委托 - 直接调用 UnifiedServiceManager.Register，避免反射
    /// </summary>
    public required Action<UnifiedServiceManager> RegisterAction { get; init; }
}

/// <summary>
/// 服务注册集合（用于配置阶段收集注册信息）
/// </summary>
internal sealed class ServiceRegistrationCollection
{
    private readonly List<ServiceRegistrationEntry> _entries = new();

    public void Add(ServiceRegistrationEntry entry) => _entries.Add(entry);
    public IReadOnlyList<ServiceRegistrationEntry> GetEntries() => _entries;
}

/// <summary>
/// 统一服务的 HostedService - 负责启动 AutoStart 服务
/// </summary>
internal sealed class UnifiedServiceHostedService : IHostedService
{
    private readonly UnifiedServiceManager _serviceManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnifiedServiceHostedService> _logger;

    public UnifiedServiceHostedService(
        UnifiedServiceManager serviceManager,
        IServiceProvider serviceProvider,
        ILogger<UnifiedServiceHostedService> logger)
    {
        _serviceManager = serviceManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting UnifiedServiceHostedService");

        // 注册所有配置的服务
        var serviceCollection = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<ServiceRegistrationCollection>>();
        if (serviceCollection?.Value != null)
        {
            foreach (var entry in serviceCollection.Value.GetEntries())
            {
                RegisterServiceDynamic(entry);
            }
        }

        // 启动所有 AutoStart 服务
        await _serviceManager.StartAutoStartServicesAsync(cancellationToken);

        _logger.LogInformation("UnifiedServiceHostedService started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping UnifiedServiceHostedService");
        await _serviceManager.DisposeAsync();
        _logger.LogInformation("UnifiedServiceHostedService stopped");
    }

    private void RegisterServiceDynamic(ServiceRegistrationEntry entry)
    {
        // 直接调用预编译的注册委托，避免反射开销
        entry.RegisterAction(_serviceManager);
    }
}
