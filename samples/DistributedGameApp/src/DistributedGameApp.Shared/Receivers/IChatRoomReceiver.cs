using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Receivers
{

/// <summary>
/// 聊天室事件接收器 - 服务器向客户端推送的聊天室事件
/// </summary>
/// <remarks>
/// 这个接口定义了服务器可以向客户端推送的所有聊天室相关事件。
/// 客户端需要实现这个接口来接收服务器推送的事件。
/// </remarks>
public interface IChatRoomReceiver : IPulseReceiver
{
    /// <summary>
    /// 收到新消息事件
    /// </summary>
    /// <param name="message">聊天消息</param>
    Task OnMessageReceivedAsync(ChatMessage message);

    /// <summary>
    /// 玩家加入房间事件
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="playerName">玩家名称</param>
    Task OnPlayerJoinedAsync(string playerId, string playerName);

    /// <summary>
    /// 玩家离开房间事件
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="playerName">玩家名称</param>
    Task OnPlayerLeftAsync(string playerId, string playerName);

    /// <summary>
    /// 房间信息更新事件
    /// </summary>
    /// <param name="roomInfo">房间信息</param>
    Task OnRoomInfoUpdatedAsync(RoomInfo roomInfo);
}
}
