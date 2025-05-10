using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 消息处理器扩展方法
/// </summary>
public static class MessageHandlerExtensions
{
    /// <summary>
    /// 添加PulseRPC消息处理系统
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="scanForHandlers">是否自动扫描处理器</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcMessageHandling(this IServiceCollection services,
        bool scanForHandlers = true)
    {
        // 注册处理器工厂和分发器
        services.TryAddSingleton<MessageHandlerFactory>();
        services.TryAddSingleton<IMessageDispatcher, MessageDispatcher>();

        if (scanForHandlers)
        {
            // 自动扫描并注册处理器
            ScanAndRegisterHandlers(services);
        }

        return services;
    }

    /// <summary>
    /// 注册单个消息处理器
    /// </summary>
    /// <typeparam name="THandler">处理器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddMessageHandler<THandler>(this IServiceCollection services)
        where THandler : class, IMessageHandler
    {
        services.TryAddTransient<THandler>();
        return services;
    }

    /// <summary>
    /// 从指定程序集中扫描并注册所有消息处理器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="assemblies">要扫描的程序集</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ScanMessageHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            ScanAssemblyForHandlers(services, assembly);
        }

        return services;
    }

    /// <summary>
    /// 自动扫描应用程序域中的所有程序集并注册处理器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ScanAndRegisterHandlers(this IServiceCollection services)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic &&
                        !a.FullName!.StartsWith("System.") &&
                        !a.FullName.StartsWith("Microsoft."))
            .ToArray();

        foreach (var assembly in assemblies)
        {
            try
            {
                ScanAssemblyForHandlers(services, assembly);
            }
            catch
            {
                // 忽略扫描失败的程序集
            }
        }

        return services;
    }

    /// <summary>
    /// 扫描指定程序集中的处理器并注册到DI
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="assembly">程序集</param>
    private static void ScanAssemblyForHandlers(IServiceCollection services, Assembly assembly)
    {
        // 查找程序集中所有实现了IMessageHandler接口的非抽象类
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass &&
                        !t.IsAbstract &&
                        t.GetInterfaces().Any(i => i == typeof(IMessageHandler)
                                                   || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
                                                   || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))));

        foreach (var handlerType in handlerTypes)
        {
            // 注册为暂时服务
            services.TryAddTransient(handlerType);
        }
    }
}
