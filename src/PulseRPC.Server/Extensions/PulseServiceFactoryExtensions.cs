using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;
using PulseRPC.Server.Services.Management;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// <see cref="IPulseServiceFactory{TService}"/> 的 DI 扩展方法
/// </summary>
public static class PulseServiceFactoryExtensions
{
    /// <summary>
    /// 注册服务工厂（使用自定义工厂函数）
    /// </summary>
    /// <typeparam name="TService">服务类型，必须实现 <see cref="IUnifiedPulseService"/></typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="serviceFactory">服务实例工厂函数，接收 ServiceProvider 和 ServiceId，返回服务实例</param>
    /// <param name="configureOptions">配置选项委托（可选）</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para>
    /// 此方法注册以下服务：
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="IPulseServiceFactory{TService}"/> - 服务工厂（单例）</description></item>
    /// <item><description><see cref="IPulseServiceFactoryMetrics"/> - 指标接口（单例）</description></item>
    /// </list>
    /// <para>
    /// <strong>工厂函数</strong>：
    /// </para>
    /// <para>
    /// 工厂函数负责创建服务实例，接收两个参数：
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>IServiceProvider sp</c> - DI 容器，用于解析依赖</description></item>
    /// <item><description><c>string serviceId</c> - 完整的 ServiceId（格式：ServiceName:BusinessId）</description></item>
    /// </list>
    /// <para>
    /// 工厂函数通常需要从 ServiceId 中提取业务 ID，然后创建服务实例。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulseServiceFactory&lt;ChatRoomService&gt;(
    ///     (sp, serviceId) =>
    ///     {
    ///         // 从 ServiceId 中提取 roomId
    ///         var roomId = serviceId.Split(':')[1];
    ///
    ///         // 创建服务实例，注入依赖
    ///         return new ChatRoomService(
    ///             roomId,
    ///             sp.GetRequiredService&lt;ILogger&lt;ChatRoomService&gt;&gt;(),
    ///             sp.GetRequiredService&lt;IChatRepository&gt;());
    ///     },
    ///     options =>
    ///     {
    ///         options.IdleTimeout = TimeSpan.FromMinutes(10);
    ///         options.MaxCachedInstances = 5000;
    ///     });
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseServiceFactory<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, string, TService> serviceFactory,
        Action<PulseServiceFactoryOptions>? configureOptions = null)
        where TService : IUnifiedPulseService
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (serviceFactory == null)
            throw new ArgumentNullException(nameof(serviceFactory));

        // 注册选项
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 注册工厂（单例）
        services.TryAddSingleton<IPulseServiceFactory<TService>>(sp =>
        {
            var options = new PulseServiceFactoryOptions();
            configureOptions?.Invoke(options);

            var logger = sp.GetRequiredService<ILogger<PulseServiceFactory<TService>>>();

            return new PulseServiceFactory<TService>(
                serviceId => serviceFactory(sp, serviceId),
                options,
                logger);
        });

        // 同时注册指标接口
        services.TryAddSingleton<IPulseServiceFactoryMetrics>(sp =>
            (IPulseServiceFactoryMetrics)sp.GetRequiredService<IPulseServiceFactory<TService>>());

        return services;
    }

    /// <summary>
    /// 注册服务工厂（使用 ActivatorUtilities 自动创建）
    /// </summary>
    /// <typeparam name="TService">服务类型，必须实现 <see cref="IUnifiedPulseService"/></typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项委托（可选）</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para>
    /// 此方法使用 <see cref="ActivatorUtilities"/> 自动创建服务实例。
    /// </para>
    /// <para>
    /// <strong>要求</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>服务类必须有一个接受 <c>string serviceId</c> 参数的构造函数</description></item>
    /// <item><description>构造函数的其他参数将从 DI 容器自动解析</description></item>
    /// </list>
    /// <para>
    /// <strong>构造函数示例</strong>：
    /// </para>
    /// <code>
    /// public ChatRoomService(
    ///     string roomId,  // 第一个参数必须是 string
    ///     ILogger&lt;ChatRoomService&gt; logger,
    ///     IChatRepository repository)
    /// {
    ///     ServiceId = $"ChatRoom:{roomId}";
    ///     // ...
    /// }
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册
    /// services.AddPulseServiceFactory&lt;ChatRoomService&gt;(
    ///     options =>
    ///     {
    ///         options.IdleTimeout = TimeSpan.FromMinutes(10);
    ///     });
    ///
    /// // ChatRoomService 构造函数
    /// public ChatRoomService(
    ///     string roomId,  // ServiceId 的业务部分
    ///     ILogger&lt;ChatRoomService&gt; logger,  // 从 DI 容器解析
    ///     IChatRepository repository)  // 从 DI 容器解析
    /// {
    ///     ServiceId = $"ChatRoom:{roomId}";
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseServiceFactory<TService>(
        this IServiceCollection services,
        Action<PulseServiceFactoryOptions>? configureOptions = null)
        where TService : IUnifiedPulseService
    {
        return services.AddPulseServiceFactory<TService>(
            (sp, serviceId) =>
            {
                // 从 ServiceId 中提取业务 ID（假设格式为 ServiceName:BusinessId）
                var parts = serviceId.Split(':', 2);
                var businessId = parts.Length > 1 ? parts[1] : serviceId;

                // 使用 ActivatorUtilities 创建实例，自动注入依赖
                return ActivatorUtilities.CreateInstance<TService>(sp, businessId);
            },
            configureOptions);
    }
}
