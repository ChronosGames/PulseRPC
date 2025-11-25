using DistributedGameApp.Shared.Domain.Matchmaking;
using PulseRPC;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Hubs;

/// <summary>
/// GameServer 内部 RPC 接口 - 用于接收其他服务器的回调通知
/// </summary>
/// <remarks>
/// <para>
/// [Channel("GameServer")] 表示这个接口由 GameServer 提供。
/// [Authorize(Role = RoleTypes.Internal)] 表示需要内网服务器认证。
/// </para>
/// </remarks>
[Channel("GameServer")]
[Authorize(Role = RoleTypes.Internal)]
public interface IGameServerInternalHub : IPulseHub
{
    /// <summary>
    /// 匹配成功回调 - BackendServer 调用此方法通知 GameServer 匹配结果
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="notification">匹配成功通知</param>
    /// <returns>是否成功处理</returns>
    Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification);

    /// <summary>
    /// 匹配取消回调
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="reason">取消原因</param>
    /// <returns>是否成功处理</returns>
    Task<bool> OnMatchCancelledAsync(string playerId, string reason);
}
