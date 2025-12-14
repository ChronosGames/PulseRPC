using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Hubs;
using PulseRPC;
using PulseRPC.Client;

namespace DistributedGameApp.BackendServer;

/// <summary>
/// 触发客户端源生成器为 BackendServer 需要调用的 Hub 接口生成代理类
/// </summary>
/// <remarks>
/// BackendServer 作为客户端调用其他服务器的 Hub：
/// - IGameServerInternalHub: 调用 GameServer 通知匹配结果
/// - IBattleHub: 调用 BattleServer 创建战斗房间
/// </remarks>
[PulseClientGeneration(typeof(IGameServerInternalHub))]
[PulseClientGeneration(typeof(IBattleHub))]
internal static class HubClientGeneration
{
    // 这个类仅用于触发源生成器生成客户端代理
    // 不包含任何运行时代码
}

public class HubProxyFactory : IHubProxyFactory
{
    public THub? Create<THub>(IClientChannel channel) where THub : class, IPulseHub
    {
        return channel.GetHub<THub>();
    }
}
