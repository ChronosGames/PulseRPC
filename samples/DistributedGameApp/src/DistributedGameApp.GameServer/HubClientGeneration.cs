using DistributedGameApp.Shared.Hubs;
using PulseRPC;

namespace DistributedGameApp.GameServer;

/// <summary>
/// 触发客户端源生成器为 GameServer 需要调用的 Hub 接口生成代理类
/// </summary>
/// <remarks>
/// GameServer 作为客户端调用其他服务器的 Hub：
/// - IBackendHub: 调用 BackendServer 的后端服务
/// - IBattleHub: 调用 BattleServer 创建/管理战斗
/// </remarks>
[PulseClientGeneration(typeof(IBackendHub))]
[PulseClientGeneration(typeof(IBattleHub))]
internal static class HubClientGeneration
{
    // 这个类仅用于触发源生成器生成客户端代理
    // 不包含任何运行时代码
}

