using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Extensions;

namespace DistributedGameApp.GameServer.Services.Player;

/// <summary>
/// PlayerService 的 DI 注册扩展方法
/// </summary>
public static class PlayerServiceRegistration
{
    /// <summary>
    /// 注册 PlayerService 相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para><strong>架构说明</strong>：</para>
    /// <list type="bullet">
    /// <item><description>PlayerService - 有状态服务（继承 UnifiedPulseServiceBase）</description></item>
    /// <item><description>PlayerHub - 无状态 Hub（实现 IPlayerHub，RPC 入口）</description></item>
    /// <item><description>Hub 与 Service 分离，Hub 通过 IServiceAccessor 访问 Service</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddPlayerServices(this IServiceCollection services)
    {
        // 1. 注册有状态服务（由 UnifiedServiceManager 管理）
        services.AddPulseService<PlayerService>((sp, playerId) =>
        {
            var logger = sp.GetRequiredService<ILogger<PlayerService>>();
            var characterRepository = sp.GetRequiredService<CharacterRepository>();
            return new PlayerService(playerId, logger, characterRepository);
        });

        // 2. 注册无状态 Hub（Singleton，全局复用）
        //    Hub 通过 IContextualServiceAccessor<PlayerService> 访问 Service
        //    Service 实例根据 RequestContext.Current.UserId 自动定位
        services.AddSingleton<IPlayerHub, PlayerHub>();

        return services;
    }
}
