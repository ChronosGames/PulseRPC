using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Receivers
{

/// <summary>
/// 玩家事件接收器 - 服务器向客户端推送的玩家事件
/// </summary>
/// <remarks>
/// 这个接口定义了服务器可以向客户端推送的所有玩家相关事件。
/// 客户端需要实现这个接口来接收服务器推送的事件。
/// </remarks>
public interface IPlayerReceiver : IPulseReceiver
{
    /// <summary>
    /// 玩家信息更新事件
    /// </summary>
    /// <param name="playerInfo">更新后的玩家信息</param>
    Task OnPlayerInfoUpdatedAsync(PlayerInfo playerInfo);

    /// <summary>
    /// 玩家升级事件
    /// </summary>
    /// <param name="playerInfo">升级后的玩家信息</param>
    Task OnPlayerLevelUpAsync(PlayerInfo playerInfo);

    /// <summary>
    /// 附近玩家移动事件
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="x">位置X</param>
    /// <param name="y">位置Y</param>
    /// <param name="z">位置Z</param>
    Task OnNearbyPlayerMovedAsync(string playerId, float x, float y, float z);
}
}
