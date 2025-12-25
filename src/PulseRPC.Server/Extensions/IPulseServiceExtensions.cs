using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Scheduling;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// 扩展方法,用于配置 IPulseService 线程调度和灾难隔离功能
/// </summary>
/// <remarks>
/// 提供 DI 容器配置方法,用于注册 IPulseService 相关的调度、健康监控和线程亲和性组件。
/// </remarks>
public static class IPulseServiceExtensions
{
    /// <summary>
    /// 添加 IPulseService 线程调度和灾难隔离支持 (使用默认配置)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合,支持链式调用</returns>
    public static IServiceCollection AddIPulseServiceScheduling(this IServiceCollection services)
    {
        return AddIPulseServiceScheduling(services, null, null);
    }

    /// <summary>
    /// 添加 IPulseService 线程调度和灾难隔离支持 (使用自定义配置)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureScheduling">调度器配置委托</param>
    /// <param name="configureHealthMonitor">健康监控配置委托</param>
    /// <returns>服务集合,支持链式调用</returns>
    public static IServiceCollection AddIPulseServiceScheduling(
        this IServiceCollection services,
        Action<ServiceSchedulingOptions>? configureScheduling,
        Action<HealthMonitorOptions>? configureHealthMonitor)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册配置选项
        services.Configure<ServiceSchedulingOptions>(options =>
        {
            configureScheduling?.Invoke(options);
            options.Validate();
        });

        services.Configure<HealthMonitorOptions>(options =>
        {
            configureHealthMonitor?.Invoke(options);
            options.Validate();
        });

        // 注册核心组件为单例
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceSchedulingOptions>>();
            return new ConsistentHashRing(
                options.Value.WorkerThreadCount,
                options.Value.VirtualNodesPerThread);
        });

        services.AddSingleton<ThreadAffinityManager>();
        services.AddSingleton<ServiceInstanceHealthMonitor>();
        services.AddSingleton<CircuitBreakerPolicy>();

        return services;
    }

    /// <summary>
    /// 添加 IPulseService 线程调度和灾难隔离支持 (从配置文件读取)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="schedulingSectionName">调度器配置节名称 (默认: "ServiceScheduling")</param>
    /// <param name="healthMonitorSectionName">健康监控配置节名称 (默认: "HealthMonitor")</param>
    /// <returns>服务集合,支持链式调用</returns>
    public static IServiceCollection AddIPulseServiceScheduling(
        this IServiceCollection services,
        IConfiguration configuration,
        string schedulingSectionName = "ServiceScheduling",
        string healthMonitorSectionName = "HealthMonitor")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return AddIPulseServiceScheduling(
            services,
            options =>
            {
                var section = configuration.GetSection(schedulingSectionName);
                if (section.Exists())
                {
                    section.Bind(options);
                }
            },
            options =>
            {
                var section = configuration.GetSection(healthMonitorSectionName);
                if (section.Exists())
                {
                    section.Bind(options);
                }
            });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 别名方法 - 向后兼容 Builder/IPulseServiceExtensions.cs 中的方法名
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 添加 IPulseService 基础设施 (使用默认配置)
    /// </summary>
    /// <remarks>
    /// 这是 <see cref="AddIPulseServiceScheduling(IServiceCollection)"/> 的别名方法。
    /// </remarks>
    public static IServiceCollection AddIPulseServiceInfrastructure(this IServiceCollection services)
        => AddIPulseServiceScheduling(services);

    /// <summary>
    /// 添加 IPulseService 基础设施 (使用自定义配置)
    /// </summary>
    /// <remarks>
    /// 这是 <see cref="AddIPulseServiceScheduling(IServiceCollection, Action{ServiceSchedulingOptions}?, Action{HealthMonitorOptions}?)"/> 的别名方法。
    /// </remarks>
    public static IServiceCollection AddIPulseServiceInfrastructure(
        this IServiceCollection services,
        Action<ServiceSchedulingOptions>? configureScheduling,
        Action<HealthMonitorOptions>? configureHealthMonitoring)
        => AddIPulseServiceScheduling(services, configureScheduling, configureHealthMonitoring);

    /// <summary>
    /// 添加 IPulseService 基础设施 (从配置文件读取)
    /// </summary>
    /// <remarks>
    /// 这是 <see cref="AddIPulseServiceScheduling(IServiceCollection, IConfiguration, string, string)"/> 的别名方法。
    /// </remarks>
    public static IServiceCollection AddIPulseServiceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string schedulingSectionName = "ServiceScheduling",
        string healthMonitoringSectionName = "HealthMonitoring")
        => AddIPulseServiceScheduling(services, configuration, schedulingSectionName, healthMonitoringSectionName);
}

/// <summary>
/// IPulseService 配置使用示例
/// </summary>
/// <example>
/// <code>
/// // 在 Program.cs 或 Startup.cs 中:
///
/// // 选项 1: 使用默认配置
/// services.AddIPulseServiceScheduling();
///
/// // 选项 2: 自定义配置
/// services.AddIPulseServiceScheduling(
///     schedulingOptions =>
///     {
///         schedulingOptions.WorkerThreadCount = 16;
///         schedulingOptions.IdleInstanceTimeout = TimeSpan.FromMinutes(10);
///         schedulingOptions.VirtualNodesPerThread = 150;
///     },
///     healthMonitorOptions =>
///     {
///         healthMonitorOptions.FailureThreshold = 3;
///         healthMonitorOptions.CoolingPeriod = TimeSpan.FromMinutes(1);
///         healthMonitorOptions.ProbeRequestLimit = 5;
///         healthMonitorOptions.ProbeSuccessThreshold = 3;
///     });
///
/// // 选项 3: 从配置文件读取
/// services.AddIPulseServiceScheduling(Configuration);
///
/// // appsettings.json 示例:
/// {
///   "ServiceScheduling": {
///     "WorkerThreadCount": 16,
///     "IdleInstanceTimeout": "00:05:00",
///     "VirtualNodesPerThread": 150
///   },
///   "HealthMonitor": {
///     "FailureThreshold": 3,
///     "CoolingPeriod": "00:01:00",
///     "ProbeRequestLimit": 5,
///     "ProbeSuccessThreshold": 3
///   }
/// }
///
/// // 服务使用示例:
/// public class ChatRoomService : IPulseHub, IUnifiedPulseService
/// {
///     public string ServiceType => "ChatRoom";
///     public string ServiceId { get; }
///
///     public ChatRoomService(string roomId)
///     {
///         ServiceId = roomId;
///     }
///
///     public async Task&lt;string&gt; SendMessageAsync(string message)
///     {
///         // 业务逻辑...
///         return $"Message sent";
///     }
/// }
/// </code>
/// </example>
public static class IPulseServiceRegistrationExamples
{
    // 文档类 - 使用示例见上方 XML 文档
}
