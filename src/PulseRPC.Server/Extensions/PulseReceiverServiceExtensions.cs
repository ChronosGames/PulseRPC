using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// PulseReceiver 服务 DI 扩展方法
/// </summary>
/// <remarks>
/// <para>
/// 提供注册推送接收器（<c>[Channel("CLIENT")] : IPulseHub</c>）相关服务的扩展方法。
/// </para>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// // 注册基础设施
/// services.AddPulseReceiverServices();
///
/// // 注册具体的 HubContext（由源代码生成器自动生成）
/// services.AddGameReceiverHubContext();
/// </code>
/// </remarks>
public static class PulseReceiverServiceExtensions
{
    /// <summary>
    /// 添加 PulseReceiver 基础服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    /// <remarks>
    /// 注册以下服务：
    /// <list type="bullet">
    /// <item><description><see cref="IUserConnectionMapping"/> - 用户-连接映射</description></item>
    /// <item><description><see cref="IGroupManager"/> - 组管理器</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddPulseReceiverServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册用户-连接映射（单例）
        services.TryAddSingleton<IUserConnectionMapping, UserConnectionMapping>();

        // 注册组管理器（单例）
        services.TryAddSingleton<IGroupManager, GroupManager>();

        return services;
    }

    /// <summary>
    /// 添加 PulseReceiver 基础服务（带自定义实现）
    /// </summary>
    /// <typeparam name="TUserMapping">用户-连接映射实现类型</typeparam>
    /// <typeparam name="TGroupManager">组管理器实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseReceiverServices<TUserMapping, TGroupManager>(
        this IServiceCollection services)
        where TUserMapping : class, IUserConnectionMapping
        where TGroupManager : class, IGroupManager
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册自定义实现
        services.TryAddSingleton<IUserConnectionMapping, TUserMapping>();
        services.TryAddSingleton<IGroupManager, TGroupManager>();

        return services;
    }

    /// <summary>
    /// 添加 PulseReceiver 基础服务（带工厂方法）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="userMappingFactory">用户-连接映射工厂</param>
    /// <param name="groupManagerFactory">组管理器工厂</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseReceiverServices(
        this IServiceCollection services,
        Func<IServiceProvider, IUserConnectionMapping>? userMappingFactory = null,
        Func<IServiceProvider, IGroupManager>? groupManagerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册用户-连接映射
        if (userMappingFactory != null)
        {
            services.TryAddSingleton(userMappingFactory);
        }
        else
        {
            services.TryAddSingleton<IUserConnectionMapping, UserConnectionMapping>();
        }

        // 注册组管理器
        if (groupManagerFactory != null)
        {
            services.TryAddSingleton(groupManagerFactory);
        }
        else
        {
            services.TryAddSingleton<IGroupManager, GroupManager>();
        }

        return services;
    }
}

