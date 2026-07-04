using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;
using PulseRPC.Server.Services.Management;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// <see cref="IPulseHubFactory{THub, TService}"/> 的 DI 扩展方法
/// </summary>
public static class PulseHubFactoryExtensions
{
    /// <summary>
    /// 注册 Hub 工厂（使用自定义工厂函数）
    /// </summary>
    /// <typeparam name="THub">Hub 类型</typeparam>
    /// <typeparam name="TService">Service 类型，必须实现 <see cref="IPulseService"/></typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="hubFactory">Hub 实例工厂函数，接收 Service 实例，返回 Hub 实例</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para>
    /// 此方法会同时注册 <see cref="IPulseHubFactory{THub, TService}"/> 和 <see cref="IPulseServiceFactory{TService}"/>（如果尚未注册）。
    /// </para>
    /// <para>
    /// <strong>Hub 工厂函数</strong>：
    /// </para>
    /// <para>
    /// 工厂函数负责创建 Hub 实例，接收一个 Service 实例作为参数：
    /// </para>
    /// <code>
    /// service => new ChatRoomHub(service)
    /// </code>
    /// <para>
    /// Hub 可以在构造函数中接收 Service 实例和其他依赖：
    /// </para>
    /// <code>
    /// (service) => ActivatorUtilities.CreateInstance&lt;ChatRoomHub&gt;(sp, service)
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 1. 简单注册
    /// services.AddPulseHubFactory&lt;ChatRoomHub, ChatRoomService&gt;(
    ///     service => new ChatRoomHub(service));
    ///
    /// // 2. 注入其他依赖
    /// services.AddPulseHubFactory&lt;ChatRoomHub, ChatRoomService&gt;(
    ///     (sp, service) => new ChatRoomHub(
    ///         service,
    ///         sp.GetRequiredService&lt;ILogger&lt;ChatRoomHub&gt;&gt;()));
    ///
    /// // 3. 使用
    /// var hubFactory = serviceProvider.GetRequiredService&lt;IPulseHubFactory&lt;ChatRoomHub, ChatRoomService&gt;&gt;();
    /// var hub = await hubFactory.GetOrCreateAsync("ChatRoom:room-1");
    /// await hub.SendMessageAsync("Hello");
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseHubFactory<THub, TService>(
        this IServiceCollection services,
        Func<TService, THub> hubFactory)
        where THub : class
        where TService : IPulseService
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (hubFactory == null)
            throw new ArgumentNullException(nameof(hubFactory));

        // 注册 HubFactory（单例）
        services.TryAddSingleton<IPulseHubFactory<THub, TService>>(sp =>
        {
            var serviceFactory = sp.GetRequiredService<IPulseServiceFactory<TService>>();
            var logger = sp.GetRequiredService<ILogger<PulseHubFactory<THub, TService>>>();

            return new PulseHubFactory<THub, TService>(
                serviceFactory,
                hubFactory,
                logger);
        });

        return services;
    }

    /// <summary>
    /// 注册 Hub 工厂（可注入其他依赖）
    /// </summary>
    /// <typeparam name="THub">Hub 类型</typeparam>
    /// <typeparam name="TService">Service 类型，必须实现 <see cref="IPulseService"/></typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="hubFactory">Hub 实例工厂函数，接收 IServiceProvider 和 Service 实例，返回 Hub 实例</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para>
    /// 此重载允许在创建 Hub 时从 DI 容器解析其他依赖。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulseHubFactory&lt;ChatRoomHub, ChatRoomService&gt;(
    ///     (sp, service) => new ChatRoomHub(
    ///         service,
    ///         sp.GetRequiredService&lt;ILogger&lt;ChatRoomHub&gt;&gt;(),
    ///         sp.GetRequiredService&lt;IHubContext&gt;()));
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseHubFactory<THub, TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService, THub> hubFactory)
        where THub : class
        where TService : IPulseService
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (hubFactory == null)
            throw new ArgumentNullException(nameof(hubFactory));

        // 注册 HubFactory（单例）
        services.TryAddSingleton<IPulseHubFactory<THub, TService>>(sp =>
        {
            var serviceFactory = sp.GetRequiredService<IPulseServiceFactory<TService>>();
            var logger = sp.GetRequiredService<ILogger<PulseHubFactory<THub, TService>>>();

            return new PulseHubFactory<THub, TService>(
                serviceFactory,
                service => hubFactory(sp, service),
                logger);
        });

        return services;
    }

    /// <summary>
    /// 注册 Hub 工厂（使用 ActivatorUtilities 自动创建）
    /// </summary>
    /// <typeparam name="THub">Hub 类型</typeparam>
    /// <typeparam name="TService">Service 类型，必须实现 <see cref="IPulseService"/></typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para>
    /// 此方法使用 <see cref="ActivatorUtilities"/> 自动创建 Hub 实例。
    /// </para>
    /// <para>
    /// <strong>要求</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>Hub 类必须有一个接受 <typeparamref name="TService"/> 参数的构造函数</description></item>
    /// <item><description>构造函数的其他参数将从 DI 容器自动解析</description></item>
    /// </list>
    /// <para>
    /// <strong>构造函数示例</strong>：
    /// </para>
    /// <code>
    /// public ChatRoomHub(
    ///     ChatRoomService service,  // Service 实例
    ///     ILogger&lt;ChatRoomHub&gt; logger)  // 从 DI 容器解析
    /// {
    ///     _service = service;
    ///     _logger = logger;
    /// }
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册
    /// services.AddPulseHubFactory&lt;ChatRoomHub, ChatRoomService&gt;();
    ///
    /// // ChatRoomHub 构造函数
    /// public ChatRoomHub(
    ///     ChatRoomService service,  // 自动注入
    ///     ILogger&lt;ChatRoomHub&gt; logger)  // 从 DI 容器解析
    /// {
    ///     _service = service;
    ///     _logger = logger;
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddPulseHubFactory<THub, TService>(
        this IServiceCollection services)
        where THub : class
        where TService : IPulseService
    {
        return services.AddPulseHubFactory<THub, TService>(
            (sp, service) => ActivatorUtilities.CreateInstance<THub>(sp, service));
    }
}
