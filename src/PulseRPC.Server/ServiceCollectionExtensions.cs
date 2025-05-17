using System.Reflection;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Network;

namespace PulseRPC.Server;

/// <summary>
/// 消息处理器扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPulseServerServices(this IServiceCollection services)
    {
        // 基础设施服务
        services.AddSingleton<HandlerRegistry>();

        // 线程池配置
        services.AddSingleton(new ThreadPoolConfiguration
        {
            WorkerThreads = Environment.ProcessorCount * 2,
            HighPriorityThreads = Math.Max(2, Environment.ProcessorCount / 2),
            LowLatencyThreads = Math.Max(2, Environment.ProcessorCount / 2)
        });

        // 线程池管理
        services.AddSingleton<HandlerThreadPoolManager>();

        // 注册主线程工作器
        services.AddHostedService<MainThreadWorker>();

        // 处理器扫描和注册
        services.AddSingleton<HandlerScanner>();

        // 服务注册
        services.AddSingleton<IPulseService, PulseService>();

        // 网络服务
        services.AddSingleton<NetworkServer>();

        // 游戏服务器启动
        services.AddSingleton<PulseServerStartup>();

        // 游戏系统服务
        // services.AddSingleton<IGameWorld, GameWorld>();
        // services.AddSingleton<IBattleSystem, BattleSystem>();
        // services.AddSingleton<IItemDatabase, ItemDatabase>();

        // 注册所有消息处理器
        RegisterMessageHandlers(services);

        return services;
    }

    private static void RegisterMessageHandlers(IServiceCollection services)
    {
        // 扫描程序集查找所有Handler类型
        var assembliesToScan = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic
                        && !a.GetName().Name!.StartsWith("System", StringComparison.Ordinal)
                        && !a.GetName().Name!.StartsWith("Microsoft", StringComparison.Ordinal)
                        && a.GetName().Name! != "netstandard"
                        && a.GetName().Name! != "mscorlib");

        foreach (var assembly in assembliesToScan)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<HandlerAttribute>() != null)
                .ToList();

            foreach (var handlerType in handlerTypes)
            {
                // 将处理器注册为瞬态服务
                services.AddSingleton(handlerType);
            }
        }
    }
}
